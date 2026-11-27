# Spend: how Claude Code fills the tiers in AGENTS.md section 15

| Tier | Model | Agent |
| --- | --- | --- |
| Top | `opus` | `ux-designer` |
| Standard | `sonnet` | `builder`, and the session itself |
| Light | `haiku` | `clerk` |

- The session runs on `sonnet`. Run it on `opus` only when the session's own job is interface design that is new, complex, or not obvious, or the owner asks for it by name. If you find yourself on a top-tier model doing anything else, say so in your first reply and suggest the owner restart on `sonnet`.
- Start workers by name: `builder` to build, fix, or review; `clerk` for CI logs, searches, clean rebases, and watching a pull request; `ux-designer` for the design itself, not for the code that follows it.
- Any other agent type needs an explicit `model` of `sonnet` or `haiku`. Searching and exploring is `haiku`.
- Do not subscribe to a pull request and stay open to watch it. Report the pull request and end the session. If the owner asks for it to be watched, hand that to a `clerk`.
- `.claude/hooks/spend-guard.mjs` refuses a worker that breaks these rules, a fork, a workflow, a ninth worker, and any worker once the conversation passes 150,000 tokens. A refusal is the rule working. Do not route around it; stop and tell the owner.
