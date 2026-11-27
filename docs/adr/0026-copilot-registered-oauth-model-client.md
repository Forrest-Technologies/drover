# 0026. Copilot sign-in uses a registered GitHub OAuth app; chat stays a Drover model client

- Status: Proposed in its pull request
- Date: 2026-11-27
- Source: [#526](https://github.com/Forrest-Technologies/drover/issues/526), [#1065](https://github.com/Forrest-Technologies/drover/pull/1065), [GitHub OAuth setup](https://docs.github.com/en/copilot/how-tos/copilot-sdk/setup/github-oauth)
- Supersedes: [0025](0025-copilot-device-code-until-sdk.md)

## Context

GitHub now documents a third-party path: the operator registers an OAuth app or GitHub App, each person authorizes it, and Copilot usage bills that person's subscription. ADR 0025's reopen condition fired. The same pages pass that token to `CopilotClient`, which is a coding-agent runtime with its own tools and permission handler, not a chat client.

## Decision

`copilot_signin` stays behind `IModelSignIn` and `IModelClientProvider`. Sign-in is GitHub's OAuth for an app the operator registers. The GitHub user token is stored in the vault, private to that person, and is the credential a model call sends. Chat stays one `IModelChatClient` call: Drover's messages and tool declarations in, a completion out. Microsoft Agent Framework remains the agent. Drover does not reference the Copilot SDK and does not run Copilot's tools, sessions, or `PermissionHandler`. Editor-impersonation headers and `copilot_internal` token exchange are not used.

## Alternatives considered

- Copilot SDK as the run: it would be a second agent, execute tools Drover did not grant, and download a native runtime into the server. It lost to [0001](0001-build-on-agent-framework-in-one-project.md) and [0005](0005-grants-narrow-down-the-tree.md).
- Wrapping the SDK with no tools as a chat client: still a Copilot session and runtime, and Drover tools would have to be re-hosted inside it. Lost; the model-provider seam is HTTP chat, not a nested agent.
- Keeping the public editor client id and internal exchange: that is the path GitHub's docs now distinguish from the registered-app route. Lost.

## Consequences

A self-hoster registers a GitHub OAuth app or GitHub App and sets its client id (and secret, for a browser return). Desktop uses the existing `Return` wait; a head with no listener uses GitHub's device flow of that same app (`Poll`). Usage still bills the signed-in person. The hosted preset still refuses personal-plan sign-in. Whether a token from that app can list models and complete chat without the internal exchange is a Live check; if it cannot, this decision is reopened rather than impersonating another client.

## Reopen when

A registered-app GitHub token cannot call Copilot as a model, or GitHub documents a model-only client in the Copilot SDK that maps to `IModelChatClient` without a second agent loop.
