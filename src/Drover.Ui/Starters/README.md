# Starters

Designed pieces for screens that are not built yet. Each one belongs to an issue. They exist so that whoever picks the issue up starts from a settled design instead of a blank file: the layout, the wording, the states, the keyboard behavior, and the accessible names are already decided and already tested.

See them running in a development build at `/dev/starters`, in both themes.

## What a starter is

| Part | What it holds |
| --- | --- |
| A `*Words.cs` or other `.cs` file | The view models the component takes, and the logic that is design: sentences, state priority, ordering, layout, where the arrow keys go. Pure functions, tested. |
| A `.razor` file and its `.razor.css` | The component. It takes view models and callbacks and injects nothing, so it has no opinion about the API. It uses only the interface kit and the tokens. |
| A `*Samples.razor` file | Its section in the starter gallery (`StarterGallery.razor`, in this folder), with content labeled as sample content. |
| Tests in `tests/Drover.Ui.Tests/Starters/` | What the wording says, what is rendered, what is read out, what is handed back. |

A starter is not a mock-up. It compiles, it passes every interface rule the build enforces, and nothing in it is invented data.

## How to use one

1. Read your issue's "Start from" section. It names the starter files that are yours.
2. Open `/dev/starters` and use the pieces: resize to 360 px, switch themes, tab through them, turn on reduced motion.
3. **Move** your files out of `Starters/` to where the issue says the feature lives (`Chat/`, `Pages/...`, `Components/`), and change their namespace. Do not copy them, and do not reference `Drover.Ui.Starters` from product code: a test fails if you do.
4. Map the server's contracts to the starter's view models, or replace the view models with the contracts where they are the same shape. Wire the callbacks to `Drover.Client`.
5. Move the starter's tests with it, into the test folder for its new home. They stay green. A sentence in those tests is the design: change one only with a reason you state in the pull request.
6. Move its samples into the component gallery (`Pages/Dev/Gallery.razor`) if it became a kit component, or delete them.
7. Delete what is left: your rows in the table below and your samples in the `*Samples.razor` files (a sample file with nothing left in it goes too, with its line in `StarterGallery.razor`). When the last starter is gone, delete this whole folder (the gallery and `_Imports.razor` with it), the `Starters` test folder, and the `global using Drover.Ui.Starters;` line in the test project's global usings.

A starter is a starting point with a strong opinion. If what you find while building shows the design is wrong (the server cannot give a value it needs, a state exists that it does not cover, it does not hold up on a real phone), fix the design and say what you changed and why in the pull request. Do not ship it unchanged because it was handed to you, and do not replace it with something weaker because that was quicker.

## What the starters leave to you

- Everything on the server, and the contracts.
- Loading data, live updates, and error handling against the real API.
- Focus management across a whole screen, routing, and the address of each rail. That includes focus where a starter replaces the control a person just used: after Start and Stop on the voice button, after Change, Save, and Cancel on a note, after "Open the screen" on the takeover card, and after Approve or Deny on a waiting call, focus moves to the control that took its place.
- Screenshots and a manual pass on a real device. The gallery proves the pieces, not the screen.
- Anything the issue lists that has no starter. The table says what each starter covers.

## Which files belong to which issue

A test reads this table: every file in this folder must be listed, and every file listed must exist. Another test checks the copy in every `*Words.cs` file for hype and filler, the way the build checks markup.

| Issue | Files | Covers |
| --- | --- | --- |
| #149 | `PushWords.cs`, `PushControl.razor` | Notifications on this device: every state and its one action |
| #150 | `TakeoverPhase.cs`, `TakeoverCard.razor` | The bot's request for a person at its screen: asked, holding, answered |
| #152 | `HistoryWords.cs`, `HistoryList.razor`, `LineDiff.cs`, `DiffView.razor` | A bot's changes as sentences, values before and after, a line-by-line comparison |
| #153 | `SkillWords.cs`, `SkillFields.razor`, `SkillRow.razor` | Writing a skill, a skill in the library |
| #154 | `VoiceWords.cs`, `VoiceButton.razor`, `ReadAloudButton.razor` | Speaking a message, hearing a reply, every refusal |
| #157 | `RoutineWords.cs`, `RoutineRow.razor`, `RoutineWhenFields.razor` | A routine as a sentence, building a schedule without cron, next run and last outcome |
| #158 | `RoutineWords.cs` (`Proposal`) | The facts an approval card shows for a routine a bot proposes. The card itself is the existing `ToolCallCard` |
| #159 | `RoutineCallWords.cs`, `RoutineCallAddress.razor` | A routine's address, its secret shown once, today's calls |
| #160 | `MemoryWords.cs`, `MemoryPanel.razor` | What a bot remembers: every state, in-place editing, the conversation cards' wording |
| #162 | `DelegationWords.cs`, `DelegationCard.razor` | Work handed to another bot in the conversation, every refusal |
| #164 | `Timelines.cs`, `Timeline.razor`, `ArrowKeyZone.razor`, `ArrowKeyZone.razor.js` | A run laid out in lanes, keyboard movement, the list form |
| #166 | `SkillProposalCard.razor` | A skill a bot offers, edited and approved by a person. Uses `SkillFields` from #153 |
| #167 | `TreeLayout.cs`, `CrewCanvas.razor` | The crew as a tree: layout, connectors, the pulse, tree-view keys. Uses `ArrowKeyZone` from #164, and `AttentionNote` from #148, which is now in `Chat/` |
| #168 | `ActivityWords.cs`, `WaitingList.razor`, `ActivityRunRow.razor` | Answering what waits, several at once with every sentence listed, a run in the list |
| Samples | `ConversationSamples.razor`, `RoutineSamples.razor`, `RecordSamples.razor`, `BoldSamples.razor` | The starter gallery's sections. Each loses a section as its starter moves |
| Gallery | `StarterGallery.razor`, `_Imports.razor` | The page at `/dev/starters`, and the one `@using` the samples need. They go with the last starter |

Where a starter uses a piece that belongs to another issue, whichever issue is built first moves that piece, and the other finds it in its new home.
