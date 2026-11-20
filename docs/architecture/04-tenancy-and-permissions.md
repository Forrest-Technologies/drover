# 4. Tenancy, row-level security, and bot permissions

Two layers, kept separate on purpose:

| Layer | Separates | Enforced by | Fails how |
| --- | --- | --- | --- |
| 1. Tenancy | One user from another, and one group from another. Every user has a personal space; a shared space is a group; inside a group, private rows belong to one user. | PostgreSQL row-level security | Zero rows, or a database error |
| 2. Bot permissions | One bot from another inside a space | The engine, backed by constraints and one SQL function | A refused tool call |

A bug in layer 2 can let a bot overreach inside its own space. It cannot cross a space boundary, because layer 1 does not depend on application code being right.

The SQL and the EF Core mechanism in this section were run against PostgreSQL 16 and EF Core 10 with Npgsql while writing this document, with a connection pool of one. The observed results are quoted where they matter.

---

## Layer 1: tenancy

### Roles

| Role | Login | Attributes | Used for |
| --- | --- | --- | --- |
| `drover_owner` | Migrator only | Owns the schema and tables | Migrations. Never used to serve a request. |
| `drover_app` | Yes | `NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE`, owns nothing | Every request and every background job |
| `drover_system` | No | `BYPASSRLS`, table privileges granted one by one | Owns the functions in schema `sys`. Nobody can log in as it or `SET ROLE` to it. |

`DatabaseBootstrap` creates the roles and the schema-level grants. It runs before the migrations, over a superuser connection (granting `BYPASSRLS` needs one), holds an advisory lock, and is safe to run again from several servers at once. `drover_owner` is made a member of `drover_system` so that a migration can hand a `sys` function to it. `drover_owner` creates the `sys` schema's functions, so it owns the schema; `drover_app` has `USAGE` on `sys` and on `public`, and `drover_owner` alone has `CREATE`. `PUBLIC` has no privileges on the database, `public`, or `sys`, which also takes away temporary tables.

`drover_app` has `SELECT, INSERT, UPDATE, DELETE` on mutable tenant tables unless a special case below narrows it. `audit_log` is append-only: only `SELECT, INSERT` are granted. `threads` permits `UPDATE` only on `id`, `space_id`, `audience_user_id`, `bot_id`, `title`, `origin`, `last_activity_at`, `archived_at`, and `is_main`. No tenant table permits `TRUNCATE`. The isolation suite checks these declared grants independently of the catalog, alongside behavioral isolation checks. The role has `EXECUTE` on `sys` functions, and nothing else. It cannot create objects, alter tables, or change policies. Observed: `ALTER TABLE threads DISABLE ROW LEVEL SECURITY` as `drover_app` returns "must be owner of table threads", and `SET ROLE drover_system` returns "permission denied".

### Policies

Two helper functions read the transaction's identity. They return null when nothing is set, and a comparison with null is never true, so an unset context sees nothing.

```sql
CREATE FUNCTION sys.current_space() RETURNS uuid LANGUAGE sql STABLE
  AS $$ SELECT nullif(current_setting('drover.space_id', true), '')::uuid $$;

CREATE FUNCTION sys.current_user_id() RETURNS uuid LANGUAGE sql STABLE
  AS $$ SELECT nullif(current_setting('drover.user_id', true), '')::uuid $$;
```

Every space-owned table gets exactly this, emitted by one migration helper so it cannot be written slightly wrong by hand:

```sql
ALTER TABLE threads ENABLE ROW LEVEL SECURITY;
ALTER TABLE threads FORCE ROW LEVEL SECURITY;

CREATE POLICY space_isolation ON threads FOR ALL
  USING      (space_id = sys.current_space())
  WITH CHECK (space_id = sys.current_space());
```

`FORCE` makes the policy apply to the table owner too. `USING` filters what can be read, updated, or deleted. `WITH CHECK` rejects inserting a row for another space or moving a row into one.

Tables with an audience (`threads`, `messages`, `run_events`, `tool_calls`, `files`, `secrets`, `tool_connection_credentials`, `oauth_flows`, `memories`) add a restrictive policy, which is combined with the first using AND. `sys.everyone()` returns the all-zero UUID.

```sql
CREATE POLICY audience ON threads AS RESTRICTIVE FOR ALL
  USING      (audience_user_id IN (sys.everyone(), sys.current_user_id()))
  WITH CHECK (audience_user_id IN (sys.everyone(), sys.current_user_id()));
```

So the rule the owner asked for holds in the database: **a row is reachable by its user, or by the group when it is not private to a user.** In a personal space the group is one person, so the two rules coincide.

Children inherit the audience through their foreign key:

```sql
FOREIGN KEY (space_id, thread_id, audience_user_id)
  REFERENCES threads (space_id, id, audience_user_id) ON UPDATE CASCADE
```

Observed on PostgreSQL 16, two users in one space, connected as `drover_app`:

| Action | Result |
| --- | --- |
| User 2 lists threads and messages | Sees the shared thread only. User 1's private thread and its message do not exist for them. |
| A message is inserted with a wider audience than its thread | Foreign key error |
| User 2 inserts a row addressed to user 1 | Error, "new row violates row-level security policy audience" |
| User 1 shares the private thread | One `UPDATE`; the cascade updates the messages under forced row-level security; user 2 then sees both |

The migration helper `IsolateBySpace(table, audience: true)` emits both policies.

User-owned tables (`user_sessions`, `push_subscriptions`) use `user_id = sys.current_user_id()` alone. Special cases, each written out in the migration and each covered by a named test:

| Table | Policy |
| --- | --- |
| `threads` | The two policies above, plus a check constraint `audience_user_id IN (sys.everyone(), owner_user_id)`, and no `UPDATE` privilege on `owner_user_id` for the app role |
| `spaces` | `id = sys.current_space()`, all commands. A space is created in a transaction whose space is already the new id. |
| `memberships` | `IsolateBySpace` (space match, all commands), plus a second policy for select only: `user_id = sys.current_user_id()`. Permissive policies combine with OR, so a user lists their own memberships before a space is chosen, and writes need the space. |
| `users` | Select own row only. Insert and update own row only (`id = sys.current_user_id()`); the app role has no `DELETE` privilege. |
| `user_profiles` | Self-only insert, update and own reads; no DELETE privilege, because every Identity user requires its profile. A SELECT-only policy additionally exposes profiles of members of the current space, provided the caller is also a member. Only `user_id` and `display_name` live here, never Identity credentials or contact details. |
| `user_claims`, `user_logins`, `user_tokens` | The rest of the ASP.NET Core Identity tables, renamed. `IsolateByUser`: `user_id = sys.current_user_id()`. No passkey table until passkeys are offered (Phase 2). |

| `audit_log` | Space match; `drover_app` has no `UPDATE` or `DELETE` privilege |
| `files` | The space and audience policies. A project file is shared, so audience alone would let every member write it. Trigger `trg_files_project_insert / trg_files_project_update / trg_files_project_delete` allows a project-file insert, update, or delete only for an owner or admin, and on delete it returns the file's quota and inserts `pending_file_deletions`. |
| `pending_file_deletions` | Space match (`IsolateBySpace`) with `UPDATE` limited to `(id, cleanup_claim_until)`. Trigger `trg_pending_file_deletions_access` restricts inserts, updates, and deletes to owners, admins, and `drover_system`, requires `storage_key` on insert to match `{space_id}/{id}`, and forbids modifying anything except `cleanup_claim_until`. |
| `pending_upload_deletions` | Space and audience match (`IsolateBySpace(audience: true)`), no app `INSERT`, and `UPDATE` limited to `(id, cleanup_claim_until)`. Only the guarded delete trigger on the real `public.files` table inserts a receipt. Another trigger permits a self-update of `id` for the generic isolation control but rejects any actual identity change. The app may claim and delete only a receipt visible in its current space and audience. |
| `file_byte_reservations` | Space match (`IsolateBySpace`). Trigger `trg_file_byte_reservations_access` restricts writes to owners, admins, and `drover_system`, rejects visible stored or pending file identities, and prevents identity changes. Expired holds are reclaimed only on administrator project-upload paths; member attachment uploads leave them charged until that cleanup. |

In C# the helpers are extensions on `MigrationBuilder`, following the guide's preference for extension builders. They live in `Drover.Data.Tenancy`, not in the `Migrations` folder, because `.editorconfig` treats that folder as generated code:

```csharp
public static MigrationBuilder IsolateBySpace(this MigrationBuilder migration, string table, bool audience = false);
public static MigrationBuilder IsolateByUser(this MigrationBuilder migration, string table);
```

`IsolateBySpace` enables and forces row-level security, creates `space_isolation`, creates `audience` when asked, and grants `SELECT, INSERT, UPDATE, DELETE` to `drover_app`. `IsolateByUser` does the same with `user_isolation` on `user_id`. `table` is always a compile-time table name from a migration, never user input.

### Setting the tenant: EF Core and connection pooling

The rule: **all tenant data access happens inside an explicit transaction, and the transaction's first statement sets the identity with `set_config(..., true)`.** The third argument makes the setting local to the transaction. PostgreSQL discards it at commit or rollback, so nothing can travel with a pooled connection.

How it is wired:

1. `TenantContext` is a scoped service holding the space id and user id. It is set once per request by middleware after authentication and membership check, and once per job by the worker. It refuses to change space once set.
2. `TenantTransactionInterceptor` is an EF Core `DbTransactionInterceptor`. When any transaction starts on the context, it runs the `set_config` statement on that transaction before anything else.
3. `TenantGuardInterceptor` is a `DbCommandInterceptor` that throws when a command runs with no transaction. Forgetting the transaction is a loud failure in development, and would return zero rows anyway.
4. `DroverDbContext` is registered by `AddDroverData` with `AddDbContext` (scoped), not `AddDbContextPool`. Pooled contexts share interceptors across scopes; a scoped context gets the interceptor for its own `TenantContext`. Database connections are still pooled by Npgsql.

```csharp
namespace Drover.Data;

public sealed class TenantTransactionInterceptor(TenantContext tenant) : DbTransactionInterceptor
{
    #region Private Fields

    private const string SetTenantSql =
        "SELECT set_config('drover.space_id', @space, true), set_config('drover.user_id', @user, true)";

    #endregion

    #region Public Methods

    public override async ValueTask<DbTransaction> TransactionStartedAsync(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result,
        CancellationToken cancellationToken = default)
    {
        await using var command = BuildSetTenantCommand(connection, result);
        await command.ExecuteNonQueryAsync(cancellationToken);
        return result;
    }

    public override DbTransaction TransactionStarted(
        DbConnection connection,
        TransactionEndEventData eventData,
        DbTransaction result) =>
        throw new NotSupportedException("Tenant transactions are opened asynchronously.");

    #endregion

    #region Private Methods

    private DbCommand BuildSetTenantCommand(DbConnection connection, DbTransaction transaction)
    {
        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = SetTenantSql;
        command.Parameters.Add(new NpgsqlParameter("space", tenant.SpaceId?.ToString() ?? string.Empty));
        command.Parameters.Add(new NpgsqlParameter("user", tenant.UserId?.ToString() ?? string.Empty));
        return command;
    }

    #endregion
}
```

The overridden method keeps EF Core's own name, `TransactionStartedAsync`. `CODE_STYLE.md` bans the suffix on Drover's methods; an override cannot rename its base member, and analyzer `DRV0001` exempts overrides and interface implementations for that reason.

Callers do not use the interceptor directly. A service opens a `TenantScope` and calls repositories inside it. Repositories never open or commit one:

```csharp
await using var work = await tenantScope.Begin(cancellationToken);
var thread = await threads.GetThread(threadId, cancellationToken);
thread.Title = title;
await work.Commit(cancellationToken);
```

`Begin` returns a `TenantWork`. `Commit` saves the tracked changes and then commits; disposing without committing rolls back and clears all tracked entities, including changes accepted by a prior SaveChanges in the rolled-back transaction. A failed commit is also uncommitted, so disposal clears its tracking before another scope reuses the context. Successful scopes retain tracking. Scopes do not nest. The context also sets `AutoTransactionBehavior.Never`, so `SaveChanges` never opens a transaction of its own: only a `TenantScope` opens one, and a request that touches no tenant data opens none.

API endpoints get this from an endpoint filter that opens the scope before the handler and commits after it, so most handlers contain no transaction code. Streaming endpoints and workers open short scopes themselves.

**Observed with a pool of exactly one connection**, reused for every call:

| Call | Result |
| --- | --- |
| Count threads as space A | 1 |
| Count threads as space B, same physical connection | 1 |
| Count threads with no tenant set | 0 |
| Query outside a transaction | Throws "Tenant data was queried outside a tenant transaction" |
| Space A inserts a row with space B's id | PostgreSQL error 42501, "new row violates row-level security policy" |

The same results held with Npgsql's `No Reset On Close=true`, which disables the pool's own state reset. Isolation does not rely on the pool cleaning up.

Settings that interact, and the decision for each:

| Setting | Decision | Reason |
| --- | --- | --- |
| EF Core retrying execution strategy | Off | It rejects user-started transactions unless wrapped, and replays the whole delegate. Retries belong at the job level, where the run engine already has them. |
| Npgsql multiplexing | Off | Its current status could not be verified, and it does not suit explicit transactions. |
| `No Reset On Close` | Left at the default (reset on) | A second line of defense that costs nothing |
| External pooler in transaction mode | Compatible | The identity never outlives the transaction |
| EF Core global query filters on `space_id` | Not used | One mechanism, tested hard. A filter can be bypassed with `IgnoreQueryFilters`; a policy cannot. |
| `SaveChanges` opening its own transaction | Off (`AutoTransactionBehavior.Never`) | An implicit transaction would start with an empty tenant. Outside a scope the guard throws instead. |
| Stamping `space_id` on insert | A `SaveChanges` interceptor fills `SpaceId` on new `ISpaceOwned` entities from `TenantContext` | Convenience only. `WITH CHECK` is what enforces it. |

### Cross-tenant work: the `sys` path

Some work must look across spaces: claiming jobs, finding a user at sign-in, cleanup. It goes through functions in schema `sys`, and nowhere else.

Rules for a `sys` function:

1. Owned by `drover_system`, `SECURITY DEFINER`, with `SET search_path = pg_catalog, public, pg_temp` pinned (`pg_temp` last, so a temporary object cannot shadow a table), and every table name in the body schema-qualified.
2. Does one narrow thing and returns the minimum: ids, not rows. `claim_runs` returns `(run_id, space_id, principal_user_id)`; the worker then opens a normal tenant scope and reads the run under row-level security.
3. `EXECUTE` revoked from `PUBLIC`, granted to `drover_app`.
4. Writes one row to `sys.audit`: function, caller, time, row count.
5. Listed in `tests/Drover.Data.Tests/SystemFunctions.manifest`. A test compares the manifest with the catalog. Adding, removing, or changing the signature of a `sys` function without editing the manifest fails the build, which puts every change to the bypass surface in front of a reviewer.

Initial set:

| Function | Purpose |
| --- | --- |
| `sys.claim_runs(worker, limit)` | Lease runnable runs across spaces. Claimable: a root run that is `queued`, or `running` with an expired lease (its `attempt` goes up by one), or `waiting` with no `pending_approval` tool call anywhere in its tree, or `waiting` with a cancel request. The lease is 45 seconds. For the last case the function reads one more column, `runs.cancel_requested_at`, and `drover_system` is granted `SELECT` on that column and nothing else new. |
| `sys.claim_due_schedules(now, claim_until, limit)` | Claim the enabled schedules, in every space, whose `next_run_at` is at or before `now`, and move each one's `next_run_at` to `claim_until` in the same statement (`FOR UPDATE SKIP LOCKED`), so two servers never claim one schedule for one due time. A schedule is due whether or not anyone is signed in, so this has no tenant. PostgreSQL cannot read a cron expression, so the function moves `next_run_at` only as far as the claim. Both moments are checked against the database's own clock, never only against each other: `now` must lie between an hour before and a minute after the database's `now()`, and `claim_until` at least a second after `now` and at most an hour after `now()`, so a caller cannot claim, and so push back, schedules by naming a moment in the future, nor hold one for more than an hour; the scheduler then writes the real next time inside the schedule's own tenant scope, and sends nothing unless the row still holds its claim ([03-run-lifecycle.md](03-run-lifecycle.md#schedules)). Returns the space, schedule, and owner ids only; never the prompt. `drover_system` is granted `SELECT` on five columns of `bot_schedules` (`id`, `space_id`, `owner_user_id`, `enabled`, `next_run_at`) and `UPDATE` on `next_run_at`. |
| `sys.find_user_for_sign_in(email)` | Return the user id for one email, compared by its normalized form, and nothing else. The caller then reads the user row in a transaction with `drover.user_id` set to that id, which the `users` policy allows. |
| `sys.memberships_for_user()` | Used once after sign-in, before a space is chosen. Takes no argument: it answers for `sys.current_user_id()` only, so it can never list another person's spaces. Returns each space's id, kind, name, slug, and the user's role, because a person cannot choose a space from ids. |
| `sys.resolve_oauth_state(state_hash)` | Map an OAuth callback to its space and user |
| `sys.tool_sign_ins_running_out(before, not_tried_since, not_renewed_since, limit)` | Find active tool server sign-ins whose access token runs out before a moment, in every space. Background renewal has no tenant. Those tried and not renewed since `not_tried_since` are left out and those never tried come first, so the sign-ins of one tool server that is down cannot fill every pass. Those renewed since `not_renewed_since` are left out too, and the spaces take turns (one sign-in from each before a second from any), so one space with many short-lived sign-ins cannot fill every pass either. Returns the space, the audience, the connection, and the credential id; never a secret id. `drover_system` is granted `SELECT` on eight columns of `tool_connection_credentials` and four of `tool_connections` for it, none of them a secret's. |
| `sys.forget_tool_credentials(connection_id)` | Delete every credential of one tool connection and the secret of each, whoever they belong to, so removing a connection takes every sign-in to it. A person's own tokens are invisible to anybody else's transaction, so the remover cannot delete them; this crosses audiences inside one space and nothing else: it reads the space from the transaction, touches no row of another space, and returns a count. `drover_system` is granted `DELETE` on `tool_connection_credentials` and `secrets`, `SELECT` on `secrets (id, space_id)`, one more column of `tool_connection_credentials`, and `EXECUTE` on `sys.current_space()`. |
| `sys.clear_project_threads(project_id)` | Clear `project_id` on every conversation in one project within the caller's own space when that project is removed. Removing a project preserves every conversation, including private conversations of other space members that row-level security hides from the remover, so clearing the foreign key crosses audiences inside the space and nothing else: it checks that the caller holds `owner` or `admin` membership in `sys.current_space()`, touches no row of another space, and returns `void` so it discloses no private row count. A `BEFORE UPDATE OF project_id` trigger on `threads` (`trg_threads_project_owner`) also enforces `owner_user_id = sys.current_user_id()` for `drover_app`, while permitting `drover_system` inside this function. `drover_system` is granted `SELECT (id, space_id, project_id)` and `UPDATE (project_id)` on `threads`. |
| `sys.sandboxes_to_sweep(now, stuck_before, limit)` | Find, in every space, the sandboxes the server's sweep must act on: retiring ones whose `retire_after` has passed, retiring ones whose `expires_at` is still ahead (they may be running and are paused), and rows left `creating` since before `stuck_before`, an age read from the row's version 7 id. Retired and interrupted sandboxes must be cleared up whether or not their bot is used again, so this has no tenant. Returns the space and sandbox id only; never a secret id or the provider's id. Each sandbox is then handled in its own space's tenant scope ([07-sandbox.md](07-sandbox.md)). `drover_system` is granted `SELECT` on two more columns of `sandboxes`, `retire_after` and `expires_at`. |
| `sys.expired_uploads(limit)` | Find user-created conversation uploads older than one day that still have no message. The database clock enforces the age, and the function caps a pass at 50 even when the caller asks for more. It returns only the space, audience, file, and thread ids. Each candidate is then locked and re-read inside its own tenant and audience transaction. `drover_system` receives column-level `SELECT` only for those eligibility and identity columns. |
| `sys.claim_pending_upload_deletions(limit)` | Atomically claim due upload-deletion receipts across spaces for five minutes with `FOR UPDATE SKIP LOCKED`, capped at 50. It returns only the space, audience, and original file id. The worker derives the canonical storage key, deletes bytes outside the transaction, then forgets the receipt inside its tenant scope. A failed or interrupted store call leaves the receipt to become due again. |
| `sys.queue_upload_deletion()` | Trigger function that inserts a receipt only after an eligible row is deleted from the real `public.files` relation. It checks the trigger relation, schema, table, and operation before writing, so an app session cannot bind it to a temporary table to forge ids. Its audit row is written by the same delete transaction. |
| `sys.purge_expired(limit)` | Delete expired OAuth flows and destroyed sandboxes that no tool call mentions, at most `limit` of each. Session tables join it when they exist. |
| `sys.health()` | Counts for the status page and the first-run check: queued, running, and waiting runs, expired leases, the oldest lease, and the number of users. No tenant data. |
| `sys.applied_migration()` | The newest migration id in EF Core's history table, which the application role cannot read, so the status page can compare it with the build's own. Reads that one column. No tenant data. |

`effective_grants(bot_id)` is not a `sys` function: it lives in `public`, runs as the caller under row-level security, and is not on the manifest. `drover_system` is given column-level `SELECT` (and `UPDATE` on `runs` columns, `bot_schedules.next_run_at`, `threads.project_id`, and `pending_upload_deletions.cleanup_claim_until`) limited to what its functions read, `INSERT` on `pending_upload_deletions`, and `DELETE` on `oauth_flows`, `sandboxes`, `tool_connection_credentials`, and `secrets`; it has no table-wide `SELECT` anywhere. The bootstrap gives it `USAGE` on `public`; a test lists exactly what it holds. `sys.audit` is written by the functions and the guarded receipt trigger only.

Observed: `drover_app` calling `sys.claim_runs` received runs from two spaces, while a direct `SELECT` on `runs` in space A's transaction saw only A's.

Billing, when the hosted service needs it, reads `usage_records` through one more such function that returns aggregates per space.

### Isolation test suite

`tests/Drover.Data.Tests`, run against a real PostgreSQL. The fixture starts a container, or uses the server named by `DROVER_TEST_POSTGRES` when that is set, so the suite also runs where Docker is not available (`scripts/dev-postgres.sh`). It makes a database of its own, runs `DatabaseBootstrap` with passwords generated for the run, applies migrations as `drover_owner`, and connects as `drover_app`. Roles belong to the server, so two runs against one server at once would reset each other's passwords.

The class of each table comes from the EF Core model: `ISpaceOwned` (with `IAudienceScoped` for an audience), `IUserOwned`, or a line in `src/Drover.Data/GlobalTables.manifest`. `spaces` and `users` are global: their policies are special cases written out in the first migration and covered by `SpecialPolicyTests`. `__EFMigrationsHistory` is global with no entity, and the app role has no privilege on it. The Identity child tables are subclasses (`UserClaim`, `UserLogin`, `UserToken`) so that they can carry `IUserOwned`.

**Catalog tests** read `pg_catalog`, so they cover tables the author forgot about:

| Test | Fails when |
| --- | --- |
| Every table has an ownership class | A table in `public` is not `ISpaceOwned`, `IUserOwned`, or listed in `GlobalTables.manifest` |
| Row-level security is enabled and forced | `relrowsecurity` or `relforcerowsecurity` is false on a classed table |
| Policies are complete | A classed table lacks a permissive `FOR ALL` policy with both `USING` and `WITH CHECK` that names its discriminator and the matching `sys` function, or, with an audience, a restrictive one for `audience_user_id`. A policy of `USING (true)` fails. |
| The discriminator is not nullable | `space_id`, `audience_user_id`, or `user_id` allows null on a table of that class |
| Audience is inherited | A table that references an audience table does not include `audience_user_id` in that foreign key |
| Foreign keys stay inside a space | A foreign key between two space-owned tables does not include `space_id` |
| The app role is weak | `drover_app` is superuser, has `BYPASSRLS`, `CREATEDB`, `CREATEROLE`, or `REPLICATION`, owns any object, can create in any schema or temporary tables in the database, or is a member of another role |
| The bypass surface is known | `sys` functions, as `schema.name(argument types)`, differ from `SystemFunctions.manifest` |

"A new table without a policy fails the build" is the second and third rows. Analyzer `DRV0006` reports the same problem earlier, at compile time, when an entity has a `DbSet` but no ownership interface.

**Behavior tests** run once per table, found from the EF Core model, so a new table joins them without anyone listing it. Each entity that belongs to a space or a user registers a `TableSeeder<TEntity>` that gives the columns of one valid row; the suite sets `space_id`, `audience_user_id`, and `user_id` itself. An entity without a seeder fails `SeederCoverageTests`. Global tables need none. Every check first proves the row it cannot reach exists, so a check cannot pass by looking at an empty table. For spaces A and B:

| Acting as A | Expect |
| --- | --- |
| Select B's rows | None |
| Update or delete B's rows by id | Zero rows affected |
| Insert a row with B's `space_id` | Error 42501 |
| Update own row to B's `space_id` | Error 42501 |
| Insert a row referencing B's parent row | Foreign key error |
| Any statement with no tenant set | Zero rows, inserts rejected |

Tables with an audience repeat the pattern with two users in one space: user 2 cannot select, update, or delete user 1's private rows, cannot insert a row addressed to user 1, and user 1 cannot hand a private row to user 2. A child cannot be inserted or updated to a wider audience than its parent, and sharing the topmost parent shares the child in one statement. A catalog test asserts that every child of an audience table carries the audience in its foreign key. User-owned tables repeat the pattern with two users and no space, and also show that a user's row is theirs in every space they work in.

The real schema has no audience table until step 5, so the audience checks and the parent-reference check also run on a scratch parent and child (`scratch_parent`, `scratch_child`) made in a scratch database with the same migration helper. They are always part of the theory data, so those checks never go unrun.

**Scratch tests** break one rule at a time in a scratch database (a copy of a migrated template, dropped after the test) and show the audit or the generated check failing, then passing once the break is corrected: a table with no policy, no class, a policy that ignores the space, a nullable `space_id`, an entity with no seeder, a child foreign key that omits the audience or the space, a `sys` function with no manifest line, and an app role that can create or owns a table.

**Pooling test**: pool size one with `No Reset On Close`, 1,000 transactions alternating seven tenants and four endings (committed, rolled back, failed on the server, refused by a policy). No transaction may see a space it does not belong to, and after each one the connection must hold no tenant setting.

They run in pull request CI on hosted runners. The data test fixture appends its total time, database setup included, to the job summary when `GITHUB_STEP_SUMMARY` is set, and `dotnet test` prints the run duration in the log.

### How things are scoped

| Thing | Belongs to | Notes |
| --- | --- | --- |
| Bot | Space | Created by a human or by a parent bot |
| Thread, with its messages, events, tool calls, files | Space, with an audience | Private to its owner by default, enforced by policy. Sharing it makes the audience the whole space. |
| Run, usage record | Space | Metadata only, so admins can see activity and cost and cancel any run without being able to read a private thread |
| Tool connection (definition) | Space | Admins create and edit it |
| Tool connection credential | Space, with an audience | Per-user rows are unreadable outside that user's transactions |
| Model connection | Space | Its key is a space secret |
| Sandbox | Space, owned by one bot | Access by other bots follows the sandbox policy |
| Secret | Space, with an audience | Ciphertext is also bound to space and secret id; see [08-auth-and-secrets.md](08-auth-and-secrets.md) |
| Push subscription, session | User | Not tied to a space |

---

## Layer 2: bot permissions

### Effective grants

Effects are ordered: `deny` < `ask` < `allow`. "No grant" is below all of them and means the tool is invisible.

**The effective grant of a bot for a tool is the minimum of its own grant and every ancestor's grant.** If any bot on the path to the root has no grant, the result is no grant.

This is computed when a run assembles its tools and again when a tool is called, by one SQL function, `effective_grants(bot_id)`, which joins `tool_grants` to `bot_ancestors`. It runs as the caller, under row-level security. Because the minimum is taken at read time:

- A child can never hold more than its parent, even if a row says otherwise.
- Narrowing or revoking a parent's grant takes effect on the whole subtree at once, with no cascade of writes to get wrong.

Writes are validated too: granting a child an effect above its parent's effective grant is rejected with a clear message ("raise it on Marshal first"). The read-time rule is the guarantee; the write-time rule keeps stored data honest.

A `needs_review` grant counts as `ask` until a human confirms it. On top of that, a run leaves a tool server's tool with such a grant out altogether, so its changed text is not shown to the model ([06-mcp.md](06-mcp.md)). A bot whose own row (distance 0) is missing from `bot_ancestors` is not in the tree and gets no grants at all.

Pure functions in `Drover.Domain` implement the ordering and the minimum. The SQL function and the C# function are tested against the same table of cases.

### Authority in the tree

| Actor | May act on | Through |
| --- | --- | --- |
| Space owner, admin | Any bot, any run | The API. Can also set caps: tree depth, children per bot, total bots. |
| A bot | Its strict descendants only | Built-in management tools, each needing its own grant |
| A bot | Its siblings, ancestors, or unrelated bots | Nothing. The check is one lookup in `bot_ancestors`. |

Built-in tools. Each is a row in `tools`, so each is off until granted, and each obeys the narrowing rule like any other tool. Two defaults, set when a person creates a bot through the API. Every new bot is granted the built-in tools on a short explicit list (`BuiltinTools.GrantedToNewBots`, today only `time.now`) as `allow`, a child no higher than its parent's effective grant. A bot a person creates at the top of the tree is also granted the four crew tools (`BuiltinTools.AskedOfNewTopBots`) as `ask`, so the first bot of a space can lead its crew and a person approves each thing it does to it; a bot created beneath a parent, by a person or by a bot, is not, and a person grants them where they are wanted. Where the deployment rule `AllowBotsBuildingBots` is off, no bot is granted them. These are grant rows like any other, visible and removable. The lists are by name, never "every tool that only reads", so a later built-in that reads the web, a file, or memory reaches no bot until someone decides it should. The four memory tools and the two web tools (`web.search`, `web.fetch`) are on neither list.

| Tool | Does | Sensitivity | Default |
| --- | --- | --- | --- |
| `crew.list` | List the bots beneath the caller, at every depth: id, name, handle, what it is for, status, parent | read | `ask` on a new bot at the top |
| `crew.add` | Make a bot beneath the caller, or beneath one of its descendants. Always asks a person, whatever its grant | write | `ask` on a new bot at the top |
| `crew.change` | Change a descendant's name, what it is for, or instructions, or pause or resume it | write | `ask` on a new bot at the top |
| `crew.remove` | Remove a descendant, as a person's remove does. For one with bots beneath it, the person's choice for them (`beneath`: `remove` or `keep`) is required | write | `ask` on a new bot at the top |
| `delegate` | Hand a task to a bot directly beneath the caller and get its answer back as the result; the bot works as a child run with its own grants ([03-run-lifecycle.md](03-run-lifecycle.md#delegation)) | write | none |
| `runs.cancel` | Cancel a descendant's run (not built yet) | | |
| `sandbox.*` | Work in a sandbox the bot can reach | per tool | none |

A crew tool's call passes the tool gate and its approval like any other call. Making a bot asks a person every time, on every server: `crew.add` is a built-in tool that always asks (`BuiltinTool.AlwaysAsks`), so the tool gate treats `allow` on it as `ask`, both when the agent is built and when the call is made, and no grant, editor, or client can change that. `BotToolEntry.AlwaysAsks` tells the editor. The approval request carries, beside the call's arguments (name, what it is for, instructions, the optional parent), what they do not say: the model the new bot will use and the name of the bot it will go beneath (`ToolApprovalRequested.Details`, redacted like the arguments). `crew.change` and `crew.remove` follow their grant. What it may touch is then decided by `CrewService`, in one short tenant transaction as the run's person, from the run's own bot, never from an id or a name the model wrote:

1. The deployment allows it (`AllowBotsBuildingBots`), and the calling bot is active.
2. The run's person is a member of the space. Every tool but `crew.list` changes bots, which through the API only owners and admins may do, so for a member those are refused: a bot never has more authority than the person its run is for, and a member could otherwise reshape a tree the whole space shares by asking a bot to.
3. The target, and for `crew.add` the parent, is strictly beneath the calling bot: one lookup in `bot_ancestors` at a distance above zero. The caller itself, its ancestors, its siblings, and any bot of another space (which row-level security does not show) are refused with the same sentence.
4. The change then goes through `BotService`, the path a person's change takes, in the same transaction. A new bot uses the caller's model connection and model, no sandbox, and the tools every bot beneath a parent starts with, lowered to its parent's effective grants; the space's tree lock is taken, the parent row is locked, and the space's cap on its bots, the space caps on the tree, and every `build_limits` above it are checked. A refusal reaches the model as the tool's error, in the sentence the API gives a person. A change is checked as the editor's is.
5. Rows are written to `bots` (with `created_by_bot_id` set to the caller and `created_by_user_id` to the run's person), `bot_ancestors`, `tool_grants` (with `granted_by_bot_id`), and `audit_log`, whose entry has the bot as its actor and the person in `data.for_user_id`.

A person creating a bot beneath another through the API (`CreateBotRequest.ParentBotId`, owners and admins only) goes through the same tree rules: the parent row is locked, the space caps and the `build_limits` of the parent and every bot above it are checked, and the closure rows are written. The tools a new bot starts with are lowered to the parent's effective grants instead of refused, because the person did not choose them; see [02-data-model.md](02-data-model.md#bots).

### Delegation targets

A bot may delegate **down the tree only**, to its direct children. A child's effective grants are never broader than its parent's, so handing work down cannot gain a permission. This also makes a delegation cycle structurally impossible; the loop check in [03-run-lifecycle.md](03-run-lifecycle.md) remains as a guard.

The `delegate` tool's description, as the model sees it, lists the caller's children with each one's `description_for_bots`. That field exists for exactly this. The governor checks the target again when the call is made, from the run's own bot and never from the model's word: a bot that is not directly beneath the caller is refused, and so is a paused or removed one, with a result that says which. The child works with its own effective grants, which the minimum along the path to the root keeps at or below its parent's.

Delegating across the tree (to a sibling's team, say) is useful and risky: the target may hold tools the caller lacks, so a low-privilege bot could steer a high-privilege one. It is deferred to Phase 2 as human-created delegation links with one added rule: **borrowed authority asks.** During a run that arrived over a link, any `allow` grant the calling bot does not itself hold is treated as `ask`.

### Rules

A grant says whether a bot may use a tool. A rule is a further check a person writes as a sentence: when something happens (a tool is about to run, a tool has answered, the bot is about to reply, a message arrives), if every condition holds, then redact, ask, block, or stop. Rules are stored in `bot_rules` and enforced by the server in code ([03-run-lifecycle.md](03-run-lifecycle.md)); the model is never asked to follow one and never decides whether one applies. A rule's conditions are of two kinds. Exact conditions (the tool, its kind, the time, a count, a list on named inputs) are decided on structure and are guaranteed. Pattern conditions (text that contains, matches, or holds something, and a list of addresses anywhere in the input) catch common forms and are not a guarantee; nothing in the product calls one a hard limit.

- **Rules only narrow.** A rule has no effect that allows. At the tool gate the grant is decided first, and a call it refuses is not shown to a rule; a milder rule never lowers a grant that asks to one that runs. A rule cannot loosen a deployment or space policy, because it never reaches one.
- **A child follows every ancestor's rules** as well as its own, ancestors first. Only a bot's own rules can be edited on that bot; an inherited rule is shown with the bot it comes from and has no route that changes it from below.
- **When several rules apply, the strictest effect decides**: stop, then block, then ask, then redact. Every applying rule that redacts is carried out whichever rule decides, so a child's rule that asks cannot let through what a parent's rule removes.
- **A list on named inputs is exact.** The rule names one or more inputs, matched without regard to case, and every value of every occurrence of them has to be on the list: one whole listed entry, or one address or host under a listed domain, compared as written. It has to say which tools it is about, and for those tools it applies when none of the named inputs is in the call. A recipient rule names every field a recipient can be in (`to`, `cc`, `bcc`); a field it does not name is not checked. A list with no input named is a pattern condition: anything with an `@` in it, anywhere in the input, has to be a listed address or at a listed host. An input inside another (`message.to`) is not a named input; the address list sees it.
- **What a person is told about a rule.** A rule that names a tool no tool is called is saved, and says so at save and every time it is read back, so a typo does not turn a rule off in silence. A list rule saved before it had to name its tools keeps working as it did when it was saved (it does not apply when its input is missing) and says that, with what to change. No stored rule is rewritten.
- **Only owners and admins of the space edit rules.** Every change is an audit entry (`rule.added`, `rule.changed`, `rule.removed`).
- **Bounds are enforced at save**: fifty rules a bot, ten conditions a rule, and a cap on every list and pattern. A rule cannot look for text with a pattern a person wrote: the deployment rule `AllowWrittenRulePatterns` is off in every preset, because such a pattern cannot be stopped once it starts matching. A name under a named input of a list is checked like a value.
- **"Try it" is the run's evaluator.** `POST .../rules/try` evaluates a sample with `RuleEvaluator.Evaluate`, the same call a run makes, so what the editor shows is what a run would do.
- A fired rule is a `rule.fired` run event that says which rule and why in words. The text that matched is not in the event, the server log, or what the model is given.

A handoff inside a team is not a tool call and passes no gate ([14-agent-framework.md](14-agent-framework.md), point 12), so no `before_tool` rule sees it. The member it lands on is built like any bot: its tool calls and replies are ruled.

### Pausing and removing

- **Pause** stops new runs for the bot and its subtree and requests cancellation of active ones.
- **Remove** marks a bot `removed`: it is no longer listed, starts no run, and passes no tool gate. Its sandbox shares end and its own sandbox is retired ([07-sandbox.md](07-sandbox.md)). Threads and history stay. A bot with nothing beneath it needs no more than that.
- **A bot with bots beneath it** needs a choice for them, on every path. `DELETE /api/spaces/{spaceId}/bots/{botId}?beneath=remove|keep`; without `beneath` such a remove is refused with a 400 whose title says the bot has bots beneath it and a choice is needed, so no client can skip the question. `crew.remove` takes the same choice as `beneath`, and without it tells the model to ask the person; its description tells the model to ask before choosing.
  - `remove`: the whole branch goes, in one transaction. Each bot in it is removed as above and has its own `bot.removed` audit entry, naming the bot it was removed with.
  - `keep`: the bots directly beneath move up beneath the removed bot's parent, or to the top, each with everything beneath it; the closure rows and depths of the moved branches are rewritten ([02-data-model.md](02-data-model.md#bots)). Moving up never makes a branch deeper, so depth caps cannot refuse it. The new parent's own `build_limits.max_children` and the space's `max_children_per_bot` can: when the moved bots would not fit beside the new parent's other children, the whole remove is refused with a sentence and nothing changes. A moved bot's own grants are lowered to the removed bot's effective grants, and a grant on a tool the removed bot could not use is taken away, each with a `grant.changed` entry, so no bot of a moved branch can do more after the move than before, and none holds more than its new parent (the write-time rule holds against the new path). Each moved bot gets a `bot.moved` entry.
  - Both take the space's tree lock first, so a bot is never added beneath a branch while it is removed or moved.

### What a prompt injection can and cannot do

The model never decides what it is allowed to do. It can only ask for a tool that was sent to it, and the gate checks the grant again when it asks. Further limits on untrusted tool output are in [06-mcp.md](06-mcp.md).

A bot's notes about its person ([02-data-model.md](02-data-model.md)) are text the model wrote, and a note may repeat what a tool result said, so an injection can reach a later run through them. They are given to the model as a labeled message that says they are reference material and not instructions, each as one quoted line, never in the system instructions, and they change no grant: a run that recalls a note is offered the same tools, through the same gate, as one that does not. The tainted-run rule follows a note: a note written by a tainted run is marked tainted, and a run given a tainted note starts tainted, so its consequential tools ask before they act, as they would in the run that read the untrusted text. A note is only ever the run's own person's, read only in a conversation private to that person.
