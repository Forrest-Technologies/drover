# 2. Core data model

PostgreSQL, one schema `public` for tenant data and one schema `sys` for the audited cross-tenant functions. Names are `snake_case`. Primary keys are UUID version 7 (`Guid.CreateVersion7()`), which sort by creation time and need no coordination.

## Ownership classes

Every table belongs to exactly one class. The class decides its row-level security policy ([04-tenancy-and-permissions.md](04-tenancy-and-permissions.md)).

| Class | Marker in C# | Column | Policy |
| --- | --- | --- | --- |
| Space-owned | `ISpaceOwned` | `space_id uuid not null` | Row visible and writable only when `space_id` equals the transaction's space |
| Space-owned with an audience | `ISpaceOwned, IAudienceScoped` | plus `audience_user_id uuid not null` | As above, and the audience is "everyone in the space" or equals the transaction's user |
| User-owned | `IUserOwned` | `user_id uuid not null` | Row visible only to that user |
| Global | listed in the isolation manifest | none | App role has no direct access, or read-only reference data |

Every foreign key between two space-owned tables is composite, `(space_id, x_id)` referencing `(space_id, id)`. A row therefore cannot point at another space's row even if application code tries. Foreign key checks are not subject to row-level security, so this closes a gap the policies alone leave open.

### Users, personal spaces, and shared spaces

Isolation between people is the normal case and the database enforces it at two levels.

| Level | What it is | Enforced by |
| --- | --- | --- |
| **Personal space** | Every user gets one at sign-up, with exactly one member. Their own bots, connections, threads, and secrets live here. | Space isolation. Another user is simply in a different space. |
| **Shared space** | A group that several users belong to. Bots, connections, and the bot tree are the group's. | Space isolation between groups, plus the audience column inside the group. |

Inside a shared space, each content row has an **audience**: one user, or everyone in the space. `audience_user_id` holds the user's id, or the all-zero UUID meaning everyone. A private thread, its messages, its run events, its tool calls, and its files are invisible to other members of the same space, by policy, not by application check.

The column is `not null` with a sentinel on purpose. It lets child tables carry the audience inside their foreign key, `(space_id, thread_id, audience_user_id)` referencing the thread with `ON UPDATE CASCADE`. The database then guarantees a message can never have a wider audience than its thread, and sharing a thread updates every descendant row in one statement. A nullable column cannot do this, because a null in a composite foreign key skips the check.

What is shared by the whole space, and has no audience column: bots, grants, connections' definitions, the tool catalog, and the `runs` and `usage_records` rows. A run row is metadata only (which bot, status, timing, cost). That lets space admins see activity and cost and cancel any run, while the content of a private thread stays unreadable to them.

## Identity and membership

| Table | Class | Key columns | Notes |
| --- | --- | --- | --- |
| `users` | Global, special policy | `id`, `email`, `display_name`, password and passkey fields from ASP.NET Core Identity | A user is visible to themself and to members of the current space. Sign-in lookups go through `sys.find_user_for_sign_in`. |
| `spaces` | Space-owned (its `id` is the space) | `id`, `kind`, `name`, `slug`, `settings jsonb` | `kind` is `personal` or `shared`. Settings hold the hierarchy caps: `max_bot_depth`, `max_children_per_bot`, `max_bots`, plus run budgets. |
| `memberships` | Space-owned, readable by own user | `id`, `space_id`, `user_id`, `role` | `role` is `owner`, `admin`, or `member`. Unique on `(space_id, user_id)`. A user can list their own memberships before a space is selected. |
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

Phase 0 creates one user and their personal space, through the same tables. Shared spaces get their UI in Phase 2; the schema and policies for them exist from the first migration and are covered by the isolation suite.

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
| `model_connections` | Space-owned | `id`, `name`, `kind`, `base_url`, `auth_kind`, `credential_scope`, `secret_id null`, `default_model`, `status`, `balance jsonb`, `last_checked_at` |
| `model_catalog` | Space-owned | `model_connection_id`, `model`, `display_name`, `capabilities jsonb`, `pricing jsonb`, `privacy_mode`, `traits`, `state`, `last_seen_at` |
| `tool_connections` | Space-owned | `id`, `name`, `slug`, `url`, `transport`, `auth_kind`, `credential_scope`, `oauth jsonb`, `trust`, `status`, `last_listed_at`, `last_error` |
| `tool_connection_credentials` | Space-owned with an audience | `id`, `tool_connection_id`, `audience_user_id`, `secret_id`, `expires_at`, `status` |
| `tools` | Space-owned | `id`, `source_kind`, `tool_connection_id null`, `name`, `title`, `description`, `input_schema jsonb`, `annotations jsonb`, `definition_hash`, `sensitivity`, `state`, `first_seen_at`, `last_seen_at` |
| `oauth_flows` | Space-owned with an audience | `state_hash`, `audience_user_id`, `tool_connection_id`, `expires_at` |

Notes:

- `model_connections.kind` is `openai_compatible`, `venice`, `anthropic`, or a sign-in kind. `model_catalog` holds one capability record per model, in Drover's own shape ([15-model-providers.md](15-model-providers.md)). Sign-in connections keep per-user credentials in `secrets` with that user as the audience.
- `tool_connections` also stores `protocol_mode` (automatic, stateless only, session-based only) and what was negotiated.
- `tools.source_kind` is `builtin` or `mcp`. Built-in tools are seeded as rows when a space is created, so a grant always references a `tools` row and the grant logic has one shape.
- `tools.state` is `available` or `missing`. A tool that disappears from a server keeps its row and its grants, marked missing.
- `tool_connections.auth_kind` is `none`, `header`, or `oauth`. `credential_scope` is `space` or `per_user`.
- A `tool_connection_credentials` row whose audience is everyone is a credential shared by the whole space. A row whose audience is one user is readable only inside a transaction running for that user. That is how a bot acting for one person cannot use another person's sign-in: the database will not return the row.

## Conversations and runs

| Table | Class | Key columns |
| --- | --- | --- |
| `threads` | Space-owned with an audience | `id`, `bot_id`, `owner_user_id`, `audience_user_id`, `title`, `origin`, `last_activity_at`, `archived_at null` |
| `messages` | Space-owned with an audience, inherited from the thread | `id`, `thread_id`, `audience_user_id`, `position`, `role`, `author_user_id null`, `author_bot_id null`, `run_id null`, `parts jsonb`, `created_at` |
| `runs` | Space-owned | `id`, `thread_id`, `bot_id`, `principal_user_id`, `trigger`, `trigger_message_id null`, `parent_run_id null`, `root_run_id`, `parent_tool_call_id null`, `depth`, `status`, `status_reason`, `error jsonb`, `tainted`, `attempt`, `lease_owner null`, `lease_expires_at null`, `cancel_requested_at null`, `last_event_seq`, `usage jsonb`, `subtree_usage jsonb`, `started_at`, `finished_at` |
| `run_events` | Space-owned with an audience, inherited from the thread | `root_run_id`, `seq`, `run_id`, `thread_id`, `audience_user_id`, `type`, `data jsonb`, `created_at`. Primary key `(space_id, root_run_id, seq)` |
| `tool_calls` | Space-owned with an audience, inherited from the thread | `id`, `run_id`, `thread_id`, `audience_user_id`, `tool_id`, `tool_name`, `arguments jsonb`, `status`, `result jsonb`, `result_file_id null`, `is_error`, `decided_by_user_id null`, `decided_at null`, `child_run_id null`, `sandbox_id null`, `started_at`, `finished_at` |
| `usage_records` | Space-owned | `id`, `run_id`, `root_run_id`, `bot_id`, `model_connection_id`, `model`, `input_tokens`, `output_tokens`, `cached_tokens`, `cost`, `currency`, `created_at` |

Notes:

- `agent_sessions` (space-owned with an audience, inherited from the thread): `thread_id`, `bot_id`, `state jsonb`, `framework_version`, `updated_at`. The agent framework's serialized session, including any pending approval. Opaque working state: if a framework update cannot read it, it is discarded and the session is rebuilt from `messages`.
- `workflow_checkpoints` (same class, Phase 1): `run_id`, `checkpoint_id`, `parent_checkpoint_id null`, `state jsonb`, `framework_version`, `created_at`. Team runs resume from the latest one.
- `messages.parts` is an ordered array of Drover's own part records: `text`, `reasoning`, `image`, `file`, `tool_call`, `tool_result`. This is Drover's schema, defined in `Drover.Contracts`. No vendor message type is ever serialized into it.
- `messages.position` orders a thread. It is assigned under a lock on the thread row.
- `runs.principal_user_id` is the human the run acts for. A child run inherits it unchanged from its parent. Scheduled runs use the schedule's owner.
- `runs.root_run_id` equals `id` for a root run. `last_event_seq` is used only on the root row.
- `runs.status` values and transitions are in [03-run-lifecycle.md](03-run-lifecycle.md).
- `tool_calls` is a first-class table, not only events, because the activity view, the approval inbox, and restart recovery all query it by status.
- `tool_calls.arguments` and `result` are stored after redaction ([08-auth-and-secrets.md](08-auth-and-secrets.md)). Large results go to a file and the row keeps a preview.
- A thread is private to its owner by default. Sharing it with the space sets its audience to everyone, and the cascade carries that to its messages, events, and tool calls. A check constraint allows only two audiences for a thread, its owner or everyone, and the app role has no privilege to update `owner_user_id`, so one member cannot take over another's thread.
- A delegated child run's thread inherits the audience of the root thread.

## Sandboxes, files, secrets, audit

| Table | Class | Key columns |
| --- | --- | --- |
| `sandboxes` | Space-owned | `id`, `owner_bot_id`, `policy`, `provider`, `provider_sandbox_id`, `state`, `access_secret_id`, `last_used_at`, `expires_at`, `retire_after null` |
| `files` | Space-owned with an audience | `id`, `audience_user_id`, `storage_key`, `name`, `content_type`, `size`, `sha256`, `created_by_user_id null`, `created_by_bot_id null` |
| `secrets` | Space-owned with an audience | `id`, `audience_user_id`, `kind`, `ciphertext bytea`, `created_by_user_id`, `created_at`, `last_used_at` |
| `audit_log` | Space-owned, insert and select only | `id`, `actor_kind`, `actor_id`, `action`, `target_kind`, `target_id`, `data jsonb`, `created_at` |
| `sys.audit` | Global | One row per call of a cross-tenant function |
| `data_protection_keys` | Global, no app-role table access outside the key ring store | Key ring when stored in the database |

`sandboxes.state` is `creating`, `running`, `paused`, `retiring`, or `destroyed`.

Phase 1 adds `memories` (space-owned with an audience, with a `vector` column) and nothing else structural. A bot's memory of one user's private conversations is private to that user; memory from shared threads is shared.

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
