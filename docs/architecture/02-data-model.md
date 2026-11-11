# 2. Core data model

PostgreSQL, one schema `public` for tenant data and one schema `sys` for the audited cross-tenant functions. Names are `snake_case`. Primary keys are UUID version 7 (`Guid.CreateVersion7()`), which sort by creation time and need no coordination.

## Ownership classes

Every table belongs to exactly one class. The class decides its row-level security policy ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)).

| Class | Marker in C# | Column | Policy |
| --- | --- | --- | --- |
| Space-owned | `ISpaceOwned` | `space_id uuid not null` | Row visible and writable only when `space_id` equals the transaction's space |
| Space-owned and user-private | `ISpaceOwned, IUserPrivate` | plus `user_id uuid null` | As above, and `user_id` is null or equals the transaction's user |
| User-owned | `IUserOwned` | `user_id uuid not null` | Row visible only to that user |
| Global | listed in the isolation manifest | none | App role has no direct access, or read-only reference data |

Every foreign key between two space-owned tables is composite, `(space_id, x_id)` referencing `(space_id, id)`. A row therefore cannot point at another space's row even if application code tries. Foreign key checks are not subject to row-level security, so this closes a gap the policies alone leave open.

## Identity and membership

| Table | Class | Key columns | Notes |
| --- | --- | --- | --- |
| `users` | Global, special policy | `id`, `email`, `display_name`, password and passkey fields from ASP.NET Core Identity | A user is visible to themself and to members of the current space. Sign-in lookups go through `sys.find_user_for_sign_in`. |
| `spaces` | Space-owned (its `id` is the space) | `id`, `name`, `slug`, `settings jsonb` | Settings hold the hierarchy caps: `max_bot_depth`, `max_children_per_bot`, `max_bots`, plus run budgets. |
| `memberships` | Space-owned, readable by own user | `space_id`, `user_id`, `role` | `role` is `owner`, `admin`, or `member`. Unique on `(space_id, user_id)`. A user can list their own memberships before a space is selected. |
| `user_sessions`, `push_subscriptions` | User-owned | `user_id`, device fields | A device belongs to a person, not to a space. |

### Roles

| Capability | Owner | Admin | Member |
| --- | --- | --- | --- |
| Delete the space, transfer ownership, change billing | Yes | No | No |
| Manage members and roles | Yes | Yes, except owners | No |
| Create and edit connections, top-level bots, grants, caps | Yes | Yes | No |
| See and override the whole bot tree, cancel any run | Yes | Yes | No |
| Chat with bots, own threads, sign in to per-user connections | Yes | Yes | Yes |
| Approve "ask" tool calls | Own runs, and any run | Own runs, and any run | Own runs |

Phase 0 creates exactly one space and one owner, through the same tables.

## Bots

| Table | Class | Key columns |
| --- | --- | --- |
| `bots` | Space-owned | `id`, `parent_bot_id null`, `name`, `handle`, `avatar jsonb`, `description_for_bots`, `instructions`, `model_connection_id`, `model`, `model_settings jsonb`, `memory_settings jsonb`, `sandbox_policy`, `sandbox_settings jsonb`, `status`, `depth`, `build_limits jsonb`, `created_by_user_id null`, `created_by_bot_id null` |
| `bot_ancestors` | Space-owned | `ancestor_bot_id`, `descendant_bot_id`, `distance` |
| `bot_sandbox_shares` | Space-owned | `bot_id`, `shared_with_bot_id` |
| `bot_schedules` | Space-owned | `bot_id`, `owner_user_id`, `cron`, `time_zone`, `prompt`, `thread_mode`, `enabled`, `next_run_at` |
| `tool_grants` | Space-owned | `bot_id`, `tool_id`, `effect`, `review_state`, `granted_by_user_id null`, `granted_by_bot_id null` |

Notes:

- `status` is `active`, `paused`, or `removed`. A bot is effectively paused when it or any ancestor is paused.
- `sandbox_policy` is `none`, `private`, `subtree`, or `named`. A bot with a sandbox defaults to `private`.
- `build_limits` narrows the space caps for one bot: how many children it may create and how deep beneath itself.
- `bot_ancestors` is a closure table that includes each bot's row to itself at distance 0. "Is X beneath Y" is one primary key lookup; "everything beneath Y" is one indexed scan. It is plain relational data, needs no extension, and is rewritten only when the tree changes, which is rare.
- `tool_grants.effect` is `allow`, `ask`, or `deny`. No row means the bot cannot see the tool. `review_state` is `current` or `needs_review`; see tool definition pinning in [06-mcp.md](06-mcp.md).
- Model settings, memory settings, and sandbox settings are `jsonb` mapped to C# records owned by `Drover.Domain`. They are provider-neutral: `temperature`, `max_output_tokens`, `reasoning_effort`, not a vendor's option names.

### Skills, reserved shape

Not created in Phase 0. The model leaves room for them without redesign:

| Table | Key columns |
| --- | --- |
| `skills` | `id`, `name`, `description`, `instructions`, `source`, `trust`, `content_file_id null`, `content_hash` |
| `bot_skills` | `bot_id`, `skill_id`, `enabled`, `position` |

A bot's instructions are assembled by the engine from an ordered list of instruction sources: the bot's own text first, then each enabled skill's description, with the skill body loaded on demand. Phase 0 has one source. A skill's scripts run only in the bot's sandbox, and a skill never carries grants: it can use only the tools the bot already holds. `trust` is always `untrusted` for imported content, which means its text is presented to the model as reference material, never as system instructions.

## Tools and connections

| Table | Class | Key columns |
| --- | --- | --- |
| `model_connections` | Space-owned | `id`, `name`, `kind`, `base_url`, `auth_kind`, `secret_id null`, `default_model`, `capabilities jsonb`, `status` |
| `tool_connections` | Space-owned | `id`, `name`, `slug`, `url`, `transport`, `auth_kind`, `credential_scope`, `oauth jsonb`, `trust`, `status`, `last_listed_at`, `last_error` |
| `tool_connection_credentials` | Space-owned and user-private | `id`, `tool_connection_id`, `user_id null`, `secret_id`, `expires_at`, `status` |
| `tools` | Space-owned | `id`, `source_kind`, `tool_connection_id null`, `name`, `title`, `description`, `input_schema jsonb`, `annotations jsonb`, `definition_hash`, `sensitivity`, `state`, `first_seen_at`, `last_seen_at` |
| `oauth_flows` | Space-owned and user-private | `state_hash`, `user_id`, `tool_connection_id`, `expires_at` |

Notes:

- `tools.source_kind` is `builtin` or `mcp`. Built-in tools are seeded as rows when a space is created, so a grant always references a `tools` row and the grant logic has one shape.
- `tools.state` is `available` or `missing`. A tool that disappears from a server keeps its row and its grants, marked missing.
- `tool_connections.auth_kind` is `none`, `header`, or `oauth`. `credential_scope` is `space` or `per_user`.
- `tool_connection_credentials.user_id` null means a credential shared by the whole space. A row with a user id is readable only inside a transaction running for that user. That is how a bot acting for one person cannot use another person's sign-in: the database will not return the row.

## Conversations and runs

| Table | Class | Key columns |
| --- | --- | --- |
| `threads` | Space-owned | `id`, `bot_id`, `owner_user_id`, `title`, `origin`, `visibility`, `last_activity_at`, `archived_at null` |
| `messages` | Space-owned | `id`, `thread_id`, `position`, `role`, `author_user_id null`, `author_bot_id null`, `run_id null`, `parts jsonb`, `created_at` |
| `runs` | Space-owned | `id`, `thread_id`, `bot_id`, `principal_user_id`, `trigger`, `trigger_message_id null`, `parent_run_id null`, `root_run_id`, `parent_tool_call_id null`, `depth`, `status`, `status_reason`, `error jsonb`, `tainted`, `attempt`, `lease_owner null`, `lease_expires_at null`, `cancel_requested_at null`, `last_event_seq`, `usage jsonb`, `subtree_usage jsonb`, `started_at`, `finished_at` |
| `run_events` | Space-owned | `root_run_id`, `seq`, `run_id`, `type`, `data jsonb`, `created_at`. Primary key `(space_id, root_run_id, seq)` |
| `tool_calls` | Space-owned | `id`, `run_id`, `tool_id`, `tool_name`, `arguments jsonb`, `status`, `result jsonb`, `result_file_id null`, `is_error`, `decided_by_user_id null`, `decided_at null`, `child_run_id null`, `sandbox_id null`, `started_at`, `finished_at` |
| `usage_records` | Space-owned | `id`, `run_id`, `root_run_id`, `bot_id`, `model_connection_id`, `model`, `input_tokens`, `output_tokens`, `cached_tokens`, `cost`, `currency`, `created_at` |

Notes:

- `messages.parts` is an ordered array of Drover's own part records: `text`, `reasoning`, `image`, `file`, `tool_call`, `tool_result`. This is Drover's schema, defined in `Drover.Contracts`. No vendor message type is ever serialized into it.
- `messages.position` orders a thread. It is assigned under a lock on the thread row.
- `runs.principal_user_id` is the human the run acts for. A child run inherits it unchanged from its parent. Scheduled runs use the schedule's owner.
- `runs.root_run_id` equals `id` for a root run. `last_event_seq` is used only on the root row.
- `runs.status` values and transitions are in [03-run-lifecycle.md](03-run-lifecycle.md).
- `tool_calls` is a first-class table, not only events, because the activity view, the approval inbox, and restart recovery all query it by status.
- `tool_calls.arguments` and `result` are stored after redaction ([08-auth-and-secrets.md](08-auth-and-secrets.md)). Large results go to a file and the row keeps a preview.
- `threads.visibility` is `private` or `space`. Phase 0 has one user, so this is stored but not yet meaningful.

## Sandboxes, files, secrets, audit

| Table | Class | Key columns |
| --- | --- | --- |
| `sandboxes` | Space-owned | `id`, `owner_bot_id`, `policy`, `provider`, `provider_sandbox_id`, `state`, `access_secret_id`, `last_used_at`, `expires_at`, `retire_after null` |
| `files` | Space-owned | `id`, `storage_key`, `name`, `content_type`, `size`, `sha256`, `created_by_user_id null`, `created_by_bot_id null` |
| `secrets` | Space-owned and user-private | `id`, `user_id null`, `kind`, `ciphertext bytea`, `created_by_user_id`, `created_at`, `last_used_at` |
| `audit_log` | Space-owned, insert and select only | `id`, `actor_kind`, `actor_id`, `action`, `target_kind`, `target_id`, `data jsonb`, `created_at` |
| `sys.audit` | Global | One row per call of a cross-tenant function |
| `data_protection_keys` | Global, no app-role table access outside the key ring store | Key ring when stored in the database |

`sandboxes.state` is `creating`, `running`, `paused`, `retiring`, or `destroyed`.

Phase 1 adds `memories` (space-owned, with a `vector` column) and nothing else structural.

## Indexes that matter from day one

| Index | Serves |
| --- | --- |
| `runs (status, lease_expires_at)` partial on non-terminal statuses | Job claiming and lease recovery |
| `runs (space_id, root_run_id)` | Subtree cancel, cost roll-up, activity tree |
| `run_events (space_id, root_run_id, seq)` primary key | Replay from a cursor |
| `tool_calls (space_id, status)` partial on `pending_approval` | Approval inbox |
| `messages (space_id, thread_id, position)` unique | Thread paging |
| `bot_ancestors (space_id, ancestor_bot_id)` and primary key `(space_id, descendant_bot_id, ancestor_bot_id)` | Hierarchy checks both directions |
| `tools (space_id, tool_connection_id, name)` unique | Catalog upsert |

`run_events` grows fastest. It is range-partitioned by month on `created_at` from the first migration, so old partitions can be detached or dropped without a table rewrite.
