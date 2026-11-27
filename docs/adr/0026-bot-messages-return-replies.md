# 0026. Bots can request replies within a space

- Status: Accepted
- Date: 2026-11-27
- Source: [issue 1075](https://github.com/Forrest-Technologies/drover/issues/1075)

Bots need to ask peers for advice and get a reply in the calling conversation. The direct-child delegation rule in [ADR 0005](0005-grants-narrow-down-the-tree.md) still applies to work that uses the receiving bot's tools.

The built-in `message_bot` tool uses the existing child-run lifecycle and is granted by default. A bot can accept or decline incoming requests through its editor. The run uses the target bot's instructions and model, and can return a reply or use its own grant for `message_bot` to ask another bot. The caller gets the answer as a tool result. Run-tree budgets, loop detection, tenant isolation, and tool rules apply to every hop.

A request confers no bot-management or action rights, and its run has no private memory or project context. The status of an incoming request cannot widen a tool grant.
