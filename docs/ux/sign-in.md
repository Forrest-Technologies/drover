# Sign-in: an identity provider, and a second step

Issues: #524 (sign in through an identity provider), #860 (a second step with an authenticator app, and recovery codes).

Wording follows the [copy guide](copy-guide.md).

Password sign-in does not change. With no provider configured and no second step turned on, the Sign in screen is what it is today. "Example" in the wireframes is a sample provider name, set by the operator.

## 1. Sign in through an identity provider (#524)

### The Sign in screen

```text
+--------------------------------------+
|  Sign in                             |
|  Email     |______________________|  |
|  Password  |______________________|  |
|              [ Sign in ]*            |
|                                      |
|  ------------- or --------------     |
|       [ Continue with Example ]      |
|                                      |
|  Use a reset code                    |
|  This app is connected to            |
|  drover.example.org.                 |
|  Use another server                  |
+--------------------------------------+
```

- The password form stays first, and Enter still submits it.
- One `Button` (`Quiet`, full width) per provider the server lists, in the server's order, labeled `Continue with {provider}`. Up to eight.
- The divider is a 1 px `line` with the word `or` in `text-3`. It is drawn only when at least one provider exists.

### States and words

| State | Words |
| --- | --- |
| No provider configured | nothing is added |
| The browser is open for the provider (device apps) | in place of the form: `Finish signing in in your browser.` and `Cancel` (`Quiet`) |
| Came back signed in | the crew |
| Came back, and an account must be made | `Create an account`: `Field` `Your name` (filled from the provider when it sent one), the terms line when the server requires it, `Create account` (`Primary`). No password field |
| The provider did not finish | `StatusNote` (`Danger`): `Signing in did not finish. Try again.` |
| The email belongs to an account the provider did not confirm | `An account with this email already exists, and {provider} did not confirm the address is yours. Sign in with your password, then connect {provider} in Settings.` |
| No account matches and the server takes no new ones | `No account on this server matches. Ask the person who runs it.` |
| The account is disabled | the server's sentence |
| Cancelled | the form returns, with no note |

In the browser build the provider's page replaces the app in the same tab and returns to it. The plain page a provider error lands on reads `Signing in did not finish. Close this window and try again.`

### Connected sign-ins

A section on the Settings screen (#600), group `You`, titled `Ways you sign in`.

```text
|  Ways you sign in                                   |
|  Password                                  [ Change ]|
|  Example · connected 3 Nov             [ Disconnect ]|
|  Other                               [ Connect Other ]|
```

| Action | Words |
| --- | --- |
| Connect | the provider flow above. On return, toast (`Success`): `{provider} is connected.` |
| Disconnect | `Dialog`: title `Disconnect {provider}`, body `You will no longer be able to sign in with {provider}.`, buttons `Keep it` (focused), `Disconnect` (`Danger`). Then toast (`Info`): `{provider} is disconnected.` |
| Disconnect the only way to sign in | the server's sentence in the dialog: `This is your only way to sign in. Set a password first.` and the button `Set a password` |
| An action that needs a recent sign-in, on an account with no password | `Sign in again to do this.` with `Sign in again` |

## 2. A second step with an authenticator app (#860)

### Turning it on

Settings, group `You`, row `Second step at sign-in`, with the state as text (`Off` or `On since {date}`) and a `Button`: `Turn on` or `Manage`.

`Turn on` opens a `Dialog` with three steps. `SetupSteps` shows where the person is.

```text
Step 1                               Step 2                                Step 3
+------------------------------+     +--------------------------------+    +--------------------------------+
| Turn on a second step        |     | Add Drover to your app         |    | Save your recovery codes       |
| After your password, Drover  |     | Scan this with an authenticator|    | Each code works once if you    |
| will ask for a code from an  |     | app, or type the key.          |    | lose your authenticator. They  |
| authenticator app.           |     |                                |    | are shown only now.            |
|                              |     |        +------------+          |    |                                |
| Current password |________|  |     |        |  QR code   |          |    |  7K2M-9QXD    P4VN-T8RC        |
|                              |     |        +------------+          |    |  ...          ...              |
| (!) {problem}                |     | Key  ABCD EFGH IJKL ... [Copy] |    |                                |
| [ Cancel ]   [ Continue ]*   |     |                                |    | [ Copy codes ] [ Save as a file ]|
+------------------------------+     | Code from the app |______|     |    | [x] I have saved these codes   |
                                     | (!) {problem}                  |    |                [ Done ]*       |
                                     | [ Back ]     [ Turn on ]*      |    +--------------------------------+
                                     +--------------------------------+
```

| Step | Words |
| --- | --- |
| 1 heading | `Turn on a second step` |
| 1 body | `After your password, Drover will ask for a code from an authenticator app.` |
| 1 field | `Current password` |
| 2 heading | `Add Drover to your app` |
| 2 body | `Scan this with an authenticator app, or type the key.` |
| 2 key | label `Key`, the secret in the machine font in groups of four, with `Copy key` (`Quiet`) |
| 2 field | `Code from the app`, numeric, six digits |
| 2 primary | `Turn on` |
| 3 heading | `Save your recovery codes` |
| 3 body | `Each code works once if you lose your authenticator. They are shown only now.` |
| 3 actions | `Copy codes`, `Save as a file` |
| 3 switch | `I have saved these codes`. `Done` is disabled until it is on |

| Problem | Words |
| --- | --- |
| Wrong password (step 1) | `The current password is not right.` |
| Wrong code (step 2) | `That code is not right. Check the time on this device and try the newest code.` |
| Too many wrong codes | `Too many wrong codes. Wait a few minutes and try again.` |
| Closing the dialog at step 2 | nothing is turned on. Opening it again starts at step 1 with a new key |
| Closing the dialog at step 3 with the switch off | `Dialog`: title `Close without saving the codes`, body `The second step is on. Without these codes you cannot sign in if you lose your authenticator.`, buttons `Go back` (focused), `Close anyway` |
| Done | toast (`Success`): `The second step is on.` |

The QR code is drawn at 200 px on a white plate with a 16 px quiet zone in both themes, because a scanner needs dark on light. Its accessible name is `QR code for your authenticator app. The key below holds the same information.`

The recovery file is named `drover-recovery-codes.txt` and holds the server's host, the account's email, the date and the ten codes.

### Managing it

`Manage` opens a `Dialog` titled `Second step at sign-in`:

| Action | Flow | Words |
| --- | --- | --- |
| `Make new recovery codes` | asks for a code from the app, then shows step 3 again | on the confirm line: `The old codes stop working.` |
| `Turn off` (`Danger`) | asks for `Current password` and `Code from the app` | title `Turn off the second step`, body `Your password alone will sign you in.`, buttons `Keep it on` (focused), `Turn off`. Then toast (`Info`): `The second step is off.` |

Recovery codes left: the dialog shows `{n} recovery codes left.` When two or fewer are left the line is a `StatusNote` (`Info`): `{n} recovery codes left. Make new ones.`

### At sign-in

After a correct password, in place of the form:

```text
+--------------------------------------+
|  Enter your code                     |
|  Open your authenticator app and     |
|  enter the code for Drover.          |
|                                      |
|  Code  |______|                      |
|  (!) {problem}                       |
|            [ Continue ]*             |
|                                      |
|  Use a recovery code                 |
|  Sign in as someone else             |
+--------------------------------------+
```

| Element | Words |
| --- | --- |
| Heading | `Enter your code` |
| Body | `Open your authenticator app and enter the code for Drover.` |
| Field | `Code`, numeric, six digits, autofill `one-time-code`. Six digits submit by themselves |
| Link 1 | `Use a recovery code`. It swaps the field for `Recovery code` (machine font) and the body for `Enter one of the recovery codes you saved. Each works once.` The link becomes `Use the authenticator app` |
| Link 2 | `Sign in as someone else`, back to the password form |
| Wrong code | `That code is not right. Try the newest code.` The field is cleared and keeps focus |
| Too many wrong codes | `Too many wrong codes. Wait a few minutes and try again.` |
| The step ran out of time | the password form returns with `StatusNote` (`Info`): `That took too long. Sign in again.` |
| A recovery code was used | after sign-in, toast (`Warning`): `You used a recovery code. {n} are left.` |
| Unreachable | `{failure sentence}`, the field kept |

The email is kept across the step so `Sign in as someone else` returns to a filled form with an empty password.

## Layout at each width

- Entry screens: one column, at most 440 px, as in `first-run.md`.
- The three-step dialog is full width below 560 px, with its buttons stacked, the primary first.
- Recovery codes are two columns of five in the machine font, one column below 360 px of usable width.

## Light and dark

Tokens only, with one stated exception: the QR plate is white with black modules in both themes. Provider buttons are `Quiet` and carry no provider colors or logos. Screenshots of every state, dark and light, at 360 px.

## Accessibility

- Provider buttons are buttons named by their visible text.
- The code field's name is `Code`. It accepts paste and ignores spaces.
- The key and the recovery codes are selectable text and are read character by character by a screen reader (each group is its own text run).
- Focus on the code step starts in the field. After a wrong code it stays there.
- Step changes in the dialog move focus to the step's heading, which is announced.
- A problem note is assertive and tied to its field.
- Nothing here depends on seeing the QR code: the key is always offered.

## Settings and defaults

| Element | Default | Setting | Location | Label |
| --- | --- | --- | --- | --- |
| The second step at sign-in | Off | Kept on the server with the account, not on the device | Settings, You | Row `Second step at sign-in`, with `Turn on` or `Manage` |
| Sign-in through a provider | Shown when the operator configured one | Server configuration | Settings, You, `Ways you sign in` | `Connect {provider}`, `Disconnect` |

Nothing here is a display extra.

## Acceptance checks

- [ ] With no provider and no second step, the Sign in screen is unchanged.
- [ ] A provider's name is drawn as plain text. No logo, no address from the server, is drawn.
- [ ] After a correct password with the second step on, no screen but the code step can be reached until a code is accepted.
- [ ] The key and the recovery codes are not in memory after the dialog closes (a view model test), and never in a log or an address.
- [ ] `Done` cannot be pressed before the switch is on, and closing early asks once.
- [ ] Every sentence in this file appears exactly as written.
- [ ] All screens pass the shared screen checks at 360 px, dark and light, and are operable by keyboard alone.
