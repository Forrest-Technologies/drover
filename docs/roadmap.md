# Drover roadmap

Written 2026-11-23 against `main` at `73f2d528`.

This file is the plan from where Drover is today to a mature product. [`ROADMAP.md`](../ROADMAP.md) names the phases. This file breaks the road into milestones small enough to finish in days, and links every piece of work to its issue. It holds no status: an issue is the source of truth for its own state.

- Planned and ready work carries the [`roadmap` label](https://github.com/Forrest-Technologies/drover/issues?q=label%3Aroadmap). The label means the issue has all seven sections listed under [How work flows](#how-work-flows).
- Each milestone below is a [GitHub milestone](https://github.com/Forrest-Technologies/drover/milestones).
- Interface designs are in [`docs/ux`](ux/README.md). Wording follows the [copy guide](ux/copy-guide.md).

## The bar

Drover should be a mature product: fully featured, its gaps filled, and calm to use.

- **Complete.** Everything a person expects from a platform for running their own bots is there from the first day: chat, tools, memory, files, schedules, delegation, sandboxes, and apps for every device.
- **Yours to shape.** Enough can be changed that a person feels real control: providers, tools, rules, themes, what the interface shows and what it leaves out.
- **Open.** Apache 2.0, the whole stack.
- **Secure by default, never limited without a reason.** Defaults are strict where a mistake costs something. Every limit is a setting someone can read and, where it is safe, change.
- **Extensible.** A new model provider, tool, sandbox or agent runtime is one adapter behind an interface Drover owns.
- **Current.** New tools and protocols are brought in as they appear.
- **Calm.** The interface shows the work and stays out of the way. No element sits in a conversation when a quieter place would serve. Words are plain.

## Where Drover is today

Checked against the code on the trunk, not against older documents.

| Area | On the trunk |
| --- | --- |
| Server | One .NET server with PostgreSQL. A run is a durable job with a stored event log. Clients resume a stream where it stopped. One `docker compose up` deploys it, with an optional HTTPS proxy |
| Bots | A hierarchy of bots, each with a character, instructions, a model and tool grants. Delegation to the bots beneath, with depth, run and cost limits. Rules at four points of a run. A rule that was removed can be put back with Undo. A conversation a bot works in for another bot refuses writes from people: posting, editing, regenerating, showing a version and retrying are refused. Routines on a schedule. Memory notes per person. Projects with shared files. Extensions as one file |
| Conversations | Streaming replies with tool activity, edit and regenerate, side chats, a running summary, search across conversations, export as Markdown or JSON, attachments, document reading, safe previews. One bot turn now reads as one unit with a compact ledger of its work, with one row for each piece of work that opens to the call. A theme sets colors, type, density, shape and motion. Large pictures get a static preview of at most 480 pixels on the long edge and the original can still be downloaded. Pictures can be pasted and files dropped anywhere on a conversation, with one sentence when one cannot be taken. The first 100 files are taken, and a pasted picture is bounded to 64 MiB and 32 million pixels |
| Code and output | Code, JSON, tables, differences, logs and bytes in a conversation are drawn from what they hold. Lines can wrap, and a block can open full screen. Past 400 lines or 64 KiB only the start is drawn until a person asks for the rest; a table past 400 rows gets a button for the rest |
| Tools | MCP tool servers with no auth, a header or a sign-in. Built-in tools for time, web pages, web search, memory, images, delegation and the crew |
| Sandboxes | Optional, turned on with one sandbox service key in `.env` and a restart; the status page names the setting when none is set. A sandbox runs commands, handles files, shows a screen a bot can use, and a person can watch and take over. A finger or a mouse can press, move and release a pointer, drag a window, right-click with a long press, and scroll with two fingers. Held buttons are let go after 10 seconds idle or 60 seconds in all, more than eight waiting touches are turned away, and input is bounded per person and space. Uploads use a writable directory in the image; a failed write answers 503 with `storage_unavailable` |
| Models | Two kinds of connection: one hosted provider with its own adapter, and any server that speaks the chat completions API. A stored catalog of each connection's models. Stored fallback lists. Usage and cost per run and per bot |
| Clients | One client codebase. The browser build ships with the server, and an Android preview is released. A share opened on a cold start of Android reaches the share screen and is cleared when a session or a server changes. Desktop builds for Linux, Windows and macOS and an iOS simulator build pass checks on every pull request, and none of them is released yet |
| People | Password sign-in, a setup code for the first account, device sessions. Each person has their own space. Roles exist (owner, admin, member), and there is no way yet to invite a second person |
| Security | Row-level security in PostgreSQL enforces isolation between spaces and people. Grants are checked at call time and narrow down the hierarchy. Secrets are encrypted with a key ring and never returned. A deployment policy sets every limit, and limits only narrow. Containers run read-only with capabilities dropped. Every pull request runs code scanning, secret scanning, dependency and license checks |
| Documentation | `README.md`, `AGENTS.md`, `CODE_STYLE.md`, `DESIGN.md`, nineteen architecture files, eighteen decision records, and guides for self-hosting, desktop installers and the Android preview |

## Gaps between the bar and today

| Goal | Gap | Milestones |
| --- | --- | --- |
| Complete | Upload failures now say why, and a cold-start share reaches the share screen. A deployment still has to mount a persistent upload volume and keep it writable | 01 |
| Complete | Some screens stop with no next step, and typed work can be lost | 03 |
| Complete | Runs can hang, ignore Stop, or fail with no useful reason | 04 |
| Complete | No desktop or iOS release. No app update notice | 08 |
| Complete | No shared spaces in practice: nobody can be invited | 09 |
| Complete | A room is recorded as the lead bot's conversation with a cap on bot replies between a person's messages (ADR 0019), but no room view is built. Searchable text and a bounded finder are in place without a find control. Message actions, voice and offline reading are open | 13, 18, 19, 20 |
| Yours to shape | One look in two modes. No shortcuts list, no command palette, few display choices | 12, 14 |
| Secure | Known defects in sandbox network rules, sessions, logs and outbound calls | 02 |
| Secure | No single page that says what is protected and how it is proven. No proof of who may call each route | 02 |
| Secure | Password only. No identity provider, no second step, no readable audit log | 09 |
| Secure | No spend ceiling that holds under concurrent runs. No retention schedule. No metrics | 11 |
| Extensible | Adding a model provider means editing the server wiring and every client. One agent runtime, with no way to add another | 07 |
| Extensible | One sandbox provider, and a conformance suite that assumes it | 10, 16 |
| Current | No job that warns when an upstream prerelease breaks the build | 11 |
| Calm | Usage and timing shown under every reply. A card for every tool call. Some interface text is cute or roundabout | 12 |
| Easy to start | Setup refuses common server addresses. No contributor guide. No backup command | 05 |

## How work flows

1. **Every issue has acceptance criteria and named tests.** An issue that carries the `roadmap` label has seven sections: Problem, Scope, Out of scope, Acceptance criteria, Test cases, Security notes, Dependencies. "Test cases" names at least one concrete test that fails before the change and passes after it. A change is done when those tests pass.
2. **At most two review rounds per pull request.** After the second round, only a security hole or a break of existing behavior on `main` blocks the merge.
3. **Follow-ups become new issues.** Anything found outside the issue's named tests that cannot be fixed inside the two rounds is filed as a new issue, with its own acceptance criteria and its own named test. It never becomes a third round.

One issue, one branch, one pull request, as `AGENTS.md` section 14 says. An interface change is built from its spec in `docs/ux`, and its pull request carries screenshots of each state.

## Order

Milestones 01 to 05 make Drover dependable: uploads, security, no dead ends, runs that behave, and a clean first hour. They come before new features.

Milestones 07 and 08 start at the same time as 01. They touch other code and need other skills: provider and runtime interfaces, and release signing.

Milestone 06 lands work that is already written and reviewed. It follows 05.

Milestones 09 to 12 fill the largest gaps: people, sandboxes, operations, and a calm conversation.

Milestones 13 to 20 complete the product. Their issues are assigned and will be given the seven sections before each milestone starts.

## Milestones


### 01 Uploads and Android basics

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/1)

**Goal.** A file of any size uploads from any client and continues after a dropped connection. Sharing and pasting on Android stop losing files. The checks that guard these stop failing at random.

**Issues.**

- #376 test: install the Android APK on a real device and run the device checklist
- #814 fix: on Android one unreadable item drops a whole share silently, and shared files are copied with no size bound
- #821 feat: chunked, resumable uploads end to end, with no total size needed up front
- #834 fix: Android share staging deletes or leaks files when receives overlap or a copy fails
- #835 fix: a pending Android share can outlive the identity or draft it was received for
- #836 fix: bound the memory a pasted picture can hold, and keep a paste error on its own conversation
- #839 test: the MCP session re-open conformance test can fail on a stale pooled connection after a server restart
- #840 test: the Android safe-area check sometimes finds no dark rows at the bottom of the screen
- #841 fix: pushing an Android preview tag starts the desktop release workflow and fails its version check (pull request #842 is open)
- #844 feat: an attachment shows how far its upload is, and can be retried or cancelled

**Interface specs.** [uploads.md](ux/uploads.md)

**Exit criteria.**

- A file at the server's size limit uploads from the Android app over a connection that drops twice, and arrives whole.
- Every named test in #821 passes, and the manual Android proof run is linked.
- The Android job and the tool server conformance test pass 20 runs in a row.
- The Android device checklist has one recorded run with an issue for each failed row.

### 02 Security holds

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/2)

**Goal.** The security defects known on `main` are closed, the threat model is written down with the test behind each control, and one table proves who may call every route.

**Issues.**

- #456 fix: closing another space's idle tool session runs inside someone else's tool call
- #460 fix: signed-out requests to health and setup scan all runs and write an audit row each
- #461 fix: signing in, registering or resetting with another account's cookie present answers 500
- #462 fix: reading an uploaded document has no time limit, no concurrency cap, and loads whole Office parts
- #463 fix: one person can use up the server-wide HTML preview grants
- #465 fix: model provider clients follow redirects and share one cookie jar across spaces
- #466 fix: device session refresh shares the sign-in rate bucket, and refresh tokens are never rotated out
- #467 fix: one request type prints its secret when logged, and tool, search, push and model failures are logged whole
- #512 docs: say what Drover encrypts at rest, and keep search indexes under the same row-level security as messages
- #632 fix: a network restriction can silently fail to apply when more than one server process runs
- #656 fix: a sandbox with its network off or limited may still reach out over IPv6
- #808 Select a writable container key ring while preserving existing keys on upgrade
- #824 fix: coordinate a screen handback with the display it hands back (follow-up to #797)
- #829 fix: authorize sandbox power controls inside the broker's dispatch
- #845 docs: a threat model that names the test behind every control
- #846 test: one table proves who may call every route

**Exit criteria.**

- Every issue in the milestone is closed with its named test in CI.
- `docs/security/threat-model.md` is on `main` and every test it names exists.
- `RouteAccessTests` is green and a route without a manifest line fails the build.
- No open issue describes a way to read another space's data or to leave a sandbox's network rule.

### 03 No dead ends

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/3)

**Goal.** No screen crashes, loses what a person typed, or stops with no next step.

**Issues.**

- #554 feat: drafts and unsent attachments survive switching bot, going back, and restarting
- #555 fix: a conversation that fails to load says "Nothing said yet" and cannot be retried
- #556 fix: "Reload the page" is a dead end: recover a lost reply stream in place, and unlock approval cards
- #557 fix: an edit to an earlier message is lost when sending it fails
- #558 fix: Enter sends a half-composed message, and a phone or tablet with a keyboard cannot send from it
- #560 fix: a streaming reply is rebuilt on every update, losing selection, focus and scroll, and slowing long replies
- #562 fix: an ended session shows errors with no way to sign in, and a refused signal stream is never retried
- #563 fix: toasts lose focus on every change, errors pile up forever, and they can cover the composer on a phone
- #569 fix: every screen survives opening while the server is unreachable, and a test keeps it that way
- #570 fix: New bot and setup can pick a failed model connection
- #572 fix: the bot and extension editors lose unsaved work without asking
- #575 fix: a bot rule is removed on one press with no way back
- #576 fix: Use another server forgets the server on one tap
- #752 Native client: hide the composer on a delegation conversation

**Interface specs.** [recovery-states.md](ux/recovery-states.md)

**Exit criteria.**

- With the server stopped, every screen opens to a failed state with a working Try again.
- A reply lost mid-stream stays on screen and is recovered with one press.
- A draft, an unsaved bot and a half-written rule each survive leaving and coming back.
- Every state in `recovery-states.md` exists with its exact words.

### 04 Runs you can trust

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/4)

**Goal.** A run ends, stops when told, says why it failed, and counts what it used.

**Issues.**

- #434 fix: a failure before the agent starts blocks the conversation, then fails as "interrupted 3 times"
- #435 fix: posting into a delegation conversation starts a second run inside a child's conversation (pull request #753 is open)
- #436 fix: a reply naming an unknown tool before a writing tool can hang a run that Stop cannot end
- #437 fix: a model reply that goes quiet mid-stream never ends, and a slow model is cut off at 100 seconds
- #438 fix: a failed model call says what went wrong, and a model's refusal reads as its reply
- #439 fix: usage is lost when a streamed call fails or is cancelled, and when two tools call a model at once
- #440 fix: a stream on a dead connection reconnects by itself, and a slow upload or a timeout reads as a sentence
- #442 fix: delegated work stays "waiting" after Stop until reload, and a run's end is announced before it is saved
- #444 fix: a tool call cut off by Stop stays "running", and a tool that times out fails the whole run
- #447 fix: removing a bot and keeping its children drops its rules from them (pull request #770 is open)
- #448 fix: pausing a bot leaves the bots beneath it running and does not stop runs in progress
- #451 fix: push retries are lost on restart, refused credentials retry forever, and one bad request removes a device
- #452 fix: the bundled notification server cannot be used, and the docs say push is not built
- #457 fix: when two tools' names clash after cleaning, one disappears without a word (pull request #767 is open)

**Interface specs.** [recovery-states.md](ux/recovery-states.md)

**Exit criteria.**

- Stop ends every run in the engine's conformance and regression tests, with no tool call left running.
- A failed model call shows one of the fixed sentences, never a raw provider message.
- Usage is recorded for every call, including a cancelled or failed stream.
- Pausing a bot stops the bots beneath it and their runs in progress.

### 05 First hour

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/5)

**Goal.** A new self-hoster gets from `git clone` to a first conversation, and a new contributor from clone to a first pull request, without help.

**Issues.**

- #17 Step 16. docs: daily-driver pass, self-hosting guide, and v0.1.0
- #43 fix: four tests depend on the machine they run on (pull request #744 is open)
- #52 feat: change a model connection's address and remove a model connection
- #454 fix: a compatible model connection says Connected for a wrong address, and four smaller adapter defects
- #474 feat: backup and restore are two tested commands, and restarting the server never costs a run an attempt
- #476 feat: password reset and registration codes arrive by email once an operator sets up SMTP
- #477 feat: operator commands: list people, disable an account, make someone an owner
- #479 docs: the architecture documents, AGENTS.md and the roadmap describe what the code does today
- #583 feat: create an account on a server with open registration
- #584 feat: change your password, and use a reset code from the operator
- #592 feat: connecting to a server takes what you type, finds a server on this computer, and says what went wrong
- #627 fix: on a Mac the browser tests cannot find Chrome, and a missing PostgreSQL fails every test with no reason (pull request #745 is open)
- #662 docs: turning sandboxes on takes one line, the guide says which, and the status page names what is missing (pull request #742 is open)
- #847 docs: CONTRIBUTING.md takes a new contributor from clone to a first pull request
- #848 chore: the Task issue form asks for named tests and security notes

**Interface specs.** [first-run.md](ux/first-run.md), [providers.md](ux/providers.md)

**Exit criteria.**

- On a clean machine the README and the self-hosting guide lead to a working server with a backup that restores.
- The desktop app connects to a server on the same computer with one press.
- A person can create an account on an open server, change a password, and use a reset code.
- `CONTRIBUTING.md` works top to bottom on Linux and macOS.
- Version 0.1.0 is tagged.

### 06 Open stacks land

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/6)

**Goal.** Reviewed work that waits in open pull requests reaches `main`: code and output blocks, the turn ledger, icon buttons, the first layer of find in a conversation, and the theme base.

**Issues.**

- #421 docs: design a family of themes beyond one look, with seven named themes (pull request #786 is open)
- #422 feat: themes as data, applied at run time for every token group (pull request #787 is open)
- #423 test: accessibility gates that every theme must pass (pull request #794 is open)
- #424 feat: choose and preview a theme on an Appearance page (pull request #791 is open)
- #678 feat: one bot turn, one compact ledger of work (pull request #734 is open)
- #680 feat: full-screen view for a code or output block (pull request #696 is open)
- #682 feat: icon button sweep in the native client (pull request #735 is open)
- #697 feat: search layer 1.1, MarkdownText, FindableText and TextFinder (pull request #751 is open)
- #720 docs: rooms, an ADR and the architecture sections (pull request #748 is open)
- #728 build: bundle an open color emoji font under SIL OFL 1.1 with its license file (pull request #750 is open)

**Exit criteria.**

- Each listed pull request is merged or closed with a reason.
- No open pull request older than this milestone targets the integration branch.

### 07 Provider plugins

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/7)

**Goal.** A model provider or an agent runtime is added as one adapter project with one registration, and a conformance suite proves it keeps Drover's rules.

**Starts now.** This milestone does not wait for the ones before it. It shares no code with them.

**Issues.**

- #482 test: the test doubles pass the same conformance suites as the real adapters
- #483 test: live provider tests run weekly and report when they break
- #527 feat: an Anthropic model adapter: tool use, thinking, prompt caching, PDF input
- #528 feat: an OpenAI adapter on the Responses API
- #529 feat: a Google Gemini model adapter
- #530 fix: Azure OpenAI cannot be connected by pasting its endpoint and key
- #531 feat: a model's abilities, context size and price are read from the server, and an admin fills in the rest
- #534 feat: an embeddings interface and a compatible-endpoint adapter
- #849 feat: one registration makes a model provider kind available to the server and every client (good first issue)
- #850 feat: Connect a model offers every provider kind the server has, with that kind's own fields
- #851 docs: a provider author's guide and a sample adapter that passes the conformance suite (good first issue)
- #852 decide: agent runtimes as providers, and what an external agent may do
- #853 feat: a bot can be run by a registered agent runtime, proven by a conformance suite
- #854 feat: a first external agent SDK as an agent runtime (good first issue)
- #855 feat: choose what runs a bot in the bot editor

**Interface specs.** [providers.md](ux/providers.md)

**Exit criteria.**

- A provider can be added by following `docs/providers.md` alone, and the sample adapter passes the suite in CI.
- The client names no provider. It draws its forms from what the server offers.
- Three more model adapters pass the conformance suite.
- The decision record for agent runtimes is approved, and one external agent SDK runs a bot under the tool gate.

### 08 Apple apps

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/8)

**Goal.** A signed, notarized macOS app and a signed iOS build come out of a release tag.

**Starts now.** This milestone does not wait for the ones before it. It shares no code with them.

**Issues.**

- #513 feat: a server says which app versions it supports, and an old app is told to update
- #611 build: install and launch each desktop package before a release is published
- #612 feat: the Android app says when a newer version is out
- #613 build: desktop packages for both processor families on Windows, macOS and Linux
- #614 fix: the installed desktop app has no icon of its own on macOS or in Windows notifications
- #617 feat: on iOS a person can save and share a file, come back from a tool sign-in, and send a file into Drover
- #619 test: the version in About matches the release that was installed
- #856 build: the first signed and notarized macOS release from a tag (good first issue)
- #857 build: a signed iOS build from a release tag, delivered to testers (good first issue)
- #858 feat: the push relay for the iOS app, server side
- #859 test: run the iOS app on a real device against a written checklist

**Interface specs.** [app-updates-and-ios.md](ux/app-updates-and-ios.md)

**Exit criteria.**

- A release tag yields a macOS package that Gatekeeper accepts on a clean Mac.
- A release tag yields an iOS build a tester can install on a phone.
- The iOS device checklist has one recorded run.
- An app too old for its server says so and offers the update.

### 09 People and spaces

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/9)

**Goal.** An owner invites people, sets roles and removes them. A person signs in through an identity provider or with a second step. Owners and admins can read who changed what.

**Issues.**

- #55 chore: follow-ups from the second review of #54 (queue, steer, approvals)
- #469 feat: account events leave a trace, and changes to model connections, memory and runs are audited
- #517 feat: invite people to a space, set their role, remove them, and leave
- #518 feat: a Members screen in the native client
- #523 feat: change your name, email and time zone
- #524 feat: sign in through an identity provider (OpenID Connect)
- #590 feat: read the audit log of a space, filter it, and export it
- #591 feat: switch between the spaces you belong to
- #860 feat: a second step at sign-in with an authenticator app, and recovery codes

**Interface specs.** [members-and-spaces.md](ux/members-and-spaces.md), [sign-in.md](ux/sign-in.md), [audit-log.md](ux/audit-log.md)

**Exit criteria.**

- Two people share a space, and removing one ends their access on every route and stream at once.
- Sign-in through one identity provider works in the browser and in a device app.
- A second step at sign-in can be turned on, used, and recovered from with a code.
- The audit log shows membership, bot, tool and rule changes, and exports them.

### 10 Sandbox foundations

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/10)

**Goal.** A sandbox reports its true state, stops when told, and survives a policy change, behind a provider interface a second provider can pass.

**Issues.**

- #628 fix: a long command makes a running sandbox read as paused and hides its screen
- #629 fix: a cancelled or failed command keeps the sandbox running for its full time limit and leaves no record
- #631 fix: changing a bot's sandbox policy strands its sandbox at the provider
- #633 feat: Drover checks its sandboxes against the provider: orphans removed, lost ones marked, unused ones retired
- #634 fix: the status page hides a rejected sandbox key and sandboxes the provider will not remove
- #637 fix: the native client cuts sandbox requests at 100 seconds though commands may run five minutes
- #638 fix: a bot's long command blocks a person's clicks, typing and commands for its whole length
- #639 feat: sandbox power controls that hold: Pause stays paused, Restart keeps files, Reset never ends with nothing
- #645 fix: one click on a sandbox screen costs about six provider round trips instead of one
- #646 fix: stopping or timing out a sandbox command leaves its child processes running
- #647 test: the sandbox conformance suite covers what the engine relies on and stops assuming HTTP
- #648 feat: a sandbox provider says what it can do, and the editor, the tools and the status go by it
- #649 feat: an optional setting names the sandbox provider, and a bot left on an old provider gets a new sandbox

**Exit criteria.**

- Every sandbox defect in the milestone is closed with its named test.
- The sandbox conformance suite covers network rules, stop behavior and file bounds, and does not assume one transport.
- Drover finds and removes a sandbox it no longer tracks.

### 11 Operate it

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/11)

**Goal.** An operator can cap spend, keep and purge data on a schedule, and watch the server with metrics.

**Issues.**

- #203 feat: a model with no known price cannot run past a server's cost limits
- #343 Enforce durable turn, handoff and timeout limits at deployment and bot level
- #344 Reserve model spend before dispatch and cap cumulative spend per space
- #468 chore: remove stored bytes with no row, explain the preview address error, and name unenforced hosted rules
- #470 feat: expired rows are purged on a schedule, with retention for audit rows, sessions, runs and events
- #471 feat: metrics and trace export for operators
- #472 fix: a failing background loop cannot be diagnosed, and bad run settings start a server that does nothing
- #538 feat: usage by person, by model and by day, for any range, with export
- #861 build: a weekly job builds against the newest upstream prereleases and opens an issue when it breaks

**Exit criteria.**

- A space cannot spend past its ceiling, including with concurrent runs and unpriced models.
- Expired rows are purged on a schedule with a stated retention for each kind.
- Metrics and traces export to an endpoint the operator sets, with no message content in them.
- A weekly job reports when an upstream prerelease breaks the build.

### 12 Calm conversation

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/12)

**Goal.** The conversation holds what was said and what needs an answer. Usage, timing and the model's name sit in one quiet line above the message field, and a person can turn it off. Interface text is plain words.

**Issues.**

- #582 fix: interface text that names a browser, a page or the retired web app inside a device app
- #594 feat: the conversation shows which model is answering, how long a run has taken and what step it is on
- #598 feat: messages carry their time, with day separators
- #600 feat: one short Settings screen: your name, password, where you are signed in, appearance and notifications
- #862 feat: what a reply used moves out of the conversation to a status line above the message field
- #863 feat: choose how much of a bot's work shows in the conversation
- #864 fix: plain words in setup, models, bots, tools and extensions
- #865 fix: plain words in conversations, search, usage and memory
- #866 test: interface text that breaks the copy guide fails the build

**Interface specs.** [conversation-display.md](ux/conversation-display.md), [copy-guide.md](ux/copy-guide.md)

**Exit criteria.**

- No model name, token count, cost or elapsed time is drawn between or under messages.
- Every display extra has a default and a switch in Settings, as its spec states.
- Every string the copy audit listed is replaced, and the copy rule test is green.
- A long tool-heavy reply reads as one line and its text by default.

## Later milestones

These complete the product. Each has its issues assigned on GitHub. Those issues do not carry the `roadmap` label yet: before a milestone starts, each issue is checked against `main`, given the seven sections, and given its interface spec where it has an interface.


### 13 Conversation tools

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/13) · 35 issues

Act on a message (copy, quote, edit, another model, reactions), find anything said in a conversation or across them, rename, archive and delete conversations, slash commands, and the remaining interface defects.

### 14 Make it yours

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/14) · 20 issues

More themes and a theme editor, keyboard shortcuts with a list of them, a command palette, the device's text size, notification choices, a bot's look, and starting-point bots.

### 15 Tools and reach

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/15) · 26 issues

More built-in tools, pictures and files from tool servers, tool server resources and prompts, a catalog of ready tool servers, OpenAPI descriptions as tools, and Drover usable from other programs with access tokens.

**Issues.**

- #880 feat: a bot's reply can show choices, a short form, a table or a checklist drawn with Drover's own controls
- #544 feat: a bot asks the person a question, with choices or your own words, and waits for the answer

### 16 Sandbox experience

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/16) · 41 issues

A sandbox a person can see, watch and take over with nothing to set up: a live screen and a terminal in the client, Docker sandboxes with no account anywhere, sandbox files, and secrets a bot can use without seeing them.

**Issues.**

- #207 feat: a bot asks for a credential on a card, Drover keeps it, and the bot gets only a handle
- #885 feat: a bot uses a credential by its handle in a command or a file in a running sandbox (part A)
- #885 feat: a bot uses a credential by its handle in a browser field in a sandbox (part B)

### 17 Crew at work

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/17) · 38 issues

Routines that say when they fail, work items and check-ins, skills, approvals that fit real work, who a bot works with, and the Crew and Activity screens.

### 18 Shared spaces and channels

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/18) · 29 issues

Rooms with several bots and people, mentions and read state, shared conversations, limits an owner sets for a space, export and deletion of a person's data, passkeys, and bots reachable from a chat channel.

### 19 Voice and memory

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/19) · 19 issues

Speak to a bot and hear replies, conversations shortened to fit the model, prompt caching, memory a person can search, and model fallback that switches during a run.

### 20 Every device

[Milestone on GitHub](https://github.com/Forrest-Technologies/drover/milestone/20) · 26 issues

Offline reading and a send queue, a desktop tray and more than one window, sharing into the app, translation, links into the app, and release checks on every platform.

## Good first issues

For someone new to the code who wants a piece of work with clear edges. Each is marked `good first issue`.

| Issue | Why it is a good start |
| --- | --- |
| #849 A provider registry | The interface and the conformance suite exist. The work is one descriptor, one registration call and one route |
| #851 A provider author's guide and a sample adapter | Reading two adapters and writing down the path is the fastest way to learn the provider boundary |
| #854 A first external agent SDK as an agent runtime | For someone who knows an agent SDK well. It waits for #852 and #853, so read and review those first |
| #856 The first signed and notarized macOS release | The job exists and has never run with real signing material. An Apple developer account is needed, and the owner will arrange it |
| #857 A signed iOS build from a release tag | A contained release job, with a clear proof: a build a tester can install. The same account is needed |

## Not in this plan

- A hosted service run by the project. The deployment policy keeps that possible. Nothing here builds it.
- A public store listing for the mobile apps.
- Loading provider code at run time. #849 records the decision against it.

## Keeping this file true

This file changes when a milestone is added, split or finished. It never holds the status of an issue. When the plan and an issue disagree about scope, the issue wins and this file is fixed.
