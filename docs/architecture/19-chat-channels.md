# 19. Chat channels

A chat channel lets a person talk with one of their bots from a messaging app they already use. The owner accepted the shape on 2026-11-21 ([issue 156](https://github.com/Forrest-Technologies/drover/issues/156)); [ADR 0018](../adr/0018-chat-channel-trust-boundaries.md) records the trust decisions. This section describes the behavior the layers of that decision build. The first app is Telegram, chosen for a free bot token, long polling without a public address, and no new library. The interface is written against two apps on paper so one app does not shape it.

## What a channel is

A channel belongs to one bot. There is one interface in `Drover.Domain` and one adapter project per app, each checked by a conformance suite. An adapter is a third party: it sees every message that passes through it, so the trust rules below decide what it is allowed to see and do.

A person links their account in the app to their Drover user with a short code that expires in minutes. The code is a one-time bridge, not a way to sign in to Drover. An unlinked sender gets one fixed reply and nothing else; their text never reaches a model.

## What crosses the boundary

- A message from a linked person lands in that person's main conversation with that bot, marked in words as having come from the app. There is one conversation, not a copy. The reply goes back to the app and is also in Drover, where the person can read it with their signed-in session.
- A linked message counts as that person for every rule and grant. The run it starts acts for them, so grants narrow the same way they do for a message sent in Drover, and row-level security sees the same user.
- The bot's words and one line saying it is working go back to the app. Approvals are never answered there: the bot's message says something is waiting and links to Drover. An app message can be forwarded or spoofed in ways a signed-in session cannot, so the one place a person's intent must be certain stays in the app they signed in to.
- Tool cards do not go to the app. The card, its arguments, its output and its buttons stay in Drover's conversation.
- The channel's token is a secret in the vault, read by the adapter at the moment of use under a tenant scope. It is never logged or returned by an endpoint, as [the secrets rules](08-auth-and-secrets.md) require for every vault secret.
- Bots only reply until scheduled messages get their own decision. A channel does not let a bot start a conversation with a person on its own.

## Operator control

A named deployment policy rule decides whether channels are allowed on a server and which adapters may run; the hosted preset decides separately. The rule and its hosted value arrive with [the deployment layer](https://github.com/Forrest-Technologies/drover/issues/726) of this decision. An adapter that is switched off is not registered, so no channel can reach it. Nothing is removed when a rule is tightened, so turning it on again restores what was there.

## What is not built yet

Scheduled messages to a channel, approvals or tool cards in an app, a channel that belongs to more than one bot, and any app beyond the first adapter are out of this decision. The layers that follow the decision are tracked in [issue 156](https://github.com/Forrest-Technologies/drover/issues/156).
