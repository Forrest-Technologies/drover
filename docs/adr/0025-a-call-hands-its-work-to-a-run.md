# 0025. A live call hands its work to a run

- Status: Proposed in its pull request
- Date: 2026-11-27
- Source: [issue 1063](https://github.com/Forrest-Technologies/drover/issues/1063), [03-run-lifecycle.md](../architecture/03-run-lifecycle.md#live-calls)

## Context

A live call was one speech-to-speech model answering by itself. It got the bot's instructions and nothing else: not
the bot's own model, no tools, no bot rules, no usage record. That was the honest limit of a model that hears and
answers in one step.

A second kind of speech model only holds the conversation. It listens and speaks at the same time, and hands every
request of substance to a backend the application runs. The question is what that backend is in Drover.

## Decision

- **The backend is the thread's bot, through an ordinary run.** What the person asked becomes their message in the
  thread, sent as them through `RunService.PostMessage`, exactly as a typed message is. The bot answers it in a run
  with its own model, instructions, rules, tools, grants, approvals, limits and usage. Nothing about that run is
  special, so nothing about a run had to be given a second way to happen.
- **The call is a viewer of that run.** It watches the run through `RunEventFeed`, with no transaction open while it
  waits, and gives the voice what the run came to. Ending the call does not end the run.
- **The run's reply is something to say, never an instruction.** It is given to the voice as text to pass on. Tool
  results and model output are untrusted text, and the voice has no tools of its own to be talked into using.
- **The voice is told who it speaks for and not the bot's instructions.** The bot's instructions stay with its runs,
  so the bot's model and rules answer for it. The voice answers greetings, thanks and requests to repeat or stop.
- **One interface for both kinds of model.** `IRealtimeVoice` says whether a model hands its work over, and
  `IRealtimeVoiceSession` carries the work and the report. The adapter for the second protocol lives in the same
  project as the first and is chosen by the model's name on a compatible connection.
- **A model that answers by itself is unchanged.** Its calls start no run and keep the limits they had.

## Consequences

- A call through a model that hands work over can act: a tool the bot is granted runs, and one that asks for approval
  waits for it in the conversation, as it would for a typed message. The voice says that it is waiting.
- The thread holds the request and the reply as the run's messages. What the voice says about them aloud is not
  written a second time. Small talk the voice answers itself is written with no run, when the next exchange begins;
  while a run is active it is said and not kept, because a thread has one writer.
- The answer is as fast as the run, not as fast as speech. The voice acknowledges and waits.
- Two models are paid for on such a call: the voice by its time, and the bot by its tokens as on any run.
