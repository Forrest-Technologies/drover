# 0025. Copilot personal plans sign in with GitHub's device-code flow until the official SDK path lands

- Status: Accepted
- Date: 2026-11-26
- Source: [#526](https://github.com/Forrest-Technologies/drover/issues/526)

## Context

Architecture docs say GitHub Copilot sign-in goes through the official Copilot SDK inside Microsoft Agent Framework. That path is not usable for a personal Copilot plan in Drover today. People with Pro, Pro+, or Max still need to run agents on the models their plan already pays for.

## Decision

`copilot_signin` is a compiled-in adapter behind `IModelClientProvider` and `IModelSignIn`. It uses GitHub's device-code flow, stores the GitHub token in the vault, and exchanges it for a short-lived Copilot session token on each refresh. Chat goes to Copilot's OpenAI-compatible endpoint with the editor and integration headers that endpoint requires. The official SDK remains the long-term path; this adapter is the interim, replaceable at the same seams.

## Alternatives considered

- Official Copilot SDK in the agent framework: the documented long-term path. It does not cover personal-plan sign-in for Drover today, so it lost for this step.
- Asking the operator for a GitHub App of their own: GitHub only exchanges Copilot tokens for a small set of client ids, of which the public VS Code Copilot Chat id is the one other open-source clients use. A self-hoster's App would not work.
- Waiting for ChatGPT-plan sign-in (Layer 5) before offering Copilot: Copilot's wait is a poll, not a browser return, so this adapter adds that wait to Layer 1 rather than blocking on a later layer.

## Consequences

A self-hoster gets Copilot with nothing to set. Usage bills the signed-in person's Copilot AI credits. The hosted preset still refuses personal-plan sign-in (`SubscriptionSignIn`). The public VS Code client id is a standing terms risk; see Reopen when.

## Reopen when

GitHub documents a supported way for a third-party app to use a personal Copilot plan through the official SDK, or GitHub restricts the VS Code Copilot Chat client id so this flow stops working.
