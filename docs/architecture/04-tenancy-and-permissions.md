# 4. Tenancy, row-level security, and bot permissions

Two layers, kept separate on purpose:

| Layer | Separates | Enforced by | Fails how |
| --- | --- | --- | --- |
| 1. Tenancy | One space from another, and one user's private rows from another's | PostgreSQL row-level security | Zero rows, or a database error |
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

`drover_app` has `SELECT, INSERT, UPDATE, DELETE` on tenant tables, `EXECUTE` on `sys` functions, and nothing else. It cannot create objects, alter tables, or change policies. Observed: `ALTER TABLE threads DISABLE ROW LEVEL SECURITY` as `drover_app` returns "must be owner of table threads", and `SET ROLE drover_system` returns "permission denied".

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

User-private tables (`secrets`, `tool_connection_credentials`, `oauth_flows`) add a restrictive policy, which is combined with the first using AND:

```sql
CREATE POLICY user_private ON secrets AS RESTRICTIVE FOR ALL
  USING      (user_id IS NULL OR user_id = sys.current_user_id())
  WITH CHECK (user_id IS NULL OR user_id = sys.current_user_id());
```

User-owned tables use `user_id = sys.current_user_id()` alone. Special cases, each written out in the migration and each covered by a named test:

| Table | Policy |
| --- | --- |
| `spaces` | `id = sys.current_space()` |
| `memberships` | `space_id = sys.current_space() OR user_id = sys.current_user_id()` for select; space match for writes |
| `users` | Select own row, or rows of users who are members of the current space. Update own row only. |
| `audit_log` | Space match; `drover_app` has no `UPDATE` or `DELETE` privilege |

In C# the helper is an extension on `MigrationBuilder`, following the guide's preference for extension builders:

```csharp
namespace Drover.Data.Migrations;

public static class RowLevelSecurityExtensions
{
    #region Public Methods

    public static MigrationBuilder IsolateBySpace(this MigrationBuilder migration, string table)
    {
        migration.Sql($"ALTER TABLE {table} ENABLE ROW LEVEL SECURITY;");
        migration.Sql($"ALTER TABLE {table} FORCE ROW LEVEL SECURITY;");
        migration.Sql(
            $"""
            CREATE POLICY space_isolation ON {table} FOR ALL
              USING (space_id = sys.current_space())
              WITH CHECK (space_id = sys.current_space());
            """);
        migration.Sql($"GRANT SELECT, INSERT, UPDATE, DELETE ON {table} TO drover_app;");
        return migration;
    }

    #endregion
}
```

`table` is always a compile-time table name from a migration, never user input.

### Setting the tenant: EF Core and connection pooling

The rule: **all tenant data access happens inside an explicit transaction, and the transaction's first statement sets the identity with `set_config(..., true)`.** The third argument makes the setting local to the transaction. PostgreSQL discards it at commit or rollback, so nothing can travel with a pooled connection.

How it is wired:

1. `TenantContext` is a scoped service holding the space id and user id. It is set once per request by middleware after authentication and membership check, and once per job by the worker. It refuses to change space once set.
2. `TenantTransactionInterceptor` is an EF Core `DbTransactionInterceptor`. When any transaction starts on the context, it runs the `set_config` statement on that transaction before anything else.
3. `TenantGuardInterceptor` is a `DbCommandInterceptor` that throws when a command runs with no transaction. Forgetting the transaction is a loud failure in development, and would return zero rows anyway.
4. `DroverDbContext` is registered with `AddDbContext` (scoped), not `AddDbContextPool`. Pooled contexts share interceptors across scopes; a scoped context gets the interceptor for its own `TenantContext`. Database connections are still pooled by Npgsql.

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

Callers do not use the interceptor directly. They use `TenantScope`:

```csharp
await using var work = await tenantScope.Begin(cancellationToken);
var thread = await db.Threads.SingleAsync(t => t.Id == threadId, cancellationToken);
thread.Title = title;
await work.Commit(cancellationToken);
```

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
| Stamping `space_id` on insert | A `SaveChanges` interceptor fills `SpaceId` on new `ISpaceOwned` entities from `TenantContext` | Convenience only. `WITH CHECK` is what enforces it. |

### Cross-tenant work: the `sys` path

Some work must look across spaces: claiming jobs, finding a user at sign-in, cleanup. It goes through functions in schema `sys`, and nowhere else.

Rules for a `sys` function:

1. Owned by `drover_system`, `SECURITY DEFINER`, with `SET search_path = pg_catalog, public` pinned.
2. Does one narrow thing and returns the minimum: ids, not rows. `claim_runs` returns `(run_id, space_id, principal_user_id)`; the worker then opens a normal tenant scope and reads the run under row-level security.
3. `EXECUTE` revoked from `PUBLIC`, granted to `drover_app`.
4. Writes one row to `sys.audit`: function, caller, time, row count.
5. Listed in `tests/Drover.Data.Tests/SystemFunctions.manifest`. A test compares the manifest with the catalog. Adding, removing, or changing the signature of a `sys` function without editing the manifest fails the build, which puts every change to the bypass surface in front of a reviewer.

Initial set:

| Function | Purpose |
| --- | --- |
| `sys.claim_runs(worker, limit)` | Lease runnable runs across spaces |
| `sys.claim_due_schedules(limit)` | Phase 1 |
| `sys.find_user_for_sign_in(email)` | Return the user id and credential fields for one email |
| `sys.memberships_for_user(user_id)` | Used once after sign-in, before a space is chosen |
| `sys.resolve_oauth_state(state_hash)` | Map an OAuth callback to its space and user |
| `sys.purge_expired(limit)` | Delete expired sessions, OAuth flows, and retired rows |
| `sys.health()` | Counts for the status page: queued runs, oldest lease, no tenant data |

Observed: `drover_app` calling `sys.claim_runs` received runs from two spaces, while a direct `SELECT` on `runs` in space A's transaction saw only A's.

Billing, when the hosted service needs it, reads `usage_records` through one more such function that returns aggregates per space.

### Isolation test suite

`tests/Drover.Data.Tests`, run against a real PostgreSQL started by Testcontainers. Migrations are applied as `drover_owner`; tests connect as `drover_app`.

**Catalog tests** read `pg_catalog`, so they cover tables the author forgot about:

| Test | Fails when |
| --- | --- |
| Every table has an ownership class | A table in `public` is not `ISpaceOwned`, `IUserOwned`, or listed in `GlobalTables.manifest` |
| Row-level security is enabled and forced | `relrowsecurity` or `relforcerowsecurity` is false on a classed table |
| Policies are complete | A classed table lacks a policy covering all commands with both `USING` and `WITH CHECK` |
| The discriminator is not nullable | `space_id` or `user_id` allows null on an owned table |
| Foreign keys stay inside a space | A foreign key between two space-owned tables does not include `space_id` |
| The app role is weak | `drover_app` is superuser, has `BYPASSRLS`, owns any object, or can create in any schema |
| The bypass surface is known | `sys` functions differ from the manifest |

"A new table without a policy fails the build" is the second and third rows. Analyzer `DRV0006` reports the same problem earlier, at compile time, when an entity has a `DbSet` but no ownership interface.

**Behavior tests** run once per table, generated from the EF Core model. Each entity registers a seeder; an entity without one fails a test. For spaces A and B:

| Acting as A | Expect |
| --- | --- |
| Select B's rows | None |
| Update or delete B's rows by id | Zero rows affected |
| Insert a row with B's `space_id` | Error 42501 |
| Update own row to B's `space_id` | Error 42501 |
| Insert a row referencing B's parent row | Foreign key error |
| Any statement with no tenant set | Zero rows, inserts rejected |

User-private tables repeat the pattern with two users in one space.

**Pooling test**: pool size one, alternate tenants across 1,000 transactions including rolled-back and faulted ones, assert no row ever appears for the wrong tenant.

These tests need Docker and nothing else. They run in pull request CI on hosted runners.

### How things are scoped

| Thing | Belongs to | Notes |
| --- | --- | --- |
| Bot | Space | Created by a human or by a parent bot |
| Thread | Space, with an owning user | Visibility `private` or `space`. Per-user thread privacy is enforced by the engine in Phase 2; see [12-risks.md](12-risks.md). |
| Tool connection (definition) | Space | Admins create and edit it |
| Tool connection credential | Space, or one user in the space | Per-user rows are unreadable outside that user's transactions |
| Model connection | Space | Its key is a space secret |
| Sandbox | Space, owned by one bot | Access by other bots follows the sandbox policy |
| Secret | Space, optionally private to one user | Ciphertext is also bound to space and secret id; see [08-auth-and-secrets.md](08-auth-and-secrets.md) |
| Push subscription, session | User | Not tied to a space |

---

## Layer 2: bot permissions

### Effective grants

Effects are ordered: `deny` < `ask` < `allow`. "No grant" is below all of them and means the tool is invisible.

**The effective grant of a bot for a tool is the minimum of its own grant and every ancestor's grant.** If any bot on the path to the root has no grant, the result is no grant.

This is computed when a run assembles its tools and again when a tool is called, by one SQL function, `effective_grants(bot_id)`, which joins `tool_grants` to `bot_ancestors`. It runs as the caller, under row-level security. Because the minimum is taken at read time:

- A child can never hold more than its parent, even if a row says otherwise.
- Narrowing or revoking a parent's grant takes effect on the whole subtree at once, with no cascade of writes to get wrong.

Writes are validated too: granting a child an effect above its parent's effective grant is rejected with a clear message ("raise it on Atlas first"). The read-time rule is the guarantee; the write-time rule keeps stored data honest.

A `needs_review` grant counts as `ask` until a human confirms it.

Pure functions in `Drover.Domain` implement the ordering and the minimum. The SQL function and the C# function are tested against the same table of cases.

### Authority in the tree

| Actor | May act on | Through |
| --- | --- | --- |
| Space owner, admin | Any bot, any run | The API. Can also set caps: tree depth, children per bot, total bots. |
| A bot | Its strict descendants only | Built-in management tools, each needing its own grant |
| A bot | Its siblings, ancestors, or unrelated bots | Nothing. The check is one lookup in `bot_ancestors`. |

Built-in tools. Each is a row in `tools`, so each is off until granted, and each obeys the narrowing rule like any other tool.

| Tool | Does | Suggested default when a human turns it on |
| --- | --- | --- |
| `bots.list` | See own descendants and their status | allow |
| `bots.create` | Create a child | ask |
| `bots.update` | Edit a descendant's definition or grants | ask |
| `bots.pause`, `bots.resume` | Pause or resume a descendant | allow |
| `bots.remove` | Remove a descendant and its subtree | ask |
| `delegate` | Hand a task to a child and get the result | allow |
| `runs.cancel` | Cancel a descendant's run | allow |
| `sandbox.*` | Work in a sandbox the bot can reach | per tool |

Creating a bot, in one transaction with the parent row locked:

1. The caller holds `bots.create`, and is active.
2. Caps hold: space depth and width limits, the caller's own `build_limits`, the space's total.
3. The child's grants are each no higher than the caller's effective grant. Anything else is refused, not silently lowered, so the model learns the rule.
4. The child's sandbox policy and model connection are ones the caller itself may use.
5. Rows are written to `bots`, `bot_ancestors`, `tool_grants`, and `audit_log`.

A child can receive `bots.create` only if its parent holds it and the depth cap leaves room.

### Delegation targets

A bot may delegate **down the tree only**, to its direct children. A child's effective grants are never broader than its parent's, so handing work down cannot gain a permission. This also makes a delegation cycle structurally impossible; the loop check in [03-run-lifecycle.md](03-run-lifecycle.md) remains as a guard.

The `delegate` tool's description, as the model sees it, lists the caller's children with each one's `description_for_bots`. That field exists for exactly this.

Delegating across the tree (to a sibling's team, say) is useful and risky: the target may hold tools the caller lacks, so a low-privilege bot could steer a high-privilege one. It is deferred to Phase 2 as human-created delegation links with one added rule: **borrowed authority asks.** During a run that arrived over a link, any `allow` grant the calling bot does not itself hold is treated as `ask`.

### Pausing and removing

- **Pause** stops new runs for the bot and its subtree and requests cancellation of active ones.
- **Remove** marks the bot and its subtree `removed`, cancels their runs, and retires their sandboxes ([07-sandbox.md](07-sandbox.md)). Threads and history stay. A human can move children elsewhere first; a bot cannot re-parent.

### What a prompt injection can and cannot do

The model never decides what it is allowed to do. It can only ask for a tool that was sent to it, and the gate checks the grant again when it asks. Further limits on untrusted tool output are in [06-mcp.md](06-mcp.md).
