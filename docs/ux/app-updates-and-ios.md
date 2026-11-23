# App updates, and the iOS app

Issues: #513 (a server says which app versions it supports), #612 (the Android app says when a newer version is out), #617 (files, tool sign-in return and notifications wording on iOS), #857 (the sentences iOS shows when it asks for a permission).

Wording follows the [copy guide](copy-guide.md).

## 1. The app is too old for this server (#513)

**The screen's job:** tell the person this app is too old for this server, and how to update it.

It replaces every other screen until the app restarts or the person switches server. It is drawn by the session gate, so it needs no route.

```text
+--------------------------------------+
|              (Drover mark)           |
|  Update Drover                       |
|  This server needs Drover 0.6.0 or   |
|  newer. This is 0.5.2.               |
|                                      |
|        [ Get the update ]*           |
|        Use another server            |
+--------------------------------------+
```

An `EmptyState`-shaped screen on `EntryLayout`: `DroverMark`, heading, one sentence, one `Button` (`Primary`), one `Link`.

| Platform | Primary button | It does |
| --- | --- | --- |
| Android | `Get the update` | the flow of section 2. Until #612 has landed: opens the download page |
| Windows, macOS, Linux, an update is ready | `Update now` | installs it (the desktop updater) |
| Windows, macOS, Linux, no update ready | `Open the download page` | opens the project's releases page outside the app |
| iOS | `Open the download page` | the same. When the app comes from tester distribution, the page says where to update |

| Element | Words |
| --- | --- |
| Heading | `Update Drover` |
| Sentence | `This server needs Drover {minimum} or newer. This is {version}.` |
| Secondary | `Use another server` (the existing action) |
| The download page could not be opened | toast (`Error`): `The download page could not be opened. It is {address}.` The address is selectable text |

The browser build never shows this screen: the server ships it.

On the server's Status page one row is added: `Oldest supported apps`, listing each platform that has a minimum (`Android 0.6.0`), or `Any version`.

## 2. A newer version is out (#612)

No dialog and no badge. The notice is words, in two places.

```text
Account menu                          About
+---------------------------+         +--------------------------------------+
| ...                       |         |  Drover                              |
| About, new version out    |         |  Version 0.1.0 preview 12            |
+---------------------------+         |  A newer version is out: 0.1.0       |
                                      |  preview 13. This device has         |
                                      |  preview 12.                         |
                                      |        [ Open the download page ]    |
                                      |                                      |
                                      |  [x] Check for new versions          |
                                      |  Once a day this app asks GitHub     |
                                      |  which Drover version is newest. The |
                                      |  request carries no account or       |
                                      |  server details.                     |
                                      +--------------------------------------+
```

| State | Account menu item | About |
| --- | --- | --- |
| A newer version is known | `About, new version out` | `A newer version is out: {newest}. This device has {installed}.` and `Open the download page` (`Quiet`) |
| Up to date, or not checked yet | `About` | nothing added |
| Checks are off | `About` | `Version checks are off.` beside the switch |
| A build with no release tag | `About` | the switch is hidden. `This build does not check for new versions.` |
| The check failed | `About` | nothing. A failed check is not an error to the person |

The switch is a `Switch` with the label `Check for new versions` and the `About` text `Once a day this app asks GitHub which Drover version is newest. The request carries no account or server details.` It is on by default. The same setting governs the desktop updater.

`Open the download page` opens the release page for the tag the app parsed. No address from a response is ever opened or shown.

## 3. iOS (#617, #857)

The iOS app is the same client. These are the places where it differs.

### Saving and sharing a file

A file card's `Save` opens the system's document picker in export mode. `Share` opens the system's share sheet, anchored to the pressed button on a tablet.

| Outcome | Words |
| --- | --- |
| Saved | toast (`Success`): `Saved {file name}.` |
| The person cancelled | nothing |
| Failed | toast (`Error`): `{file name} could not be saved. {failure sentence}` |

### Files shared into Drover

Other apps offer `Copy to Drover` for pictures, PDFs, text and other files. The Share to Drover screen opens with the files, as on Android, with the same limits and the same words (`uploads.md`).

### Coming back from a tool sign-in

The app returns to Tool connections with the sign-in finished. An address the app does not expect is dropped with no message.

### Notifications

Until the relay exists (#858), the notifications control on iOS shows one sentence and no switch:

`Notifications are not available in the iOS app yet. Add Drover to your Home Screen from Safari to get them there.`

### Permission sentences

iOS shows one sentence when the app first asks for a permission. They are in `Info.plist`. The app asks only when the person presses the control that needs it, never at launch.

| Permission | When it is asked | Sentence |
| --- | --- | --- |
| Photo library (read) | The person chooses a picture to attach | `Drover reads the pictures you choose so it can attach them to a message.` |
| Camera | The person chooses to take a picture to attach | `Drover uses the camera when you take a picture to attach to a message.` |
| Microphone | Not asked. Voice is not built | no entry |
| Notifications | Not asked until #858 and its app side have landed | `Drover tells you when a bot finishes or is waiting for you.` |
| Local network | The person connects to a server on their own network | `Drover connects to a server on your network when you give it that address.` |

A permission the person refused: the control that needs it shows a `StatusNote` (`Info`) with `Drover may not use {the photo library}. You can allow it in Settings.` and the `Link` `Open Settings`.

### Icon and launch screen

The app icon is the Drover mark, the wedge head with its visor cut out, in `text` on `bg-0`, with no text and no border. The launch screen is `bg-0` with the mark centered at 64 pt, in both appearances. Nothing animates on launch.

## Layout at each width

- The update screen is one centered column, at most 440 px, with 16 px padding at 360 px.
- About is a `Dialog`. Its button stacks under the sentence below 560 px.

## Light and dark

Tokens only. The update screen and About use `text`, `text-2`, `bg-1`. The primary button is `ember`. No state here is shown by a dot or a color: "new version out" is text in the menu item. The launch screen has a dark and a light variant.

## Accessibility

- `Update Drover` is a level 1 heading and the primary button has focus when the screen opens.
- The account menu item's name is its visible text, so a screen reader hears `About, new version out`.
- The switch's description is its `About` text.
- On iOS, the share sheet and the document picker are the system's own and keep their own accessibility.
- With the largest text size on iOS the update screen and About scroll and lose nothing.

## Settings and defaults

| Element | Default | Setting (stored name) | Location | Label |
| --- | --- | --- | --- | --- |
| The daily check for a newer version, and the notice it leads to | On | `drover-update-checks` (absent means on, `off` means off) | About Drover, under Version. The Settings screen (#600) keeps this switch in About | `Check for new versions` |

The update screen of section 1 is not optional: the server refuses the app.

## Acceptance checks

- [ ] With a minimum above the app's version, every request leads to the update screen and no other screen can be reached except Connect through `Use another server`.
- [ ] The browser build is never refused.
- [ ] The update check sends no cookie, token, server address or device id (a test reads the request).
- [ ] No address from the release feed is opened or shown.
- [ ] Every sentence in this file appears exactly as written.
- [ ] On iOS, no permission is asked at launch (a screen check starts the app and finds no system prompt).
- [ ] Each permission sentence in the table is in `Info.plist`, and `Info.plist` has no usage sentence for a permission the table does not list.
