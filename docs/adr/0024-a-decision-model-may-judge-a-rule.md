# 0024. A decision model may judge a rule written as a sentence, and the judgment only narrows

- Status: Proposed in its pull request
- Date: 2026-11-26
- Source: the owner's direction of 2026-11-26 in [issue 1018](https://github.com/Forrest-Technologies/drover/issues/1018), and [issue 80](https://github.com/Forrest-Technologies/drover/issues/80), slice 1 of it. Reverses the earlier plan in issue 80 to judge with an ordinary chat model and no new interface

## Context

A bot rule is decided by code: typed conditions, one evaluator, no model asked whether a rule applies
([03-run-lifecycle.md](../architecture/03-run-lifecycle.md#rules)). That keeps rules exact, and it leaves out the rules
people state most often, which are about meaning: "Always ask me before sending an email", "Always verify the invoice
total matches the purchase order before paying it". No typed condition can say whether an action is "sending an email"
in every form a tool can take.

## Decision

- A rule may hold **one condition of kind `judged`**: a sentence a person wrote, and how sure the check must be
  (0.50 to 0.99, 0.80 by default). Everything else about the rule is as for any rule: checkpoint, effect, order,
  inheritance down the bot tree, audit entries, `rule.fired`, "Try it". Only `before_tool` and `before_reply`, never
  `redact`.
- A **decision model** judges it: a model that answers typed questions with probabilities and writes no text. It sits
  behind `IDecisionModel` in `Drover.Domain.Decisions`, a new interface Drover owns, because its shape (a state and
  questions in, probabilities out) is not a chat. It is never the bot's model and is never given a tool.
- **Code decides.** The evaluator stays pure and synchronous and gains two steps around the network call: which judged
  rules still need a verdict, then the outcome from the verdicts handed in. The model's probability is compared with
  the rule's own number; at or below one minus it the rule does not apply; between, nobody is sure.
- **It only narrows.** Grants, policy and rules decided by code come first, and what they refuse is never sent. A
  judged rule can ask, block, or stop. It can never allow, show a tool, or change a grant.
- **It fails toward a person.** Unsure, a timeout, a provider error, an answer that cannot be read, no model, a spent
  check budget, or the deployment rule `AllowJudgedRules` off: before a tool a person is asked; before a reply the
  reply is held back with a sentence naming the rule.
- **It is not a guarantee**, and the product says so wherever it says it for pattern conditions. Tool results are
  untrusted text: they can sway only whether what a rule requires was done, so the worst they can do is keep a rule
  that only narrows from firing.

## Alternatives considered

- Judge with the space's ordinary chat model. Kept as a fallback for a server with no decision model (slice 9 of issue
  1018). Lost as the first choice: a chat model writes text that must be parsed and can be talked into an answer, while
  a decision model returns probabilities code can compare.
- Let a judgment allow what a grant asks about. Lost: that is issue 484, a separate decision; this one only narrows.
- No judged rules. Lost: the rules people state most often would stay unwritable.

## What would reopen it

Evidence that judged rules are relied on as a guarantee, or that a decision model can be made to raise what a bot may
do through anything it is sent.
