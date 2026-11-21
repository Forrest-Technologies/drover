# 19. Chat channels

Status: decided on 2026-11-21, not built. Nothing described here exists in code yet; the layers that will build it are tracked in [issue 156](https://github.com/Forrest-Technologies/drover/issues/156). This section records the design so implementation layers do not re-litigate it. [ADR 0018](../adr/0018-chat-channel-trust-boundaries.md) records the trust decisions.

## What a channel will be

A channel will belong to one bot. Drover will add one interface in `Drover.Domain` and one adapter project per app, each checked by a conformance suite. An adapter is a third party: it will see every message that passes through it, so the trust rules below decide what it may see and do.

A person will link their account in the app to their Drover user with a short code that expires in minutes. The code will be a one-time bridge, not a way to sign in to Drover. An unlinked sender will get one fixed reply and nothing else; their text will never reach a model.

## What will cross the boundary

- A message from a linked person will land in that person's main conversation with that bot, marked in words as having come from the app. There will be one conversation, not a copy. The reply will go back to the app and will also be in Drover, where the person can read it with their signed-in session.
- A linked message will count as that person for every rule and grant. The run it starts will act for them, so grants will narrow the same way they do for a message sent in Drover, and row-level security will see the same user.
- The bot's words and one line saying it is working will go back to the app. Approvals will never be answered there: the bot's message will say something is waiting and link to Drover. An app message can be forwarded or spoofed in ways a signed-in session cannot, so the one place a person's intent must be certain will stay in the app they signed in to.
- Tool cards will not go to the app. The card, its arguments, its output and its buttons will stay in Drover's conversation.
- The channel's token will be a secret in the vault, read by the adapter at the moment of use under a tenant scope. It will never be logged or returned by an endpoint, as [the secrets rules](08-auth-and-secrets.md) require for every vault secret.
- Bots will only reply until scheduled messages get their own decision. A channel will not let a bot start a conversation with a person on its own.

## Operator control

A named deployment policy rule will decide whether channels are allowed on a server and which adapters may run; the hosted preset will decide separately. The rule and its hosted value arrive with [the deployment layer](https://github.com/Forrest-Technologies/drover/issues/726) of this decision. An adapter that is switched off will not be registered, so no channel can reach it. Nothing will be removed when a rule is tightened, so turning it on again will restore what was there.

## What is not built yet

None of this section is built. [Issue 156](https://github.com/Forrest-Technologies/drover/issues/156) splits the work into the Domain interface and conformance suite (#722), records and link codes with the fixed reply to unlinked senders (#723), linked messages into the main conversation (#724), the first adapter (#725), the deployment policy rule and hosted preset row (#726), and the link flow screen (#727). Scheduled messages to a channel, approvals or tool cards in an app, a channel that belongs to more than one bot, and any app beyond the first adapter are out of this decision.
