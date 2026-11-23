# 0018. Trust boundaries for chat channels

- Status: Accepted
- Date: 2026-11-21
- Source: [issue 156](https://github.com/Forrest-Technologies/drover/issues/156), owner decision 2026-11-21

## Context

A person can reach their bots from an app they already use. That app is a third party that sees every message passing through it, and it is a new way into a space. The limits have to be chosen before the work is cut into tasks. The existing rules on [tenancy](../architecture/04-tenancy-and-permissions.md), [secrets](../architecture/08-auth-and-secrets.md), and [deployment policy](../architecture/16-deployment-policies.md) still apply; the question is what the app is allowed to do with a message.

## Decision

A chat channel belongs to one bot. A person links their app account to their Drover user with a short code that expires in minutes, and an unlinked sender gets one fixed reply with no model call.

A message from a linked person is written into that person's main conversation with the bot, marked in words as having come from the app. It counts as that person for every rule and grant, so the run it starts acts under the same grants and tenant scope as a message sent in Drover. The reply goes back to the app and is also stored in Drover; there is one conversation, not a copy.

Approvals and tool cards stay in Drover. The app receives the bot's words and one line when it is working, plus a link when something waits for a person. An app message can be forwarded or spoofed, so it cannot carry the certainty an approval needs. The channel's token is a vault secret, and bots only reply until scheduled messages get their own decision.

## Alternatives considered

- Let a person approve in the app. Rejected: an app message is not a signed-in session and cannot prove intent.
- Let the app answer approvals and show tool cards. Rejected: it would hand untrusted text the app's own view of arguments, output, and buttons, outside Drover's gate and audit trail.
- Give a channel its own conversation outside the person's main one. Rejected: the design keeps one conversation per person and bot, and a copy would drift.
- Treat a linked message as an untrusted prompt that acts for the channel rather than the person. Rejected: after an explicit link, the message is the person's own, so every rule and grant must apply to them.
- Let bots start messages to the app. Deferred: scheduled messages need their own decision.

## Consequences

The app sees message text and a working line, never a tool card or an approval. Linking is a short, expiring action, so a stray code cannot become a lasting sign-in. The channel token is a secret like any other and needs the same vault path. The first adapter is Telegram, with the interface written against a second app on paper so it stays general.

## Reopen when

A channel needs to carry a decision a person makes, a bot needs to start a message, or a second app cannot be served without changing the trust rules above.
