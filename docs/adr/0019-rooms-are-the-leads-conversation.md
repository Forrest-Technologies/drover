# 0019. A room is the lead's conversation

- Status: Accepted
- Date: 2026-11-21
- Source: [issue 155](https://github.com/Forrest-Technologies/drover/issues/155), owner decision 2026-11-21

## Context

Drover's interface rule says a person talks to a bot, not to a thread, with exactly one main conversation per person and bot. Bots answering each other in one place is the feature people single out, but it can turn into a loop that spends money while saying nothing. The choice had to be made before the work was split into layers. The existing section on [delegation and teams](../architecture/03-run-lifecycle.md#teams) and the [design direction](../architecture/09-design-direction.md#1-conversations) are the starting point.

## Decision

Option A. There is no new kind of conversation. When a bot leads its bots with a team pattern, the lead's conversation is the room: each member's contribution appears there, spoken from that member's own character, in order. This refines the interface rule, it does not replace it: the person still talks to a bot, and the room is that bot's one main conversation.

The person may mention any member bot by name to address it directly in the lead's conversation, as well as addressing the lead. Members are bots beneath the lead, so the existing delegation safety checks are reused unchanged: grants, budgets, the loop check, and cancel.

Between two messages from a person, bots may take at most four turns by default. The cap is configurable per space under a deployment ceiling, so a space cannot raise it above that ceiling; a space may set any value up to the ceiling, including one above four. The ceiling's own default is a decision for the policy layer [#715](https://github.com/Forrest-Technologies/drover/issues/715), which this ADR does not settle. The number four is a default proposed in the decision for the owner to confirm, not a number the owner chose.

## Alternatives considered

- A named group with its own conversation and its own entry in the crew. Rejected: it needs a second kind of conversation, a rule for who answers, a separate turn budget, and an answer to which grants apply to bots that are not in one subtree.
- No room yet, with better delegation cards instead. Rejected: the owner chose the visible room over waiting.
- Let only the lead be addressed. Rejected: the owner chose direct addressing of any member by name.

## Consequences

Every member's contribution has its own head and voice in one conversation, and the run stays one tree with one person as principal. All existing safety checks apply without a second permission model. A cap is needed because bots can otherwise keep answering each other; a space cannot raise it above the deployment ceiling. The room view and the cap setting arrive in the later layers of [issue 155](https://github.com/Forrest-Technologies/drover/issues/155).

## Reopen when

Work across bots that are not in one subtree becomes available, a room needs its own conversation and audience, or the four-turn default proves too low or too high in practice.
