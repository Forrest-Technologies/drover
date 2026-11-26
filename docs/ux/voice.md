# Voice

Issues: #413 (a spoken message, read aloud, the Voice page), #500 slice 2 (the call surface).

Wording follows the [copy guide](copy-guide.md). Every string is in `VoiceWords` (`src/Drover.Presentation/Chat/VoiceWords.cs`), which the controls, the view models and their tests all read.

**The screen's jobs:** "Say a message instead of typing it, and listen to a reply." and "Talk with a bot out loud, as on a phone call."

## When a control exists

`GET /voice/options` is read once for the space (`SpaceVoice`) and again after a model connection changes. A control exists only when it can do something:

| Control | Exists when |
| --- | --- |
| Microphone button in the message field | The device can record and a model can listen |
| Speaker button under a reply | The device can play and a model can speak, on a reply the server has stored |
| Call button in the conversation's header | The device can record and play, and a model can listen or hold a live call |

On a device whose build has no sound yet (iOS) none of them exists, and the Voice page says so in one sentence.

## A spoken message

Beside Send, in the composer's frame. Each control is 44 px in every pointer mode, as Send is.

| State | What shows | Accessible name and tooltip | Read out once |
| --- | --- | --- | --- |
| Idle | The microphone icon | `Record a message` | nothing |
| Recording | The stop icon, the time recorded in the mono face (`0:07`), a close icon before it. Send is not shown | `Stop recording`, `Discard recording` | `Recording` |
| Writing it down | The microphone, disabled, `Writing it down` beside it, the close icon | `Record a message`, `Discard recording` | `Writing it down` |

The words go into the message field after what was already written, with the cursor in the field. Nothing is sent. The time is never read out.

A recording that came to nothing is said once, in a toast (`Error`), the way a file that could not be attached is:

| What happened | Sentence |
| --- | --- |
| The microphone was refused, in a browser | `Drover is not allowed to use the microphone. Allow it for this site in the browser's site settings, then try again.` |
| The same on Android | `Drover is not allowed to use the microphone. Allow it for Drover in Android's settings, under Apps and Permissions, then try again.` |
| The same on a desktop | `Drover is not allowed to use the microphone. Allow it for Drover in the system's privacy settings, then try again.` |
| No microphone | `This device has no microphone Drover can use. Connect one and try again.` |
| The microphone failed | `The microphone stopped working. Check it and try again.` |
| The recording is larger than the server takes | `This recording is too long. This server takes recordings up to {n} MB.` |
| The provider failed | The server's own sentence |
| Nothing was heard | `No words were heard in the recording. Record it again.` |

The failure is a toast and not a line under the field so that nothing above the composer moves: a line that grows the composer moves the conversation, and in a browser leaves the accessible boxes of its buttons where they were (see "Pitfalls found" in `18-native-client.md`).

## Read aloud

In the reply's action row, after Copy. `Read aloud` (speaker icon) becomes `Stop reading` (stop icon) while that reply plays, and goes back by itself when the sound has ended. One thing plays at a time in the whole app: another reply, or a sample of a voice, stops the first. A failure is a toast with the server's sentence, or `This device could not play the sound. Check its sound output and try again.`

## A live call

`Call` (phone icon) in the conversation's header. The surface stands between the header and the conversation, named `Call with {bot}` for assistive technology.

| State | Word | The bot's head |
| --- | --- | --- |
| Asked for, not yet connected | `Connecting` | thinking |
| The person may speak | `Listening` | at rest |
| The person has spoken, no answer yet | `Thinking` | thinking |
| The bot's voice is playing | `Speaking` | steady |
| Listening with the microphone off | `Muted` | at rest |
| Over | `Call ended` | at rest |

The word is a level two heading and is read out once when it changes. Under it, the time connected in the mono face, never read out. Then three icon buttons: `Mute` (becomes `Unmute`, pressed), `Show words` (becomes `Hide words`, pressed) and `Hang up` in the danger color. In the corner, `Shrink the call`: the surface becomes one row (head at 32 px, word, time, Mute, Hang up, `Expand the call`) so the conversation stays in use. A call that is over, or did not start, has `Close` in the corner instead.

`Show words` shows the exchange under way: `You said: {words}` and `{bot} said: {words}`. Everything said earlier is in the conversation beneath, as ordinary messages the server wrote.

Nothing on the surface moves except the head's own visor while the bot thinks, which is one of the four live states.

A call that does not start shows the reason where the word would be: the server's sentence (`Calls need a live voice model or a speech model.`, `The bot is still working in this conversation. Call when it has finished.`), with the link `Connect one` to Models when the space has no live model, or the microphone's sentence from the table above. A call that ends by itself says why:

| Why | Sentence |
| --- | --- |
| The server's limit | `The call reached this server's limit of {n} minutes. Call again to go on.` |
| The provider ended it | `The speech provider ended the call. Call again to go on.` |
| The connection was lost | `The call was disconnected. Call again to go on.` |

Talking over the bot: when the server says the person started speaking, the bot's voice stops in the same turn and the server is told how many milliseconds of the answer had been played. On a device that cancels no echo (a desktop), the microphone is ignored for 600 ms after the bot's voice starts.

## The Voice page

Account menu, after Notifications. Address `/voice`. One card per row:

| Row | Picker | Under it |
| --- | --- | --- |
| `Listens with` | `Automatic`, then each model as `{connection}: {model}`, with `(On your network)` after a model at a private address | What it uses now |
| `Speaks with` | the same | What it uses now; `Voice` picker; `Play a sample` (becomes `Stop the sample`) |
| `Live calls with` | the same | What it uses now; `Voice` picker |

"What it uses now" reads `Automatic: {model}, {connection}` until someone picks, then `{model}, {connection}`. The server's own speech service reads `This server`; it is what Automatic falls back to and is not a pick. A row nothing can fill reads `No connected model can do this.`

A member sees the rows disabled and `An owner or admin of the space can change these.` Anyone may play a sample.

`Advanced`, closed: `Longest recording: {n} MB`, `Longest text read aloud: {n} characters`, `Longest call: {n} minutes` or `No limit`, and for an owner or admin the link `Mark a model for speech on Models`.

| State | What shows |
| --- | --- |
| Loading | `Loading the voice settings.` |
| Failed | `Drover could not load the voice settings`, the cause, `Try again` |
| Nothing connected | `Voice needs a speech model.` and the link `Connect one` |
| A save refused | The server's sentence in a `StatusNote` (`Danger`); the row goes back |
| No sound in this build | `Voice is not available in this app on this device yet. These settings still apply to the space.` above the rows |

## Settings and defaults

Voice adds no setting of a person's own. The three rows are the space's, default Automatic, stored by the server in `spaces.settings.voice`.

## Not built here

- A sample of a live model's voice: the speech route reads with the model that speaks, which does not have a live model's voices.
- A person's own choice of voice (#412 follow-up), calls by turns and the call in the record (#500 slices 3 and 4).
