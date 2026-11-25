# 18. The native client

One codebase that draws Drover's interface natively on the web, on Android, and on desktop. It replaced the Razor component library and its two hosts. The decision is [ADR 0015](../adr/0015-one-native-client.md). This section is the working manual: a person or an agent changing a screen should need nothing else.

The Blazor web client (`Drover.Ui`, `Drover.Web`) and the MAUI Android shell (`Drover.App`) were retired on 2026-11-20 by the owner's decision, ahead of the verification the retirement gate below asked for. That verification stays open against this client. The server serves this client's browser build at `/`. What the retired projects still had that this client, the server, or the shared folders needed was moved, not deleted: the tests of shared and framework-free code and the recorded event logs to `tests/Drover.Presentation.Tests`, the design tokens' specification to `Theme/tokens.css`, and the shared browser, transport, and Android files stay in `src/Shared`.

## What it is

- `src/Drover.Native` is one Uno Platform project (Uno.Sdk 7.0.22, Apache-2.0). Only the open source platform is used. The design tools and the app inspection server that the SDK offers in development builds are switched off in the project file (`UnoDisableHotDesign`, `UnoDisableMCPSupport`).
- Every target is drawn by the Skia renderer, so one set of controls looks the same everywhere. Nothing is an HTML element: in a browser the app is a canvas, with a hidden tree of elements beside it for assistive technology.
- The interface logic that does not depend on a UI framework lives in `src/Drover.Presentation`, a plain class library. Words, drafts, tool cards, timelines, bot looks, icon shapes, the markdown node model, session state, and the toast service moved there unchanged from the web client, under the `Drover.Ui.*` namespaces they always had. View models and the router are new code there, under `Drover.Presentation.*`.

## Projects

| Project | Kind | Holds |
| --- | --- | --- |
| `src/Drover.Presentation` | `net11.0` library, in `Drover.slnx` | The moved framework-free code (`Drover.Ui.*`), view models (`Drover.Presentation.ViewModels`), the router (`Drover.Presentation.Routing`), the readers for drawings kept as data (`Drover.Presentation.Drawing`), `IDeviceSettings` |
| `src/Drover.Native` | Uno single project, in `Drover.Native.slnx` | The app: tokens, controls, layouts, shell, pages, platform code |
| `src/Shared/Transport` | Source files, linked | `AntiforgeryHandler` (compiled into the browser build); `AppRequests`, `NativeSession`, `NativeSessionTokens`, `ServerAddress` (compiled into the device builds) |
| `src/Shared/Web` | Browser files, linked | `boot.js`, `cookies.js`, `push.js`, `push-worker.js`, `service-worker.published.js`, the icons: copied into the browser build, which the server serves at the root |
| `src/Shared/Android` | Source files, linked | UnifiedPush registration, transport, receivers and delivery service, notifications, shares in (`AndroidShares`), files out (`AndroidFileDelivery`, `DocumentDestination`), and `IncomingIntents`, written for the retired Android shell; compiled into the Android build |
| `tests/Drover.Presentation.Tests` | xunit, in `Drover.slnx` | Router, view models, wording, token parity and contrast, the interface rules for native source, and the shared web files (service worker, the retiring `/native/` worker, the HTML preview) in a real Chromium |

`Drover.Native` has its own solution file because it needs workloads the main solution's build does not have (`wasm-tools`, Android). The [Android preview workflow](../android-preview.md) builds and signs the Uno Android head on protected preview tags.

```
src/Drover.Native/
  Drover.Native.csproj     targets, switches, linked transport
  App.xaml(.cs)            resources, start, restart
  Theme/                   Tokens.xaml, Styles.xaml, and their readers (Tokens, Styles, Motion, Targets, Pill, Themed)
  Controls/                one class per component
  Layouts/                 EntryLayout, WorkLayout, PhoneNav, NavLink
  Session/                 SessionGate, AccountMenu, ServerChoice
  Shell/                   AppServices, Screens, ShellView, Screen, PendingScreen, Splash
  Pages/                   one class per screen, in the folders the web client uses
  Device/                  compiled for desktop and phones only: server choice, settings, the phone credential store
  Platforms/WebAssembly/   compiled for the browser only: storage, history, cookies, the eval table
  Platforms/Desktop/       Program.cs, files out, the desktop credential store
  Platforms/Android/       application, activity, manifest
  Platforms/iOS/           entry point, manifest, keychain entitlement, safe areas
  Assets/Fonts/            Inter and JetBrains Mono, with their licenses
```

## Platform matrix

| Target | Framework | State |
| --- | --- | --- |
| Web | `net11.0-browserwasm` | Builds, publishes, and runs in Chromium under the server's security headers |
| Desktop: Linux, macOS, Windows | `net11.0-desktop` | Linux Release builds and renders its first screen under virtual X11 with the bundled font fallback. Linux, Windows and macOS start in CI and pass the [screen checks](#screen-checks) |
| Android | `net11.0-android` | Release build and signed development APK verified with SDK 37 and Java 21. The API 35 emulator checks text editing and passes the [screen checks](#screen-checks). |
| iOS | `net11.0-ios` | Hosted macOS builds the simulator app and runs the shared screen checks. The second launch verifies that the saved sign-in is restored from the system credential store. |

Uno 7 removed Mac Catalyst. macOS uses the desktop head. This is confirmed by the upstream [migration guide](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/doc/articles/migrating-to-uno-7/project-setup.md#mac-catalyst-is-removed).

## Commands

```bash
# Build one target (-warnaserror holds the same rules as the rest of Drover)
dotnet build src/Drover.Native -f net11.0-desktop -p:DroverNativeTargetFrameworks=net11.0-desktop -warnaserror
dotnet build src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm -warnaserror     # needs: dotnet workload install wasm-tools
dotnet build src/Drover.Native -f net11.0-android -p:DroverNativeTargetFrameworks=net11.0-android -warnaserror         # needs the Android SDK
dotnet build src/Drover.Native -f net11.0-ios -p:DroverNativeTargetFrameworks=net11.0-ios -p:DroverEnableApple=true -p:RuntimeIdentifier=iossimulator-arm64 -p:CodesignKey=- -warnaserror

# Run on desktop
dotnet run --project src/Drover.Native -f net11.0-desktop -p:DroverNativeTargetFrameworks=net11.0-desktop

# Run in a browser, development build, at http://127.0.0.1:5199/ (the gallery is at /dev/gallery)
ASPNETCORE_URLS=http://127.0.0.1:5199 dotnet run --project src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm

# Publish the browser build. Drover's server serves it at the root (NativeClient:Path, or the server's web root).
dotnet publish src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm -c Release -o <folder>
# The same, with the gallery in it, for the parity bench
dotnet publish src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm -c Release -p:DroverShowGallery=true -o <folder>

# Tests for everything that is not drawing
dotnet test --project tests/Drover.Presentation.Tests
```

Android URL fields host an Android `EditText` in the shared field frame. The editor keeps the selection and composing
text with Android's input connection. Sign-in and account fields with an `Autofill` name host one too, with Android's
autofill hint (`username`, `password`, `newPassword`) and `ImportantForAutofill.Yes`: the platform's own text box gives
the autofill service no structure to read. The native-client workflow exercises the address field on an API 35 emulator
with a test keyboard that composes, commits, and deletes text and with hardware key events, and checks in a gated build
that the hosted `EditText` views carry those hints and `ImportantForAutofill.Yes` (filling with a real Android password
manager was not tried). Run the same check with
`bash scripts/check-native-android-text-editing.sh` on a fresh emulator with the Drover development APK installed.

On Android, the edge-to-edge window draws its background behind the system bars. The shell and custom popup
panels use Uno's visible bounds to keep controls clear of the bars and display cutouts. The emulator check
compares the first and last rows of the connection screen's yard with Android's system-bar bounds at 360 dp
and captures the screen in both themes.

Taking a screenshot of the browser build: serve the published `wwwroot` at the root with any static host that falls back to `index.html`, open it in Playwright, wait until the element with class `uno-loader` is gone, then capture. The app is one canvas, so Playwright's full-page capture gives one viewport: use a tall viewport, or send wheel events of about 1300 pixels and capture again. A development build takes about fifteen seconds to start in headless Chromium; a published build about three.

Taking a screenshot of the desktop build on a machine with no display: `xvfb-run -a --server-args="-screen 0 1280x800x24"` around a script that starts `bin/Debug/net11.0-desktop/Drover.Native.dll`, waits, and runs `import -window root out.png`. A Debug build started with `DROVER_DEV_GALLERY` set shows the gallery's conversation samples with no server (`Pages/Dev/DevGallery.cs`): some words of a section's title show the sections that hold them, and `pane` shows one sample conversation over its composer and status line, as wide as `DROVER_DEV_WIDTH` says, light or black with `DROVER_DEV_MODE=light` or `black`, thinking or with a turn of two messages with `DROVER_DEV_STATE=thinking` or `turn`. `DROVER_DEV_THEME` names the theme (`front-office`, `night-yard`). None of it is in a Release build.

The appearance choices (theme, mode, accent) are a dialog the shell owns, not the account menu: a choice of theme, accent or the black mode makes the open screen again, and the dialog stays open over it. Whether it is open is `ThemeModel.AppearanceOpen`, which the account menu's "Appearance" item sets; the options are `AppearanceViewModel`. After such a choice `ShellView` makes the dialog again from the new values, opens it over the old one with no fade before the old one goes, and hands the option that had the focus to the new body (`AppearanceChoices.FocusHeld`); when it closes, the focus goes to the account menu's button on the screen now showing, or to the screen, with the ring only when the keyboard closed it (`Dialog.ClosedWith`). A signed-in person's choice is also saved through `/api/account/appearance`. Signing in loads it silently. A missing record leaves the device choice in place. An offline or failed save keeps the local choice, queues the latest value for reconnect and shows one quiet retry sentence in the dialog. Signed-out choices use only `drover.theme`. A development build started with `DROVER_DEV_APPEARANCE` set opens the dialog over the first screen and presses the buttons named in it, separated by commas, two seconds apart, in a window as wide as `DROVER_DEV_WIDTH` says (`Shell/AppearanceRehearsal.cs`, not in a product build), for a picture with no pointer. `DROVER_DEV_GALLERY=splash` shows the splash alone.

The pictures taken for this work are in `docs/screenshots/native-foundation/`, native beside web, at 1280 and 360 pixels, dark and light.

## Screen checks

Every pull request starts the app on Android, iOS, Linux, Windows and macOS and runs one shared set of screen checks:
the app opens on the connect screen, connects to a test server, signs in, opens the conversation with the test
bot, sends a message and reads the bot's reply, and goes back to the crew. Every target runs them at phone width,
so each shows the same controls.

- **In the app.** The checks are in `tests/Drover.Native.UITests/App/`. They are compiled only into a build made
  for them (`-p:DroverUiChecks=true`, desktop and Android), never into a product build: `App.StartChecks` and
  `AppServices.AddChecks` are partial methods with no body anywhere else. A check finds each control by its
  automation id (`AutomationProperties.AutomationId`) and presses it through its automation peer, so renaming or
  dropping an id fails the step that needs it. The ids in use are `ConnectAddress`, `ConnectSubmit`,
  `SignInEmail`, `SignInPassword`, `SignInSubmit`, `CrewRow`, `ComposerField`, `ComposerSend`, `MessageList`
  and `TalkBack`. Each step reports one line, and the last line says whether all of them passed.
- **The harness.** `tests/Drover.Native.UITests` runs the real server in process on a loopback HTTPS port, with a
  certificate made for the run, a scripted model, and PostgreSQL from `DROVER_TEST_POSTGRES` or a container. It
  waits until `/health/ready` answers over the same HTTPS connection the app will use (a bounded wait that fails with
  the end of the server log), makes an account and one bot through the API, starts the app with the server's
  address, the account, and the certificate's SHA-256 hash, and exits with 0 only when the app reports that every
  check passed. The app's connect step gives the server 30 s and tries a refused connection again after 0.5, 1 and 2
  s, logging each attempt; a server that never answers still fails with the screen's own problem text, and a failed
  iOS run prints the app's log. A step waits up to 60 s for its screen and returns as soon as it is there, because a hosted runner shared with the simulator has taken 17 s to sign in. The app trusts
  that one certificate by its hash and nothing else that a product build would refuse. On Android the server's
  port is reversed onto the device and the settings travel in the start intent's extras; the report comes back
  through the device log under the tag `DroverUiCheck`.
- **Shipping build guard.** After building the shipping Android, iOS, and desktop targets without `-p:DroverUiChecks=true`,
  `.github/workflows/native.yml` scans the compiled `Drover.Native.dll` for UTF-8 metadata names that only exist in
  the checks build (`ScreenChecks`, `CheckSettings`, `TryGetIsolatedText`) and fails if any match.
- **iOS restart.** The simulator runs the same screen flow, closes the app, and opens the installed app again.
  The second run passes only when the server choice and sign-in from the first run restore and the crew appears.
- **Switching the theme.** The last step applies a sample theme made for the checks (`SampleTheme`, Night Yard with a different value in each group, never offered to a person) and reads one value from each group off the open screen: the type scale's base size, the spacing step, the size and language of a frame's corner, the list row, bloom, the duration scale, that live states fade, the yard texture where it is drawn, the character line weight and an accent color. It then switches back and requires exactly what was there before.
- **Adding a check.** Add a step to `ScreenChecks`, give each control it uses an automation id in the app, and
  keep the step working at phone width on every target.

```bash
# Desktop: build the app with the checks, then run them (Linux needs a display: xvfb-run -a in front)
dotnet build src/Drover.Native -f net11.0-desktop -p:DroverNativeTargetFrameworks=net11.0-desktop -p:DroverUiChecks=true -o <folder>
dotnet run --project tests/Drover.Native.UITests -- desktop <folder>/Drover.Native.dll

# Android: install a build with the checks on the one device adb sees, then run them
dotnet build src/Drover.Native -f net11.0-android -p:DroverNativeTargetFrameworks=net11.0-android -p:DroverUiChecks=true -o <folder>
adb install -r <folder>/*-Signed.apk
dotnet run --project tests/Drover.Native.UITests -- android
```

## Android proof

Two things keep the Android basics from breaking again without anyone seeing it. Neither is added to the Android
preview build or to the checks every pull request runs.

**Fast tests, in the normal test jobs.** They need no device and take seconds.

- `tests/Drover.Server.Tests/AndroidPickedFileUploadTests.cs` sends a picture the way the app sends one picked on
  Android: the picker reports no size, the stream has no length and cannot seek, the app measures it by buffering
  (`UploadFileWithUnverifiedSize`) and sends it with the antiforgery token, to the real upload route. It reads the
  picture back from the server and compares its hash, and it covers a small file, a file at the limit, a file past
  the limit (the server answers 413 with a sentence that names the limit, which is what the app turns into its words),
  no token, and a cancelled upload. These guard the path that works today. They do not cover a server that cannot
  write its file store or the words the chip shows; those belong to the #799 fix, which carries its own failing-before
  tests.
- The sandbox pointer has its own server test, `SandboxPointerDragApiTests`, which lives with the pointer input
  route (#797, in the pull request that stacks on it) and needs that route to pass. It sends a drag to the real route as the app does (a press, moves, a release, and the whole drag as one
  request) to a sandbox provider that records the commands it ran. `tests/Shared/PointerEvents.cs` reads those commands
  as a pointer's events, so a test asserts a down, one or more moves and an up, with no button left held. The emulator
  proof below uses the same reader.

**A manual proof on an emulator.** The workflow `.github/workflows/android-proof.yml` runs only when someone starts it
(Actions, "Android proof", "Run workflow"): never on a push, a pull request or a tag. It runs on a GitHub-hosted
runner. It proves the branch or tag it is started on and takes no input: choose it under "Use workflow from", or run
`gh workflow run android-proof.yml --ref <branch>`. The branch must contain the workflow and the harness. GitHub's
documentation says a manual workflow is listed and dispatched from the default branch (`main`). Tried for this
workflow, the dispatch was accepted from a branch while the file was on no default branch: it works once GitHub
knows the workflow, which it learns from a run of its file (here, runs made from the branch that added it with a
temporary push trigger). A workflow GitHub has not run yet is not expected to be found (not tried), and the Actions tab lists it only from
`main`. So the dependable start for a new proof workflow is its file on `main`, for example a small stub there that
takes a `ref` input and runs the proof for it. Each of the pull requests that fix the
upload or the touch drag links the run that proves it; without that run the fix is not called fixed. The drag
step needs the pointer input route (#797) and the touch model on the picture (#798) in the build, so on a base that
has neither it is expected to fail while the other three pass.

The run builds the app made for the screen checks (the preview app refuses a test server's certificate, as it must),
starts an emulator (`system-images;android-35;default;x86_64`) and waits for `sys.boot_completed`, starts the real
server of the screen checks in the harness with a sandbox provider that records its input, waits for
`/health/ready`, and runs `dotnet run --project tests/Drover.Native.UITests -- android-proof <folder>`. Every wait has
a bound and says what it saw when the bound passes. Four things print one `PROOF PASS` or `PROOF FAIL` line each:

1. **Chat.** The app sends a message and the bot's reply arrives.
2. **Upload.** The app presses attach. The harness chooses `drover-proof.png` in the system's file picker, so the
   file comes through the same path as a person's screenshot. The app waits for its chip to say it is uploaded, sends
   it, and sees it in the conversation. The harness then reads the stored file from the server and compares its hash
   with the file it sent.
3. **Sandbox screen.** The app opens the bot's sandbox screen and waits for its view-only mode line. The sandbox must
   have been asked for its picture.
4. **Touch drag.** The app turns on "Send desktop input". The harness finds the picture in the device's accessibility
   tree and runs `adb shell input draganddrop` over it (a hold, then moves, then a lift: real touch events). The
   recording sandbox must have been told a button down, one or more moves and a button up, with nothing left held.

The artifact `android-proof` holds `proof-summary.md` (the four lines), `harness-and-server.log` (the harness and the
test server), `app-report.txt` (the app's own steps), `logcat-tail.txt`, `final-screen.png`, `the-last-screen.xml`
(what the harness last read from the device), `proof.mp4` (a recording of the run), `drover-proof.png` (the file it
sent) and `emulator.log`.

To add a step, add the app's part to the proof steps in `ScreenChecks` (`tests/Drover.Native.UITests/App/`), named
`proof <number> <what>`, add the harness's part to `AndroidProof` (what only the device or the server can say), and give
every control the step uses an automation id. The fast tests and the proof are written to fail on a build that has the
bug, so a step is added only with a test that failed before its fix.

The emulator is never started on the Mac mini or by hand on a developer machine: the proof is a run of this workflow.

## Tokens

`Theme/Tokens.xaml` is the only XAML file that holds raw values. It carries every token of `Theme/tokens.css`, and `TokenParityTests` fails when the two files disagree. The CSS file is the specification, as ADR 0015 decided: a new token is added there first. It moved there from the retired web client and is not part of the app.

| In `tokens.css` | In `Tokens.xaml` | Type |
| --- | --- | --- |
| `--dv-bg-0`, any color | `DvBg0`, and the brush `DvBg0Brush` | `Color`, `SolidColorBrush`, in the theme dictionaries |
| `--dv-space-4`, any `px` | `DvSpace4` | `x:Double`, device-independent pixels |
| `--dv-text-14`, any `rem` | `DvText14` | `x:Double`, the `rem` times 16 |
| `--dv-dur-2`, any `ms` | `DvDur2` | `x:Double`, milliseconds |
| `--dv-weight-label` | `DvWeightLabel` | `FontWeight` |
| `--dv-font-ui`, `--dv-font-mono` | `DvFontUi`, `DvFontMono` | `FontFamily` |
| `--dv-ease` | `DvEase` | `x:String`, the key spline `0.2,0.8 0.2,1` |
| `--dv-scrim` | `DvScrim` | `Color`: the first surface at 72 percent |
| `--dv-measure` (65ch) | `DvMeasure` | `x:Double` 572: sixty-five zeros of Inter at 14 |
| `--dv-tracking` (0.01em) | `DvTracking` | `x:Int32` 10, the unit `CharacterSpacing` takes |
| `--dv-bloom-ion`, `--dv-bloom-violet` | `DvBloomIon`, `DvBloomViolet` | `x:Double` 8, the blur; the color is the accent |
| `--dv-composer-lines`, `--dv-output-lines` | the same names | `x:Double`, a count of lines |
| `--dv-travel-width` (25%) | `DvTravelWidth` | `x:Double` 0.25 |
| `--dv-layer-menu`, `--dv-layer-toast` | the same names | `x:Int32` |

The rule for a name: drop `--`, capitalize each part, join. `--dv-radius-control` is `DvRadiusControl`. One value exists only natively: `DvYardDot`, the line color at 70 percent, which the web computes in a style rule.

The theme dictionaries are `Default` (dark, the primary theme), `Dark` (the same values, so a subtree can be shown dark inside a light app), and `Light`.

`Tokens.xaml` holds Night Yard's values and is what the app starts from. The theme in use writes over them (see [Theme](#theme)): a color in place, so a brush a control holds repaints; a number, a face or a weight under the same name. Names that only a theme sets, because Night Yard has them as fixed values, are written the same way and have no entry in this file: `DvRowPointer`, `DvRowTouch` (the smallest list row), `DvFontReading`, `DvFontNumerals`, `DvFontHeading`, `DvFontHeadingLarge`, `DvList` (the surface lists rest on by the number of surface levels, resolved but not yet read: the crew list still rests on `DvBg1`, as Night Yard's does today), `DvBubble` (the surface of a person's message: the card surface where replies run as a document, a tint of `ember` where both sides speak in bubbles), `DvLeadBody`, `DvLeadMono`. The six steps of the type scale keep the names of the pixel sizes they have in Night Yard: `DvText12` is the first step of any theme and `DvText14` its base. `DvLine12`, `DvLine13`, `DvLine14`, `DvLineH1`, `DvLineH2` and `DvLineH3` are the size of a step times its leading, worked out from the theme.

`Theme/Styles.xaml` holds what is derived from tokens: the text styles, the layout breakpoints (`DvBreakPhone` 560, `DvBreakEntry` 600, `DvBreakSheet` 640, which the web writes inside media queries), and the control templates. It is the only other file where a number may appear. A style cannot hold a number that changes, so applying a theme loads this file again (`Styles.Reload`).

Text styles: `DvTextBody`, `DvTextParagraph` (kept to the measure), `DvTextSecondary`, `DvTextLabel`, `DvTextCaption`, `DvTextH1`, `DvTextH2`, `DvTextH3`, `DvTextMono`, `DvTextCode`.

Using a token:

```xml
<TextBlock Style="{StaticResource DvTextSecondary}" Text="..." />
<StackPanel Spacing="{StaticResource DvSpace4}" />
<Border Background="{ThemeResource DvBg2Brush}" BorderBrush="{ThemeResource DvLineBrush}" />
```

```csharp
Tokens.Number("DvSpace4")           // a length, a size, or milliseconds
Tokens.Brush(this, "DvBg2")         // the brush in the theme this element is shown in
Tokens.Color(this, "DvIon")
Styles.Text("Sign in", "DvTextH2")  // a TextBlock in a text style
```

A control that sets colors in code repaints when the theme changes: call `this.WhenShownOrRethemed(Paint)` in its constructor. A color written in XAML with `ThemeResource` follows the theme by itself.

## Theme

`ThemeModel` (in `Drover.Presentation`) holds the choice: a `ThemeChoice`, which is a theme id, a mode (`system`, `dark`, `light` or `black`) and an accent ([ADR 0021](../adr/0021-front-office-black-and-accents.md)). The app is Front Office, dark, in the theme's own color, until a person picks otherwise or asks it to follow the device. The choice is kept through `IDeviceSettings` under the name `drover.theme` as one small JSON object, `{"themeId":"front-office","mode":"dark","accent":"theme","version":2}`. In a browser that is the local storage entry the retired web client wrote, so a person's mode carried over; elsewhere it is the platform's settings store. A value written before a person could choose a theme, the one word `dark`, `light` or `system` or the object without a version, keeps its mode and takes the default theme: the theme it named was the only one there was. A value that is none of these reads as a device that has not chosen. A local theme id the app does not have draws as the default and remains in the device cache. A server choice whose theme or accent this app does not have is cached with the default theme or theme accent. `ShellView` applies the mode by setting `RequestedTheme` on the root; a window has no black mode, so black is the dark mode with the theme's black colors written into the dark dictionaries (`ThemeTokens.Apply(theme, black)`).

The black mode and the accents are worked out, never stored in a theme file. `ThemeFile.In(ColorMode.Black)` is the dark mode with a black first surface and each surface above it taking the color of the one below. `Accents.For(theme)` lists the accents a theme offers, its own color first, and `theme.WithAccent(id)` is the theme with `ember` turned to that hue at the theme's own chroma, as close to its lightness as still reads on every surface, with `on-accent` kept where it still reads. An accent is offered only when the theme then passes every gate, so one whose hue lies within 45 degrees of the theme's `ion` or `violet` is not on the list, and neither is one within 20 degrees of the theme's own color. The same theme and accent always give the same object, which is how `ShellView` tells that the look in use changed.

A theme is data ([ADR 0020](../adr/0020-a-family-of-themes.md)): one JSON file per theme in `src/Drover.Presentation/Theme/Themes`, in the closed format of [09-design-direction.md](09-design-direction.md#theme-files). `ThemeFileReader` reads a file and refuses it with every problem listed when a key is missing or unknown, a type or bound is wrong, or a color is not `#RRGGBB`. These checks are small and local; the shared checker of issue 778 replaces them, and the accessibility gates (`ThemeGates`, listed in [09-design-direction.md](09-design-direction.md#the-gates)) add the contrast, touch target and invariant rules, and run over every theme file in the build. `ThemeResolution.Resolve(mode)` turns a file into a `ThemeTokenSet`: the colors and numbers named as the token file names them, with everything derived already worked out (the scrim, the yard color, the spacing steps, the durations, the measure). `BuiltInThemes` holds the themes that ship in the app, each embedded from its file: Front Office, the default, and Night Yard, whose values are the ones `Tokens.xaml` holds. The app applies the default before its first frame, so the token file's values are never what a person sees unless they chose Night Yard. `TokenParityTests` holds `tokens.css` and `Tokens.xaml` to each other, and `ThemeResolutionTests` holds Night Yard's file to both, so Night Yard cannot drift.

`ThemeTokens.Apply` (native) puts a theme in the resources, in its dark (or black) and its light mode. Colors change in place in the `Default`, `Dark` and `Light` dictionaries and in the brushes beside them, so everything that holds a brush repaints at once, in `ThemeResource` markup and in code. Numbers, faces, weights and the derived line heights are written under their token names, and `Styles.Reload` makes the styles again from them. A control that reads a number when it is built (`Tokens.Number`) keeps what it read, so `ShellView` makes the open screen again when a theme or an accent other than the one in use is chosen, or the black mode is entered or left: every group changes without a restart. A change between dark, light and following the device is only the window's requested theme and rebuilds nothing. The shape class is `Facet`: faceted cuts two corners (control and frame cut), square and rounded round all four (control and card radius, a rounded control becoming a pill at its smallest height), ticked keeps the rectangle and marks the same two corners with ticks of the cut's length, crossing out past the corner as registration marks where the theme says so. `Yard` draws the texture, strength and step of the surfaces group. The characters group reaches `AvatarCanvas` as the head and visor colors, the facet highlight (`DvCharacterFacet`), the part line weight (`DvCharacterLine`) and the visor style: an outlined visor draws its edge, lit while the bot works, so it reads in a theme without bloom. A theme that fades its live states (`Motion.Fades`) shows thinking and a running tool as one fade up to a still state and never loops them; `Motion.Loops` is the loop, and reduced motion is neither. The state is said in words in every case. Icons draw at the theme's icon stroke (`DvIconStroke`), the travel line at `DvTravelLine`, conversation text in `DvFontReading` and headings in `DvFontHeading`.

A screen made again starts from its address, so it would drop what a person typed and had not saved or sent. A theme is chosen in the Appearance dialog, which opens over any screen, so `ShellView` does not make a screen again while it holds such work: a field a person can write in that has text in it, a composer with words or files not sent, or a screen that set `Navigator.HoldsWork` and answers true (`AppearanceViewModel.MakesScreenAgain`). The colors still change at once, because they change in place. The shapes, sizes and type of that screen wait until it is left, the next screen is made from the new values as every screen is, and the dialog says so in one sentence while that is the case. A field that shows a saved value counts as typed, which only makes that screen wait. Nothing is asked: the leave prompt is for leaving.

## Controls

Every component of the retired web client has a control of the same name in `src/Drover.Native/Controls`, with the same parameters as dependency properties. Where a name could not be kept, the table says why.

| Control | Notes |
| --- | --- |
| `ArrowKeyZone` | Mark an item with `ArrowKeyZone.IsItem="True"`. Arrow keys, Home, End move among items |
| `Avatar` | `BotName` (the web's `Name`; every element already has a `Name`), `Hue`, `Seed`, `Size`, `ImageUrl`, `State`. Drawn from `BotLook` |
| `Badge` | `Text`, `Kind`, `Wraps` |
| `Button` | `Kind`, and everything a platform button has: `Content`, `Command`, `Click`, `IsEnabled` (the web's `Disabled`) |
| `CodeBlock` | `Code`, `CodeLanguage` (the web's `Language`; every element already has a `Language`) |
| `Dialog` | `Open` (two-way), `Title`, `Body`, `Actions` |
| `Disclosure` | `Title`, `Summary`, `Status`, `Open` (two-way), `Body`, `Edge` (a color token, in place of the web's `Class`), `Running` |
| `DroverMark` | `Foreground` |
| `EmptyState` | `Title`, `Cause`, `ActionLabel`, `ActionHref`, `ActionCommand` or the `Action` event |
| `Field` | `Label`, `Value` (two-way), `Type`, `Placeholder`, `Hint`, `Error`, `Disabled`, `MaxLength` (the web's `maxlength`), `Autofill` (the web's `autocomplete`), the `Submitted` event |
| `Icon` | `NameOfIcon` (the web's `Name`), `Size`, `Foreground`. Drawn from `IconShapes` |
| `IconButton` | `Icon`, `Label` (required: it throws without one) |
| `ListCard` | The one frame for a list of things to open. `Title` is the link named by the item; `Facts`, `Leading`, `Status` and `Trailing` fill its four columns; `Beneath` holds a block below the frame. The `Opened` event makes the whole card a tap target and gives its name a keyboard path; with no handler the name is plain text |
| `ScreenHeader` | The one header at the top of a work screen. `Title` is the level one heading; `BackLabel` and `BackAddress` add the arrow button (`ScreenBack`) when both are set; `Actions` holds icon buttons and icon menus |
| `MarkdownView` | `Text`, `Streaming`. Draws the nodes of `MarkdownDocument` as native text |
| `Menu`, `MenuItem` | `Label`, `AccessibleLabel` (the web's `AriaLabel`), `TriggerIcon`, `ShowTriggerLabel`, `Items`; an item's `Selected` event |
| `NotFound` | |
| `SegmentedControl` | `Label`, `Options`, `Value` (two-way), `Disabled`, `Fills`. Values are text: XAML has no generic controls |
| `Select` | `Label`, `Options` (a list of `SelectOption`, in place of option elements), `Value` (two-way), `Disabled` |
| `SetupSteps` | `Current` |
| `Sheet` | The dialog at an edge: the trailing edge, or the bottom below 640 |
| `StatusNote` | `Text`, `Kind` |
| `Switch` | `Label`, `About`, `Value` (two-way) |
| `Tabs` | `Label`, `Items`, `Active` (two-way), `Panel` or `PanelFor` |
| `TextArea` | `Label`, `Value` (two-way), `Rows`, `Placeholder`, `Hint`, `Machine`, `Disabled`, `MaxLength` |
| `Timeline` | `Model`, `Label`, `EmptyText`, `AsList`, `PageSize`, `Detail` |
| `ToastHost` | `Model`. The shell has the one instance |
| `Tooltip` | `Text`, `Described` |

Native only, because the web does these in CSS:

| Control | What it is |
| --- | --- |
| `Facet` | The frame, and the only place corners are drawn: `Role` (a control or a larger frame), `Fill`, `Edge`, `Ring`, and `Cut` where a faceted frame needs a size of its own. The theme's corner language decides what it draws: cut corners, rounded corners, or ticks. A panel; what is put in it sits on the surface |
| `Yard` | The texture behind the working area: dots, lines, a grid, or nothing, as the theme says |
| `Link` | An anchor: `Text`, `Address`. An in-app address goes through the navigator. External links and markdown links share `OpenOutside`: when the browser refuses a new tab, the current tab follows the address instead |
| `PlainButton` | The base of every pressable control on a plain surface. Colors are token names |
| `FlowPanel` | A row that wraps: `Gap`, and `FlowPanel.FullRow` on a child |
| `TravelLine` | Live state: a tool is running |
| `LiveLight` | Live states: thinking (breathes), work passing between bots (one pulse), with bloom |
| `Motion` | `Breathe`, `Arrive`, `Pulse`, `FadeIn`, `Rest`, `Duration`. With reduced motion each leaves its element still |

### Rules a control follows

- **State is painted, not styled.** `Pressable` tracks hover, press, and keyboard focus and calls `Paint()`. A control sets its frame's colors there from tokens.
- **Focus.** The ember ring, two wide, is drawn only when the keyboard moved the focus. On a `PlainButton` it is outside the control with a gap of two; set `RingInside` where something would clip it. On a `Facet` it is inside the edge (`Facet.Ring`), because a cut frame cannot show a ring outside itself.
- **Targets.** `Targets.Minimum` is 44 where the pointer is coarse and 32 otherwise. Every pressable control uses it for its smallest height.
- **Motion.** Only `Motion`, `TravelLine`, `LiveLight`, and the avatar's canvas may repeat or run a clock. A rule test holds this.
- **Names.** A control that shows no words gets `AutomationProperties.Name`. A control that is decoration sets `AccessibilityView.Raw`. Use `RolePeer` or `RoleButtonPeer` to say what a drawn control is: a picture, a tab, a link.
- **Dependency properties.** Register with `Dp.Register<TOwner, TValue>(nameof(Property), default, owner => ...)`. Keep the registration field and its property together in `#region Properties`.

### The conversation's pieces

What a conversation shows and what opens beside one is in `src/Drover.Native/Chat`, one control for each component of the retired web client's conversation, with the same name and the same parameters. Their logic is in `Drover.Presentation/ViewModels/Chat`, moved from the components as it was, and tested in `tests/Drover.Presentation.Tests/ViewModels/Chat` with the web client's cases and its recorded event logs (`tests/Drover.Presentation.Tests/Fixtures`).

| Control | Takes | Raises |
| --- | --- | --- |
| `ToolCallCard` | `Card`, `BotName`, `Decide`, `Forget`, `Forgotten`, `MemoryHref` | |
| `DelegationCard` | `Item`, `ChildContent` (the child's activity, as an element) | |
| `TakeoverCard` | `BotName`, `Reason`, `Phase`, `Sending`, `Problem`, `Access`, `SetupHref`, `AnswerBesideScreen`, `TakeFocus` | `OpenScreen`, `Done`, `CouldNot` |
| `TakeoverCall` | `Takeover`, `BotName`, `Takeovers` | |
| `FileCard` | `File`, `ReadWhole` | |
| `ActivityPanel` | `Bot`, `Bots`, `RunId`, `Version` | |
| `RunMomentDetail` | `Tree`, `Moment`, `BotName`, `ChildName`, `Decide`, `Results` | |
| `FilesPanel` | `State`, `BotName`, `Files`, `Problem`, `ReadWhole` | `Retry` |
| `MessageVersions` (native only, inside `ChatEntry`) | `ShowVersion`, `OnAct`; `Show(message)` draws the message's `Versions` switch ("2 of 3" between previous and next) and Edit or Write another reply when the message offers it | |
| `ExchangedFiles` | `Bot`, `Version` | |
| `MemoryPanel` | `State`, `BotName`, `Notes`, `NearlyFull`, `Problem`, `EditorHref`, `OnAdd`, `OnChange`; `FocusNote(id)` | `Remove`, `Retry` |
| `MemoryRail` | `Bot`, `Version` | |
| `SummaryPanel` | `Bot`, `Version` | |
| `ScreenPanel` | `Bot`, `Takeover` | `HandedBack` |

- **A piece draws once for a batch of changes.** Setting several values on a piece, or a view model changing several things, ends in one redraw (`Chat/Batch.cs`): the values are handed to the view model, its `ParametersSet` runs once, and the piece draws. This is the order a component had.
- **A card is drawn before it is first measured.** `ToolCallCard` and `DelegationCard` run their waiting draw in `MeasureOverride` (`Batch.DrawIfWaiting`), and show nothing of their frame until then, so a card appears with its title and state in the frame it first appears in. A question offers Approve and Deny only once it says who asks and what for (`ToolCallCardViewModel.CanAnswer`).
- **What a person is using is kept.** A piece keeps the elements a person may be in the middle of using (a button with the focus, a box they scrolled, a field they type in) and changes them in place. `Pieces.Detached` moves a kept element into a new drawing.
- **A panel makes its own view model** with `Pieces.Make<T>()`, as a screen does with `Make<T>()`.
- **`ScreenPanel` hosts the sandbox screen.** It makes one `Bots.SandboxScreen` when the bot has a screen to show, keeps it while the sandbox's state is read again, and reads the state again when the screen says the sandbox changed. The gallery puts a labeled box in its place, because the screen's own view model would ask a real server.
- **A line that takes the place of a button** is a `FocusLine`: it can be given the focus and is not a stop on the way through the page, as an element with a tab index of minus one is on the web.
- **A status** is a text block with a live setting whose automation peer raises `LiveRegionChanged` when its words change (`Pieces.SayWhenShown`, `StatusNote`). That is the native form of a region marked as a status or an alert.
- **The message field's accessible description** comes from `ComposerViewModel.FieldDescription`: while the conversation could not be read, the disabled field says it will be available once the conversation has loaded; the description clears once it has.
- **The gallery reads from a sample server.** The panels that load from the server are shown with their real view models, which ask `Pages/Dev/SampleServer.cs` inside the app. Nothing leaves the app.
- **Native file previews stay inside native controls.** `FileCard` hosts `NativeFilePreviewControl` (`NativeFilePreviewViewModel`) for files other than bot HTML. The server's `/api/spaces/{spaceId}/files/{fileId}/native-preview` endpoint inspects the stored bytes within tight size, page, and pixel caps and returns bounded Markdown text, a static PNG picture copy, extracted PDF pages, a parsed flowchart node-and-edge model, or a refusal code. Markdown is parsed by `FileMarkdown` with raw HTML disabled, node count and depth capped, and remote images converted into external links whose target address is visible; fenced `flowchart` and `graph` blocks draw as native node-and-edge cards. For PNG, JPEG, GIF and WebP originals up to 25 MiB and 50 million source pixels, the server returns the first frame as a PNG with a longest edge of at most 480 pixels, subject to the decode limits below. The client checks the returned copy against its 1 MiB and 4,000,000-pixel limits before decoding it. PDFs extract up to 10 pages and 16 KiB of text or bounded page images and step through pages with keyboard-focusable Previous page and Next page buttons. Closing a preview or replacing the file clears all decoded bitmaps, pages, and diagram controls and keeps Download available.
- **Conversation picture copies.** The native preview route opens the original through the caller's normal file authorization before checking a server-side cache keyed by space, file and content hash. A cache miss accepts originals up to 25 MiB. The decoder checks input dimensions before allocating pixels (50 million source pixels, 16 million decoded pixels), strips compressed PNG metadata, and schedules one decode at a time. JPEG input is limited to 64 scans. Progressive and multi-scan sequential JPEGs are limited to 16 million source pixels. The decoder checks a two-second deadline between scanlines and after native calls. At the deadline the caller receives a busy refusal and the scheduling slot opens. A native call already in progress retains a worker until it returns. At most two workers can be live, including overdue calls. The server fits the first picture to a 480-pixel longest edge and returns a static PNG. At most three requests hold original read buffers at once. Each client sends at most two picture reads at once. A member can run two misses concurrently, with up to twelve requests including waiters and up to 200 starts per minute, enough for one full Files panel. A space can run eight concurrently with up to twenty requests including waiters. The server admits up to 32 requests across spaces. A request can wait up to eight seconds for member and space capacity. Hits bypass these budgets. A completed copy stays available even if its requester leaves. Copies expire after ten minutes and share a 16 MiB cache. Download still serves the original bytes. When the ten-second server deadline expires, the preview route returns a busy refusal. The card retries that refusal or its own twelve-second timeout once after the 420 ms retry delay. A permanent refusal or an unsuccessful second attempt shows a message and keeps Download available.

Differences from the web client in these pieces, each seen in the pictures in `docs/screenshots/native-chat-pieces`:

| Piece | Difference |
| --- | --- |
| Output of a tool | Error output is tagged `stderr: ` in the error color. The web draws the tag in an outlined box, which text here cannot hold |
| `FileCard` on a device | "Save" and "Share" show when the app registers an `IFileDelivery`. None is registered yet, so a device shows "Download", which opens the file's address outside the app, where the person is not signed in. In a browser "Download" asks the server for the file as one to keep. For a bot-shared HTML file, the browser target opens the server's single-use preview grant in a sandboxed, credentialless iframe (`HtmlPreview`), whereas desktop and Android targets refuse in-app HTML preview before requesting a grant and leave Download or Save as the fallback (ADR 0017) |
| State badge in a card's title | Regular weight. On the web it takes the title's weight |
| A conversation's model | Only this client offers it. When the bot may answer with more than one model, the row behind the composer's plus button has a `Select` named "Model for this conversation" beside Attach: the bot's own model first, then the others the server allows. A choice the server refuses is said in a toast and the choice goes back. A finished reply's usage line starts with "Written by" and the model that wrote it, when the server kept it |
| A reply's reasoning and sources | Only this client shows them. A reply's reasoning is a closed `ReplyReasoning` section above its words, with the live edge while it arrives; a reply whose reasoning arrives before any words speaks from the bot's head and says "Thinking", in place of the list's own thinking line (`MessageListViewModel.ShowsThinking`). Its sources are a `ReplySources` list under the words: each the source's title as a link that opens outside the app, and the site beside it. The web client keeps its thinking line and shows neither |

## The shell

- **Start.** `App` builds the services (`AppServices.Build()`), starts the platform's side (`PlatformShell.Start`), and shows `ShellView`: a `Frame` for the screen and the `ToastHost` over it.
- **Services.** `Shell/AppServices.cs` is the one place services are registered. `AddPlatform()` is defined once in `Device/PlatformServices.cs` and once in `Platforms/WebAssembly/PlatformServices.cs`, and adds what differs. A view model is not registered: a screen makes its own with `Make<T>()`, which fills the constructor from the services.
- **Addresses.** `Shell/Screens.cs` is the one place a screen is given its address patterns. They include every page address the retired web client had, so a link someone kept still opens; `NativeScreensTests` fails when one is missing. `RouteTable` matches literal segments before ids and ids before free text, and hands the screen a `RouteMatch` with the path parameters, the query, and the fragment.
- **Going somewhere.** Ask the `Navigator`: `navigator.NavigateTo(Addresses.Bots)`, or with `replace: true` to leave no step behind. In XAML use `<dv:Link Address="/bots" Text="..." />`. Never navigate the frame directly.
- **Back on a device.** Android's Back asks the navigator for one step back (`Navigator.StepBack`). An open dialog or sheet closes first, then an open rail (into its conversation, leaving no step behind), then the app goes to the address before, through `Leaving`, so the bot editor still asks before it drops changes. With nothing open and no address before, Android leaves the app. Something new that opens over a screen and should close on Back registers with `Navigator.AddCloser` while it is open. `Platforms/Android/BackButton.cs` listens to the system only while `CanStepBack` is true, because from Android 16 listening alone decides whether the app takes Back. `scripts/check-native-android-back.py` checks Back on the first screen in the emulator; the other steps are covered by the navigator's tests, because the emulator has no server.
- **In a browser** the address bar and the browser's history follow the navigator, and the browser's back and forward buttons move the app, so every address is a link that can be opened, reloaded, and shared. The browser's Back arrives through `Navigator.Arrive`, which takes the same step as a device's Back (`StepBack`): an open dialog closes first, and a screen holding work asks first through `Leaving`. When the app stays, `Platforms/WebAssembly/PlatformShell.cs` puts the address bar back. A step the app takes through its own history, such as leaving the bot editor by going back, moves the browser's history the same way. Closing or reloading the tab asks the browser to confirm while `Navigator.HoldsWork` answers true; the bot editor sets it while changes are not saved. `scripts/check-native-browser.mjs` checks both on the bot editor, and checks that leaving a new bot, which opens as an unsaved draft, creates no bot.
- **Layouts.** A screen's root element is its layout: `EntryLayout` for sign-in and setup, `WorkLayout` for a screen of the workspace. `WorkLayout` holds the `SessionGate`, the bar, and below 560 pixels the dock. Children are stacked in the layout's column, and the first child of a work screen is its `ScreenHeader`. Until the gate knows who is signed in it shows `Splash`: the mark alone on the first surface, `DvMarkSplash` across, still, with no words; its name, "Drover is starting", is what assistive technology reads. `ShellView` also gives the window the first surface as its background, so a desktop window clears to it before its first frame.
- **Diagnostic log.** The app keeps a log of its warnings and failures on the device, so a person can attach it to a bug report. Nothing sends it anywhere. It is written through the app's logging (`DiagnosticLogProvider`, added in `Shell/AppDiagnostics.cs`), and `DiagnosticEntry` keeps only an allow-list: time, level, logger name, event number, the message template of Drover's own code, values that cannot hold text (numbers, flags, durations, times, named choices), and for an exception its type, result code and frames read from the runtime, with source files named without their folders. Formatted messages, text values, ids, exception messages and other libraries' messages are never written. The log is two parts of at most 64 K characters each (`DiagnosticLog`); the last failure nothing handled is kept apart. On a desktop or a phone it is text files under the app's local data folder (`Drover/diagnostics`); in a browser, local storage entries named `drover.diagnostics*`. A failure nothing handled is one the app's or the runtime's unhandled-exception events report, or an error the platform's dispatcher logs after catching it (`Uno.UI.Dispatching.NativeDispatcher`, which on desktop and in a browser catches what an interface callback throws). The next start shows a toast that offers "Save diagnostics", once per failure. "About Drover" in the account menu shows the version and platform and saves the log at any time, through the same save paths as an extension (`PlatformFiles.SaveLog`: the system's save dialog on a device, a download in a browser).

## Transport and security

Unchanged from the clients this one replaces, because it compiles the same source.

- **In a browser:** the app talks to its own origin. The browser holds the session cookie, which script cannot read. `AntiforgeryHandler` adds the token to every request that changes something. No token is in storage. Local storage holds the theme choice and the diagnostic log (below), which keeps nothing secret. A tool server's sign-in started here names the path the build is served under (`AppSignInReturn`), `/`, so the browser comes back to this client on the connection.
- **On a device:** `AppRequests` sends requests only to the HTTPS origin the person chose, follows no redirects, and keeps cookies in memory. `ServerAddress` keeps the address in plain settings. `NativeSession` keeps the sign-in that outlives the app through `ISecureStorage`: on a phone, the platform's credential store (`DeviceSecrets`); on a desktop, the operating system's (`DesktopSecrets` over `SystemSecrets`): the login Keychain on macOS, the Secret Service on Linux, and on Windows a file under local application data that only the person's Windows account can decrypt (DPAPI). Each key is its own entry, and signing out removes it. When the computer has no store the app can use (a Linux machine with no Secret Service), or the store fails, the app logs one warning and keeps the sign-in only until it closes. No secret is written to a plain file or the log. `tests/Drover.Native.Desktop.Tests` runs the store against the real one of the computer it is on; the desktop workflow runs it on each system, on Linux under a test Secret Service. A tool server's sign-in comes back into the desktop app through `DesktopSignInReturn`, a listener on `127.0.0.1` at a port the system gives it, started the first time a sign-in asks for it. It holds at most a few incomplete connections and refuses any more at once. It reads one request, hands a well-formed ticket to the tools screen through `IncomingNavigation`, and answers with a page that loads nothing ([06-mcp.md](06-mcp.md), "Coming back into a native app"). The same test project covers it.
- **Content security policy.** The published browser build runs under the policy `SecurityHeaders` sends, with nothing added. The platform's font fallback, which would fetch Noto fonts from another site for a character Inter lacks, is switched off in `Platforms/WebAssembly/Program.cs` (`SkiaBackend.CreateFontProvider(enableFallback: false)`): the policy refused every such fetch anyway, with an error in the console for each. The platform's runtime evaluates five small expressions as script at startup, which the policy forbids. `Platforms/WebAssembly/WasmScripts/NoEval.js` answers exactly those five with functions and passes anything else to the browser's own `eval`, which the policy refuses. So a new expression fails loudly instead of being allowed. After any SDK upgrade, and after first using a platform feature (clipboard, launcher, file picker, share), run the published build under the policy and read the console.
- **Calling the browser.** Use `[JSImport]` on a browser function, as `Platforms/WebAssembly/Browser.cs` does. Never `WebAssemblyRuntime.InvokeJS`: it is `eval`. A shared script module is imported with `JSHost.ImportAsync` relative to `_framework`, as `PageCookies` (`cookies.js`) and `BrowserPushRegistration` (`push.js`) do.
- **The page and its worker.** The browser build's page is `Platforms/WebAssembly/index.html`; `DroverIndexHtml` in the project file writes its `<base>` (`/`) and gives it to the platform as its page template. It loads the shared `boot.js` before anything is drawn: the mode of the stored theme choice (the JSON `ThemeChoice` writes, or the one word an older build wrote; anything else is dark) is on the page from the first frame as `data-theme`, `light`, `dark` or `black`, and not set at all for a person who follows the device (`boot.css` colors the loader in it, with the first surface of the default theme, and `BootThemeTests` runs both files in a real Chromium), and the service worker is registered so that a new build takes over at once, and the page reloads unless the person is writing, then when they look away. The worker is the shared `service-worker.published.js`: `DroverServiceWorker` in the project file puts it in place of the platform's own worker after publish (the platform's PWA switch stays off, so its worker is never registered) and writes `service-worker-assets.js`, the build's files with a SHA-256 of each that the worker stores at install. It removes every other stored shell of the origin once its own is stored, the retired web client's and the one stored under `/native/` included, and leaves `/native/` to the server (`ServiceWorkerTests`). The retired web client's worker had the same address, `/service-worker.js`, so a browser that ran the web client gets this worker as its update, keeps its push subscription, and reloads into this client. `manifest.webmanifest` makes the client installable at `/`, with the id the web client's manifest had. The loader's picture comes from the app's own package; the platform's default asks another site for one, which the policy refuses.
- **The address it was served under before.** Until 2026-11-20 the build was served at `/native/`, beside the web client. `RetiredNativeAddress` in the server sends `/native` and every address under it to the same address at `/`, query included, when the path is plain segments (letters, digits, `-._~`, as every address the client writes is); any other path goes to `/` alone. The target is built from `/` and those segments only, and sent as a local redirect, so it can never name another host. Three addresses under it are answered with fixed files instead, for a GET only:
  - `/native/service-worker.js`: a browser that installed the client there still has a worker registered for `/native/`, and when it next asks for this script the server answers with `retired-native-worker.js`. That worker removes the old worker's stored shell, unregisters it, and moves the pages it controlled to the root. If the old worker held a push subscription, it first leaves a note (the cache `drover-push-moved`, which no worker of the client removes), then ends the subscription.
    The note is read by `push.js` (`moved`, `forgetMove`) and acted on by `MovedNotifications` in `Drover.Presentation`, which the session gate runs once a person is signed in. The note holds the ended subscription's push service address. When the browser still allows notifications, the client at the root subscribes without asking and sends `POST /api/push/device/move` with the old address and the new subscription; a toast says notifications moved. The server checks that the old subscription was the signed-in person's and writes the new one in the same request and transaction, under that one identity, so an account that changed in another tab in between is refused and given nothing. A subscription another account held is never taken up for this one. A subscription the client at the root already holds for another account is never ended: the person is told to turn notifications on in the account menu if they want them instead, which is their choice. The new subscription's address is kept with the note before the server is asked. One the server refused is ended, and only while it is still the browser's subscription, so one another account made in another tab meanwhile is never touched. When the server's answer is lost, nothing is ended: the next start asks the server about that subscription, keeps it when the move was taken (the previous one is gone by then), and ends it and tries again when it was not. The new subscription is kept with the account it was made for, and only that account settles it: while another account is signed in, the note and the subscription are left as they are, because the server's no for that account says nothing about the first one. The device-state answer names the account it is for (`PushDeviceState.AccountId`), so an account switched in another tab between the steps is caught the same way. In every other case (not this person's, permission gone, no server key, no subscription made) a warning toast says notifications are off and where to turn them on, and the note is forgotten; permission is never asked at start. A server that cannot be reached, or a browser call that fails, leaves the note for the next start (`MovedNotificationsTests`, `RetiredNativeWorkerTests`).
  - `/native/` with no query, the start page of an app installed from there: `retired-native-page.html`, which links `/native/manifest.webmanifest`, says Drover has a new address, and moves itself to `/` after two seconds. The retiring worker leaves this page alone.
  - `/native/manifest.webmanifest`: `retired-native-manifest.webmanifest`, the installed app's manifest under its old id `/native/`, with its start address and scope moved to `/`. The browser reads it while the start page is open and updates the installed app in place: the same app, now opening `/` in its own window. `InstalledAppMigrationTests` installs an app with the manifest the client shipped under `/native/` in a real Chromium, through the browser's own install, and checks that it opens the client at the root and that its scope moves to the root. Installing again from `/` is not needed. `ToolSignInOptions.WebPaths` still accepts `/native/`, so a tool sign-in started there before an upgrade comes back through the redirect.
- **Notifications in a browser** go through the shared `push.js` and `push-worker.js`, behind `IPushRegistration` (`BrowserPushRegistration`). A browser failure is reported as `DeviceCallFailedException`. The development build has no service worker (the platform's own module worker is what a development build serves), so notifications need a published build.
- **On Android** the native build uses the code written for the retired Android shell (`src/Shared/Android`): UnifiedPush registration and delivery, notifications with replies, the sign-in return scheme, shares in, and saving and sharing files. The few things that code asks of its host by the shell's framework's names (main thread, current activity, the notification permission, the cache folder, services) are in `Platforms/Android/AndroidShims.cs`; the one share call is behind `ShareSheet`, which each app defines. The application id is `com.forresttechnologies.drover.client`, so it installs beside the preview (`.preview`, ADR 0014), and the server's `ToolSignInOptions.AppSchemes` accepts both. A notification that arrives with no interface running is handled with services made for that work (`IPlatformApplication` in the shims). A share that arrives the same way waits in the process-wide `ShareInbox.Process`, so the services the interface builds when it starts read the same share. A share is placed only if the person and the server are the ones it arrived for: the inbox notes when it starts copying the files, and signing in, signing out or choosing another server (`ShareInbox.Reset`) ends every copy still running, drops a waiting share, and removes its staged files. The activity hides `StreamPresence` when it stops and shows it when it starts, so no live stream stays open in the background and the server wakes the phone by push instead; the native Android build also carries the embedded distributor (`13-deployment-and-push.md`, path 2).
- **On a desktop** `DesktopNotifications` follows the signed-in person's live signal stream while the app runs. It asks the server for the same safe wake text and per-bot choice as push before showing anything. The foreground conversation stays quiet. A selected conversation in an inactive or minimized window still notifies. `DesktopNotificationSink` uses each system's notification facility. Selecting one opens its main conversation or side chat through `IncomingNavigation`. Session changes cancel pending work and remove old notifications before they can navigate. A missing system facility disables this delivery path without stopping the app. The Notifications dialog describes this live-only delivery and says that system settings can still silence it. It does not offer the background push switch.
- **On Android** a conversation file is saved by asking where it goes first (`DocumentDestination`, with a type taken from the file's name, since a stored type that disagrees with the name can make the picker add a second extension) and then streaming the download into the place chosen, so a person who cancels costs no download and nothing passes through the cache. The save can be cancelled. Only one save runs at a time, from the picker to the end of the clean-up (`SingleSave`), because the marker that recovers an interrupted save is one record: a second file card that asks meanwhile is told another file is being saved. The pressed button stays enabled while it works, so focus stays on it, and the card says why a save or share failed when the platform or the server gave a reason (`FileDeliveryException`: a fixed sentence, never a system error, which can name a path). If Android stops the app while its picker is open or while the file is being written, the document the picker made would stay empty or cut off, so the next start removes it and says so. A copy made for sharing is removed at sign-out and at every start of the app. A save shows no progress yet.
- **On a desktop** a conversation or project file is saved through the system's save dialog (`DesktopFileDelivery`). Sharing puts a copy in a folder of the app's own and opens the folder: the file itself is never opened, because a bot can send a program.

## How to add a screen

Every screen of the retired web client has moved; the steps below were how, and are how a new screen is written. A screen is a view model, its tests, and a page.

1. **Name the screen's job** in one sentence. List its states: loading, empty, failed, each kind of content.
2. **Write the view model** in `src/Drover.Presentation/ViewModels/<Screen>ViewModel.cs`. `[ObservableProperty]` for what the screen shows, `[RelayCommand]` for what it does. It takes `DroverApi`, `SessionState`, `Navigator`, `ToastService`, a logger, in its constructor. It never touches a UI type. A route parameter is a method the screen calls (`Open(Guid botId)`).
3. **Write its tests** in `tests/Drover.Presentation.Tests/ViewModels/`. Use `FakeServer` to say what each address answers: the tests are the specification of the screen's behavior.
4. **Write the page**, `src/Drover.Native/Pages/<Folder>/<Screen>Page.xaml` and `<Screen>Page.xaml.cs`, and give it its address in `Shell/Screens.cs`. A screen registered before its page is written derives from `PendingScreen`.

   ```xml
   <shell:Screen x:Class="Drover.Native.Pages.BotListPage"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:dv="using:Drover.Native.Controls"
                 xmlns:layouts="using:Drover.Native.Layouts"
                 xmlns:shell="using:Drover.Native.Shell">
     <layouts:WorkLayout>
       <dv:ScreenHeader Title="Bots" />
       <dv:StatusNote Kind="Danger" Text="{x:Bind Model.Problem, Mode=OneWay}" />
       ...
     </layouts:WorkLayout>
   </shell:Screen>
   ```

   ```csharp
   public sealed partial class BotListPage : Screen
   {
       public BotListPage() => InitializeComponent();

       public override string Title => "Bots - Drover";

       public BotListViewModel Model { get; } = Make<BotListViewModel>();

       protected override Task Opened(RouteMatch<Type> route) => Model.Load();
   }
   ```

   `Pages/SignInPage.xaml` is the worked example. Read parameters from `route` in `Opened`: `route.GuidParameter("BotId")`, `route.QueryValue("section")`. `Opened` returns its task to `Screen`, which awaits it through `SessionState.OpenScreen`: a request that fails because the server cannot be reached is caught there and the screen's `SessionGate` shows "The server did not answer", whose Try again opens the screen again. Never make `Opened` or anything it starts `async void`. The native CI workflow builds a separate check binary with `-p:DroverUnreachableCheck=true` (excluded from shipping builds) and runs it with `DROVER_UNREACHABLE_OPEN_CHECK=1` on desktop and the `drover.unreachable_open` extra on Android (`UnreachableOpenCheck`), verifying that offline deep links catch each screen-open failure without unhandled dispatcher errors.
5. **Lay it out from tokens.** A column is a `StackPanel` with `Spacing`, a row that wraps a `FlowPanel`, a faceted frame a `Facet`. A narrow layout is a `SizeChanged` handler that reads a breakpoint from `Styles.xaml`.
6. **Add its samples to the gallery** if it brings a control the gallery does not show.
7. **Screenshot it** at 1280 and 360, dark and light, and each state. Put the pictures in the pull request.

## Pitfalls found

- **This app uses static font cuts.** `Assets/Fonts/Inter.ttf.manifest` maps weights 400, 500, 600 to static files, and large headings use the display cut through `DvFontDisplay`. The old foundation note that Skia cannot vary fonts is obsolete: the resolved 7.0.65 `SkiaFontProvider` applies OpenType variation axes. Variable-font parity still needs a runtime comparison before changing the bundled font strategy.
- **A wrong family name after `#` shows nothing at all.** `ms-appx:///Assets/Fonts/InterDisplay.ttf` works without a name.
- **Line height.** A text box keeps its letters at the top of the line height and puts the rest below; a browser centers them. The text styles correct this with a padding above and the same margin taken back below. Do not set `Margin` or `Padding` on a styled `TextBlock`: wrap it, or space it with the parent's `Spacing`.
- **A shape must not size its parent.** A `Polygon` inside a `Grid` asks for the size of its points, which grows the grid, which moves the points: a layout cycle. `Facet` keeps its shapes on a `Canvas`.
- **A full radius draws an ellipse.** A corner radius above half the box is not clamped. Use `Pill.Round(border)`.
- **Text inside a control does not follow a later color change** unless the template binds it. The two templates bind `Foreground`, `FontFamily`, `FontSize`, `FontWeight` on the presenter, and `PlainButton` recolors text and icons put in it as elements.
- **`InlineUIContainer` is not supported in a `TextBlock`.** A picture in markdown stands as its own block.
- **Invariant globalization** is on for all of Drover. The platform asks for the device's culture by name at startup, so the project sets `PredefinedCulturesOnly` to false.
- **JSON by reflection** is off by default in this SDK's browser build. The project turns it on, because the API client reads contracts that way, as the web client does.
- **A layout change in `SizeChanged` must be guarded.** Change the layout only when the outcome differs from last time, or the handler runs forever.
- **APIs that are not implemented** fail the build as `Uno0001`. That is how `PasswordVault` on desktop and `Timeline.AutoReverse` were found. Treat the error as the answer, not as something to suppress.
- **A generic helper that makes a view model** must carry `[DynamicallyAccessedMembers(PublicConstructors)]` on its type parameter, as `Screen.Make` and `Pieces.Make` do. Without it the trimmed browser build drops the constructor and the view model fails with `CtorNotLocated` only in a Release publish.
- **Read a field's text when Enter arrives**, not the view model's copy: in a browser the change event for the last letters can come after the key. `Field` and `Composer` do this.
- **Browser semantic text selection stays with its field.** Uno 7.0.65 creates semantic inputs with field ids in
  [`SemanticElements.ts`](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/src/Uno.UI.Runtime.WebAssembly.Browser/ts/Runtime/SemanticElements.ts),
  but its shared hidden input keeps pending selection state in
  [`BrowserInvisibleTextBoxViewExtension.ts`](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/src/Uno.UI.Runtime.WebAssembly.Browser/ts/Runtime/BrowserInvisibleTextBoxViewExtension.ts).
  On a slow host, the document selection handler can apply one field's pending caret to the next semantic field.
  `WasmScripts/Accessibility.js` recognizes only those semantic input ids and clears the shared restore. Before C#
  binds the platform function, it transparently wraps `SemanticElements.updateTextBoxValue` and records only the
  normalized range and direction for each element. A matching `select` event is a managed echo and is ignored. A
  different range from the currently focused semantic field goes through the runtime's existing managed export.
  The shared hidden input and unrelated page inputs keep the runtime handler. No field text is stored or logged.
  `scripts/check-native-text-selection.mjs` executes the shipped adapter against stale, backward, late-field,
  unrelated, repeated-focus, late-initialization, and throwing-export cases.
- **The development build logs a failed connection to a development server** on every start. It is the hot reload client looking for an editor, and it is not in a release build.
- **Under a virtual display the message list kept its old picture when a message in it only got shorter.** Seen on the desktop build under `xvfb` only: an older reply's actions were collapsed, the layout was right (the message and the column reported their new heights), and the picture stayed the one from before until something else was drawn. Growing drew at once. It was not shown on a real display or another target, so the client holds no workaround for it; a picture taken under `xvfb` right after something collapses can be one frame behind.
- **The browser's accessible boxes go stale when an ancestor moves.** Uno 7.0.65 updates the box of a control only when that control's own visual changes its offset or size (`RouteVisualOffsetOrSizeChanged` re-emits descendants only below a scroll source). The composer grows upward from the bottom of the page, so opening or closing its options row, or the field growing a line, moves the composer and leaves the boxes of Attach, More options and Send where they were, off by the distance moved (27, 34, and 40 px were measured). A control's box is also kept relative to its nearest accessible ancestor, so a container that moves must itself be an accessible one, which updates when it moves while the boxes inside it stay right. `Composer` and its input frame therefore carry names (`Message composer`, `Write a message`). Any new part of the interface that grows or collapses above controls the person can reach needs the same: name the container, and add the move to `checkComposerBounds` in `scripts/check-native-browser.mjs`, which focuses each button and compares every edge of the ring it draws, seen in a picture much larger than the box, with the same edge of the accessible box, at 1280 and 360 px and with a run held in progress so that Stop is checked as well (`checkStopBounds`; the mock holds a run by sending its start and never its end). The desktop screen checks compare each peer's rectangle with the control's at both widths, with the options open, the field three lines tall, and a run held in progress (`ScreenChecks.ComposerBounds` and `StopWhileWorking`; the check server's model holds its second run until Stop ends it); a peer reads the live layout, so it was right in every state measured.
- **Starting is slow in a development browser build**: about fifteen seconds in headless Chromium, three for a published build.

## Known differences from the retired web client

Each was seen in the screenshots or in the browser's element tree, while both clients ran. None is hidden by a workaround.

| Area | Difference |
| --- | --- |
| Text rendering | Light text on a dark surface looks slightly heavier than in Chromium. Same font, same weight; the renderer applies no gamma correction to text |
| Headings | `text-wrap: balance` has no equivalent: a heading that wraps breaks greedily |
| Type | Sizes between 14 and 28 use the text cut where the web's variable font interpolates toward the display cut |
| Inline code | Has its surface color but no padding or rounded corners |
| Select | The platform's list: its arrow and its width differ from a browser's select |
| Tooltip | Opens where the platform places it, which near an edge is not below the control |
| Lane heads of a timeline | Do not stay in view while a long timeline scrolls |
| Task list boxes | Drawn, not the browser's checkbox |

## Limits of the platform that threaten parity

Verified in Chromium against the build of this work. Each was to be decided before the web client was retired; it was retired first (see "Retirement gate"), and each stays open against this client.

| Area | What was found |
| --- | --- |
| Accessibility in a browser | The app now sets `FeatureConfiguration.AutomationPeer.AutoEnableAccessibility` before creating its UI, so the semantic tree does not require a hidden opt-in. The resolved 7.0.65 runtime activates too early and can leave an empty semantic root, so the shell rebuilds it through the runtime’s own managed accessibility exports after loading. The published smoke check requires actual headings on every tested route and invokes the download through its accessible button. This deliberately accepts the runtime cost. Screen-reader testing across supported platforms is still required. Historical foundation observation: the interface is a canvas. The platform builds a parallel tree of real elements (headings with levels, buttons, inputs with their labels, tabs, images with names, landmarks), but only after the person activates a hidden "Enable accessibility" element that the first press of Tab reveals. Keeping the tree on for everyone is possible (`FeatureConfiguration.AutomationPeer.AutoEnableAccessibility`) and the platform's documentation warns of its cost |
| Links in the browser accessibility tree | Uno 7.0.65 creates semantic anchors without `href`. The browser adapter gives those anchors a link role and keeps Uno's activation handlers. The published smoke check activates the usage link for a bot and checks that its details open |
| A part shown again in the browser accessibility tree | Uno 7.0.65 can leave a node `hidden` for good. Setting `AutomationProperties.AccessibilityView` on an element that is not in the tree, as every `Icon` does when it is made, rebuilds the whole semantic tree. A rebuild that passes a collapsed element marks it as left out; when that element is shown again where it stood before, the runtime adds its node hidden, takes the mark as an order to build it, finds the node there, and does not take `hidden` off it, and with unchanged bounds nothing later does. On a phone this hid the conversation from assistive technology after a panel was opened and closed, and on a wide window the right divider after its panel was opened again. The runtime builds an element's nodes anew, without the mark, when its `AccessibilityView` changes, so `SemanticTree.PutBack` changes it and changes it back, and `TalkWorkspace` calls it for the conversation, the rail and the dividers when it shows them again. The published smoke check opens and closes the activity at 360 px and requires the composer afterwards, and at 1280 px requires the right divider back where it was. Other controls that are collapsed and shown in place are not covered |
| Where a node stands in the browser accessibility tree | Uno 7.0.65 places an element's semantic node when the element itself is arranged, from the visuals as they are then, and a parent's visual is placed after its children's. A node under an element that has no node of its own is therefore placed as if that element had not moved, and is not placed again when only that element moves; a later rebuild of the whole tree puts it right. In the phone's dock no rebuild followed: the landmark stood at the top of the window and the account button in the first cell. `PhoneNav` calls `SemanticTree.PutBack` for its bar once a layout has moved its parts; the smoke check requires the five cells side by side at 360 px. Other screens still lean on a rebuild that happens to follow |
| Password managers | The platform keeps one real `input` per field in the accessibility tree (type `text` or `password`, named by the field's label), and focus and typing go to it, by keyboard and by pointer. A field with an `Autofill` name has it written to that input's `autocomplete` while the field is on screen (`Platforms/WebAssembly/WasmScripts/Fields.js`): on `/native/sign-in` the inputs carry `username` and `current-password`, on `/native/setup` `username` and `new-password`, which the browser check asserts. The page still has no `form`, so a manager that offers to save only on a form submission does not offer it; a value a manager fills reaches the field through the input's `input` event. The email input is `type="text"`: the platform sets it. Not yet tried with a real password manager |
| Text entry and input methods | Typing goes to the field's `input` in the accessibility tree, focused by keyboard or by pointer (measured in headless Chromium). That input carries no `inputmode`, so a phone's keyboard does not follow the field's type. The platform's hidden `input`, whose `inputmode` does, was not seen in use while that tree is on. Composition for languages that need it was not tested |
| Find in page, reader mode, translation, selection across the page | None works. Measured: `window.find("Use the account you made")` is false on `/native/sign-in` and true on `/sign-in`; the words exist only as labels of the semantic tree, which is not drawn. Selection works inside one text block that enables it |
| Browser zoom and text size | Page zoom scales the canvas. At 200 percent (a 640 px wide CSS viewport at twice the pixel ratio) sign-in draws sharp, at the same layout as the web client, with no sideways scroll. The browser's text-size setting has no effect (not re-measured) |
| Clipboard | Copying is asked of the browser without waiting for its answer, so a refusal cannot be told to the person |
| Download size and start | Integration pass, Chromium, cache and service worker off, against Drover's server: a first visit to `/native/sign-in` transferred 11.48 MB in 103 requests; `/sign-in` 4.14 MB in 76. The email field was in the accessible tree after 2.4 to 2.6 s (three runs) for the native build and 0.95 to 1.0 s for the web client. A conversation of about a dozen messages showed its messages 10.1 s after navigation in the native build and 2.0 s in the web client, on a machine loaded by other builds; the time is spent between requests, in the interpreted runtime. Remeasured on 2026-11-19 on an unloaded machine against a Release server, twelve messages, median of five page loads: 2.75 s native and 0.59 s web before the fix below, 2.51 s native and 0.55 s web after it; most of the native time is the runtime's start before the first request. The conversation is made once per opening; what showed "Nothing said yet" on the way was the message list, revealed before it had drawn the loaded messages (see "The conversation workspace") Earlier measurement on a Release publish: 46.2 MB of files, 14.5 MB if every file is fetched as its Brotli copy (the web client's publish: 12.5 MB, 4.1 MB). A first visit to `/native/sign-in` with no service worker fetched 104 files: 44.1 MB when nothing is compressed (before XAML resource trimming), 11.2 MB from Drover's server, which serves the build's own Brotli copies (`PrecompressedFiles`). Trimming is already full; IL stripping applies only to ahead-of-time compilation, which is off. See "Size levers" below |
| Service worker, install, push in a browser | The web client's worker, start script and notification script, shared. Verified in Chromium against Drover's server: the worker installs, stores all 169 shell files with matching hashes, and takes control. A real push delivered to the worker, and the notification tap, are covered by `ServiceWorkerTests`; a subscription against a real push service was not made from this machine |
| Script evaluation | The runtime evaluates script strings. Five are known and answered without `eval`; another may appear with an SDK upgrade or a platform feature |
| Credential store on desktop | The platform has none, so the desktop app uses the operating system's own through `SystemSecrets` (see "Transport and security"). On a computer without one (Linux with no Secret Service) the sign-in lasts until the app closes, as before |
| Variable fonts | The resolved 7.0.65 provider applies variation axes. This app still uses static cuts; matching the web’s variable rendering is unverified |
| Search engines | Nothing to index. Drover's screens are behind sign-in, so this costs nothing today |

## Current scope and remaining verification

Status is checked against source and build results, not commit subjects or a handoff. The original PR head was
`4f18a812505b90ecdcdc00cb46e98b73d34e1c1c`. At that revision, twelve of the sixteen screen classes registered in
`Shell/Screens.cs` derived from `PendingScreen`; the four implemented classes were sign-in, gallery, extensions,
and extension editor. Route registration alone was not implementation.

The setup steps, bot list, model connections, status, usage, and tool connections now have native pages and framework-free view
models. The setup account, provider retest, first-bot selection, role restrictions, bot creation, model key
replacement, usage selection, changed-tool review conflicts, external sign-in redirects, one-time ticket returns, and refused tool removal have fake-server tests. The first bot's instructions are moved into `FirstBot`
and called by both clients, preserving their text. Native page presence and passing view-model tests do not
establish visual parity.

No screen class derives from `PendingScreen` any more; the class stays for a screen registered before its page is
written. The last two to move were the bot editor and the share screen.

Projects have a native list and a native project screen (`ProjectListPage`, `ProjectPage`) with view models in
`Drover.Presentation` and screenshots from the browser check in the CI artifact. An owner or admin creates a project
from a dialog and changes its name and instructions in a sheet. Removal and file removal ask first and are reached from
icon buttons. A member sees no edit or more action and can still open a project, start a conversation with a bot, and
move a conversation they own. The server still decides each request. The sheet and dialogs are bound both ways to the
view model, so a refusal keeps them open with the reason; the named view model tests cover drafts after a failed save.
Project file downloads use `DroverApi` through `IFileDelivery` on desktop and Android and fall back to `Launcher` only
in the browser target. Devices without file delivery do not offer a download action.
Upload size checks read the server's limit from `/files/rules`, and
movable conversations are fetched in one request for the signed-in owner. The list keeps the existing limit of
200 non-archived threads, including scheduled and handed work. `scripts/check-native-browser.mjs` checks
every project state, the new project dialog and Cancel, the edit sheet and Discard, the move in and move out flow,
keyboard text, and the five-item `PhoneNav` dock at 360 pixels.

The share screen (`ShareToPage`, `ShareToViewModel`) reads what is waiting in the one process-wide `ShareInbox.Process`, names the
chosen bot there with `SendTo`, and opens that bot's conversation; the bot's main `ConversationViewModel` takes the
share with `TakeFor` when it opens, as the web client's `ConversationPane` does. The usage screen's crew is a table
at desktop width, with the web's columns, order, and hidden caption, and a block for each bot below the phone
breakpoint. With no table semantics on this platform, each figure carries its column's name for assistive
technology (`Tokens in: 100 on its own`), and the drawn column names and phone labels are left out of what is read.

### The conversation workspace

`TalkPage` is native. Its logic is in `src/Drover.Presentation/ViewModels/Chat/`, moved from the web client's
`Talk` page and `ConversationPane` with the same calls, messages, and failure handling (the order of independent reads differs, below):

| View model | Holds |
| --- | --- |
| `TalkViewModel` | Everything read from the address (`TalkAddress`): what is beside the conversation, which of crew, main, and side a narrow window shows, the held takeover, the moment, the title with the waiting count. The space, the threads, the signal stream, the screen probe. It makes and ends the conversations on screen |
| `ConversationViewModel` | One conversation: load, resume and watch a run, send, stop, retry, edit the last message, ask for another reply, show another version, decide, forget and restore, attach, take what was shared, mark seen, and what it tells the screen around it |
| `ComposerViewModel`, `MessageListViewModel` | The field and its actions; the messages, what is read out, the moment to land on |
| `ReplyUsageViewModel`, `BotNotifyMenuViewModel` | What a reply used; the menu about a bot |
| `ChatRows` | What a row says and how it is shaped, apart from drawing: a bot's row in the crew, a message, a tool call's card, the tray of files |

Opening reads what does not depend on something else at the same time: the model connections and the bots, then
every bot's threads; the conversation starts loading as soon as the bots are known, beside the question of whether the
bot has a screen; a conversation's messages and its tool calls are read together; and the conversation is shown before
the server is told it was seen. Until the conversation has been read the screen says it is loading, never that nothing
was said. If the read fails, the screen says so in place of the messages and offers "Try again"; it says nothing into
which a person can write until the read succeeds.

A run is still the server's: these only watch it and ask. Three seams replace what the web client took from its
framework:

- **Drawing.** A view model says it changed through `IUiThread`, the interface's thread. Where the web client called
  `StateHasChanged`, the view model works out what it shows once and raises one change. The views draw once for
  every change since they last drew.
- **Conversations that outlive an address.** `TalkViewModel` keeps a conversation for as long as what it was made
  for is on screen: the main one for its bot, a side chat for its key (a new side chat keeps its conversation when
  its first message gives it a thread and an address), handed work for its thread. These are the web client's
  `@key` rules, and `TalkViewModelTests` hold them.
- **Files.** `IPickedFile` is a file a person picked, pasted, dropped, or shared: a name, a size, and a way to read
  it. `ShareInbox` moved to `Drover.Presentation` and holds picked files; the browser's files and Android's staged
  files implement the interface. Android document providers
  can report no size or a stale size. The Android composer reads the bytes within the server's file limit before the
  HTTP handler sends the request, so the request's length matches the bytes it carries.

The conversation's "More" menu ends with "Save conversation as Markdown" and "Save conversation as JSON". The server
writes the file (`ConversationSaver` asks for it through `DroverApi.ExportConversation`), and the platform saves it
the way the extension editor's download does (`PlatformFiles.SaveText`): the system's save dialog on desktop and
Android, a download in a browser. A toast says it was saved or why not; a cancelled dialog says nothing.

When panels sit side by side, drag either divider to change the crew or right panel width. With a divider focused,
Left and Right move it in 16-pixel steps. The workspace keeps those widths while you switch conversations and
panels, and limits them to leave room for the conversation. The header holds one menu, at every width, for everything
that opens beside the conversation; the item of what is open says so (its item status is "Open").

Escape anywhere inside an open rail closes it, as its close control does, and the focus goes back to the way that
opened it: the bot menu in the conversation's header, which holds every way in (handed work opens from its card,
and the menu is the nearest control for it too). A key a dialog or a menu inside the rail takes for itself closes that first, and a composer
holding text or attached files inside the rail keeps the key so a draft is never dropped. Non-composer fields inside
the native rail do not count as drafts, so Escape still closes the rail from them.
`TalkViewModel.CloseRailFromKeyboard` is the one path, so a device's Back that closes a rail can call it too. The web
client does the same in `Talk.razor.js`.

The page keeps the workspace while the app moves between this screen's addresses. A change of query or fragment
goes through the shell's `StaysOpenWhenQueryChanges`. A change of path makes a new page, as for any screen, and the
new page takes the workspace from the old one, with its conversations, what was written, and where the reader had
scrolled to. Leaving for another screen ends it: the streams stop and unsent files are removed.

The controls are in `src/Drover.Native/Chat/`: `TalkWorkspace`, `ConversationPane`, `MessageList`, `ChatEntry`,
`Composer`, `AttachmentTray`, `CrewList`, `SideChatList`, `FailureLine`, `AttentionNote`, `ReplyUsage`,
`BotNotifyMenu`, `ConversationSearch`.

`ConversationSearch` sits above the crew, with a "Mine only" switch under the box (`Switch`, with its hint) that leaves out what other people shared and searches again from the newest messages. Enter searches the person's conversations on the server
(`ConversationSearchViewModel`), and while a search is shown it stands in place of the crew: the messages found, or a
state that says nothing matched or the search failed, each with its one next step. Escape or the close button clears
it and puts the crew back. Each result is a link to the message's moment (`Addresses.Moment`), so it opens the main
conversation or the side chat scrolled to that message, the same way a link to a moment from memory or files does. A search looks at
the newest 2,000 messages of the conversations the person may read, their own and the ones shared with the space, and the screen says so: the status line names how far back it looked,
and the nothing-found state says no message in that many was a match rather than that none exists. While older
messages remain, a "Search older messages" button runs the same words over the next 2,000 and adds what it finds to the
list. Once every message has been looked at, the screen stops mentioning a limit.

`Keyed.Sync` keeps a panel's children in step with a keyed list, so a message, a card, or a chip
is kept and told what changed, never made again. A new child is put in at its place and only a kept child the list
moved is taken out, so a child that holds the focus keeps it. The activity timeline keeps its nodes and their open
details by event the same way, so a node or a control in its details keeps the focus while a live run's events arrive.

**The message list is not virtualized, on purpose.** Every message is an element, kept by its key, and only a
message that changed is looked at again. That gives what the web client's reversed column gives: the reader at the
foot stays at the foot while text streams, the reader who scrolled up stays put, a link to a moment can land on any
message, an open card stays open, and assistive technology has the whole conversation. Measured on the desktop
build with sample replies (bold, inline code, a list), on a machine whose four cores were loaded two to three times
over by other builds: 300 messages took 2.2 s to first show and 22 ms for each update of a streaming reply (median
of 40); 600 messages took 2.8 s and 26 ms. Before unchanged messages were skipped the update took 36 ms at 300. If
conversations of thousands of messages become common, load the older part on demand; do not recycle elements.

A streaming reply is parsed and laid out again for each stretch of text that arrives, as the web client does. The
list draws at most once in a quarter of the shortest duration, and after what the person is doing, so a reply
picked up again, whose events arrive all at once, does not hold the screen. New characters of the last paragraph
fade in over the shortest duration; with reduced motion they are simply there.

Known differences of this screen from the web client:

| Area | Difference |
| --- | --- |
| Arrow keys | The crew and the side chats also move the focus with the arrow keys, Home, and End. The web client has Tab alone |
| Escape with text in non-composer rail fields | In the native client only a non-empty `Composer` (or composer attachments) keeps Escape from closing the rail; in the web client (`Talk.razor.js`), any non-empty field inside the rail keeps the key |
| Pasting a picture on a desktop | A picture with no text and no file behind it (a screenshot) is read, checked and attached as `Pasted image <date> <time>.png`, by the local clock. Windows and macOS hand it over as a bitmap (`GetBitmapAsync` in Uno 7.0.65); its stream is read by `PastedPicture.Read`, which never asks for more than 64 MiB and refuses a stream that holds more, whatever size it said. A Linux desktop (X11, and Wayland through it) has no bitmap; it lists the picture under its type, and the client reads it by that name (`image/png` first). The platform reads those bytes whole before the client sees them, so there the 64 MiB limit keeps a larger picture from being decoded and sent, not from being read. Either way the picture must be one still picture of at most 32 million pixels: a PNG is kept as it is, any other format is written again as PNG. The server's file limit then refuses a large one on its chip. Text on the clipboard wins and nothing is attached. A clipboard that cannot be read says "What was pasted could not be attached."; text and an empty clipboard say nothing. Android in Uno 7.0.65 reports text, HTML and links only, so a picture cannot be pasted there; a picture format a device lists and does not hand over is said in a toast. The limits and the sentences are unit tested (`PastedPictureTests`, `ChatViewModelTests`). Not verified here: a real clipboard on Windows, macOS, a Linux desktop or Android. What each platform hands over was found by reading the Uno packages, not by running them |
| Pasting files in a browser | The page's own `paste` event carries files and pictures (`Platforms/WebAssembly/WasmScripts/Paste.js`, called through `[JSImport]`). While the event runs, before anything is read, the app is asked which field takes it: the one with the cursor, while its conversation takes files. The files go to that conversation however long they take to read, and to none if it stops taking files. The first 100 files are read, and only while the bytes held for every paste being read stay within 128 MiB; the rest are counted, not read, and the person is told. A file that cannot be read fails the paste once, and every byte held for it is let go. A file keeps its own name, even `image.png`. A browser does not say whether a file came from storage, so a file named `image.<type>` and made at the moment of the paste is taken for a picture the browser made, and named for when it was pasted; one changed in the five seconds before it was copied would be named so too. Exercised in `scripts/check-native-browser.mjs` against the trimmed publish under the server's content security policy, in headless Chromium with script-made paste events: the mixed paste left to the field, the picture uploaded byte for byte with its chip, a copied `image.png` keeping its name, a paste still arriving after the cursor left, a failed read said once with nothing held, and 150 files cut to 100. Not verified here: a real clipboard, and browsers other than Chromium |
| Dropping files | Dropped on the conversation pane (messages and composer), not on the composer alone: the composer's frame takes the ember edge and shows "Drop the files to attach them." while files are held over it. Hover and release make the same check: the drop carries files (storage items) and the conversation takes files now (`ConversationViewModel.AcceptsFiles`: not read-only, missing, still loading, failed to load, or left). Anything else is refused, the cursor says so, and the line goes. A drop reads storage items only, never a picture with no file behind it. A drop of folders alone says folders cannot be attached. Exercised in a browser with script-made drag events over the message list: the line shown, gone on leave and on drop, words refused, one upload. Not verified here: a person dragging from a file manager on Windows, macOS, a Linux desktop or Android |
| Why a file was not taken | Each failure is said once in a toast (`ComposerViewModel.Notify`, sentences in `FileWords`): the chooser could not open, a paste or a drop could not be read, only the first 100 files were taken, only folders were given, or the device did not hand over a picture. A cancelled chooser says nothing. The log lines stay. A conversation that does not take files offers no Attach and takes no paste or drop, and a share from another app waits until the conversation can take it |
| File chooser | The platform's own. The first 100 files chosen are read; the rest are counted, not read. A chooser that fails shows a toast. Not verified here: the chooser on a desktop or a phone, which the virtual display does not run |
| Enter while composing with an input method | The web client leaves Enter to the input method. The platform does not say that a composition is under way |
| A bot's name in the header | In a wide header a long name is not cut short with an ellipsis; it is in a wrapped or phone header |
| The line along the composer | Under the virtual display it was drawn with gaps. It is the shared `TravelLine` |
| Failure heading | Wraps greedily, as every heading here does |

#### Needs a manual check: pasting and dropping files

None of these was run where the native client was verified; each needs a person at the device.

- Windows, macOS and one Linux desktop: copy a screenshot, press Ctrl+V or Cmd+V in the message field. One chip named
  `Pasted image <date> <time>.png` appears, it uploads, and Send sends it. With text and a picture copied together,
  the text is pasted and no chip appears.
- The same desktops: hold a file over the message list. The composer's frame takes the ember edge and shows the drop
  line; moving away removes it; dropping attaches the file. Over a bot's side of handed work, or a conversation still
  loading, the cursor refuses the drop.
- The same desktops: drop a folder alone, and see "Folders cannot be attached. Attach the files inside."
- Android: Attach behind "More options" opens the picker, and a picker that fails shows its toast. Pasting a picture
  is not expected to work there.
- A real clipboard in a browser other than Chromium: a screenshot is named for when it was pasted, and a copied
  `image.png` keeps its name.
- Screenshots of the pasted picture's chip and the drop line on a desktop, dark, light and 360 px wide. The browser
  check saves the same two states of the browser build as `pasted-picture-<width>-<theme>.png` and
  `drop-cue-<width>-<theme>.png`.

### The sandbox screen

`BotSandboxPage` and the control `SandboxScreen` (`src/Drover.Native/Bots/`) are native. What they say and do is in
`BotSandboxViewModel` and `SandboxScreenViewModel` (`src/Drover.Presentation/ViewModels/Bots/`), moved from the web
client's `BotSandbox.razor.cs` and `SandboxScreen.razor.cs` with the same calls, order, and sentences.

- **What the screen speaks.** Nothing but the API every screen uses. The picture is one PNG per request,
  `POST .../sandbox/screen?wake=false`, asked for every two seconds while the screen shows, the sandbox runs, and the
  window is visible, later after each failure, and 300 ms after anything a person sends. Input is one JSON
  `SandboxInputRequest` per action to `POST .../sandbox/input`. There is no stream and no socket, in the web client
  or here, so there is nothing to connect or reconnect but the next request. Both go through `DroverApi`, so the
  antiforgery token, the session, and the HTTPS-only rule of a device apply as they do everywhere. The server decides
  who may see or drive a screen; the client draws what came and sends what the person did.
- **The picture** is drawn with Skia (`PictureLayer`, an `SKCanvasElement`) from the PNG bytes, decoded once per
  picture, inside `SandboxPicture`, a frame that keeps the screen's shape. The zoom is a transform on the layer, so an
  enlarged picture is drawn again at its size and stays sharp. The ring of the keyboard pointer is drawn on the same
  canvas.
- **What the web client's script did** is now `ScreenGesture` (pinch, drag, and what counts as a tap; framework
  free and tested), pointer and key handlers in `SandboxPicture`, the window's `VisibilityChanged`, and the
  platform's `InputPane` for the on-screen keyboard.
- **The touch model in control** is `ScreenGesture` too (issue 798), started with `inControl: true` and the time of
  each event, so it reads no clock. A tap is a click and a long press (`LongPressTime`, 600 ms, still) a right
  click, both when the finger lifts. A move past `TapSlop` (10 px) drags with the left button: `Press` where the
  finger went down, `Drag` for each new place, `Release` where it last was. On an enlarged picture a quick move pans
  the view, and a move after `HoldTime` (300 ms) drags. Two fingers moving together turn the wheel, one step for
  each `ScrollStep` (24 px), up and down only as the sandbox scrolls; two fingers whose spread changes by
  `PinchShare` (12 percent) pinch the view. A second finger during a drag releases the button and ends the gesture.
  A cancelled finger releases a held button; `Release()` does the same when the focus or control goes. What the
  sandbox is sent is handed over by `TakeInput()` as `ScreenTouch` values at places on the frame. Watching, it does
  what it always did. `ScreenGestureTests` holds every transition. The picture uses it in the second layer of issue
  798, with the pointer kinds of issue 797.
- **The keyboard.** While the person drives the screen the picture is one tab stop: the arrow keys move the pointer
  (with Shift, far), Enter or Space clicks there. Enlarged and only looked at, it is a tab stop whose arrow keys
  move the view. Tab and Shift+Tab are never taken, so the focus enters and leaves the picture as it does a button.
- **`SandboxScreen`** has the web component's parameters as dependency properties (`BotId`, `BotName`, `Status`,
  `Held`) and its callback as the event `SandboxChanged`. It makes its own view model, refreshes while it is in the
  tree, and stops when it leaves it.
- **A screen that stays open.** Choosing a tab changes the address (`?tab=terminal`). A screen that overrides
  `Screen.StaysOpenWhenQueryChanges` is told the new address through `Opened` instead of being made again, as a
  Razor page is given new parameters. `BotSandboxPage` is the one screen that asks for it.

Tried on Linux under X11 against a stand-in for the server that speaks the same addresses and answers the screen
with a test pattern (pictures in `docs/screenshots/native-sandbox/`): pictures drawn and refreshed, a click mapped
to the sandbox's pixels at fit and enlarged, the keyboard pointer, Tab into and out of the picture, typed text, key
buttons, key combinations, scroll, right click, zoom buttons, drag of the enlarged picture, a failing screen and its
recovery, start from a paused sandbox, the terminal, pause, and the dialogs. Not tried: a real sandbox provider,
touch (pinch, a finger on the picture while the page scrolls, the on-screen keyboard), a screen reader, the browser
and Android builds.

Differences from the web client: the text field does not ask the on-screen keyboard for a "send" key, which the
platform's text box cannot; the level between the zoom buttons is announced politely as the web's is, but the
buttons of a wrapped zoom row start at the left; a focused button that is switched off while an input is sent gets
the focus back when it is on again, where a browser never takes it away.

The browser build is served at `/` from the server's web root (or the folder `NativeClient:Path` names) by
`src/Drover.Server/Clients/NativeClientFiles.cs`, with every non-file address falling back to its `index.html`.
A missing file is a 404, never the page; the API is never answered with the page. `/native/` is the retired address
(see "The address it was served under before"). Manifest files are JSON; the build's Brotli and gzip copies are served to a browser that accepts them, with the
file's own type and `Vary: Accept-Encoding`; content-named framework files and content-named native package
directories can be cached immutably; entry files are revalidated. The CSP is unchanged. Verified on 2026-11-18,
while the build was served under `/native/`, against a real server and PostgreSQL: `/native/sign-in` loaded in Chromium with no policy violation and no console
error but the expected `401` for the session and `404` for closed setup; signing in through the drawn form sent the
antiforgery header, set only `HttpOnly` session cookies (plus the readable `XSRF-TOKEN`), left local storage empty,
and showed the workspace. The image always includes the browser client, in Docker Compose and CapRover; the
`DROVER_BUILD_NATIVE` switch is gone.
The build installs `python3` in the build stage for the WebAssembly linker (`emcc`). The Compose workflow
builds the image, checks the BuildKit container's effective 2 GiB memory limit and disabled swap, and starts it.
The Docker publish caps the managed heap, skips the optional XAML resource-trimming pass, uses one Emscripten
and Binaryen worker, and links at `-O1`. Managed assembly trimming stays enabled.
The workflow also checks the entry page, a deep link, the manifest, the service worker, and the redirect from
`/native/` through Drover's server.

`.github/workflows/native.yml` builds Android on hosted Linux, iOS on hosted macOS, and desktop on hosted Linux,
Windows and macOS. It also publishes the browser head. The iOS job builds for a simulator with warnings as errors,
runs the shared screen checks, and relaunches the installed app to verify its saved sign-in. CI uploads a
development-signed Android APK and the browser publish as artifacts. Production iOS signing, packaging, and real
provider flows remain to be verified. Incoming links, tool sign-in returns, UnifiedPush
registration and notifications, and shares in are built on Android from the retired shell's code and compile in the
APK (its manifest was read back: receivers, the file provider, the return scheme, the share targets), but were not
run on a device. `ConnectViewModel` still
lives in the native app and lacks unit tests. A desktop has no system share target or link scheme, so nothing
comes into the desktop app from outside it.

Screenshots in `docs/screenshots/native-parity-pass` document the earlier setup and management pass. The browser
workflow captures the published client with sample data. It includes 360 and 1280 px, dark and light,
sign-in and setup, management, conversation, editing a bot, the sandbox terminal, and sharing with an empty inbox.
Complete state coverage, 200 percent zoom, keyboard navigation, and screen-reader verification remain open.

### Verification recorded for the setup and management pass

- Main solution Release build: zero warnings and errors. Both main and native format checks passed.
- Linux desktop Release build and browser Release publish passed. Linux runtime startup did **not** pass (see below). Android Release compiled and produced a
  development-signed APK for ARM64 and x64; the APK manifest keeps backup and cleartext traffic disabled.
- Presentation tests pass (187), including server-returned review hashes, conflicts, tickets, and device launch refusal.
- The expanded full run had 3,932 passing tests, three failures, and 25 explicit live-provider cases excluded.
  The unchanged engine retry, web-push, and password-reset query tests each passed on isolated rerun with builds
  stopped, without assertion changes. The password-reset failure had captured an unrelated run-queue poll.
  This is recorded as an intermittent full-run failure, not a clean full-run sign-off. A preceding full run before
  the final tool-screen additions passed 3,929 tests with 25 live-provider cases excluded.
- The browser smoke check exercises the trimmed publish under the exact server CSP, actual accessible headings,
  tool add/review states, extension loading/failure, both themes and widths, and downloaded JSON bytes. Reference
  captures use the same fixtures in the existing client. Screenshots are regression evidence, not parity approval.
- Native package metadata and exact-version license corrections pass the license checker, and NuGet's current
  advisory feed reports no vulnerable packages in the resolved native graph. Actionlint, Zizmor and the staged
  secret scan passed. Hadolint reports only the existing informational use of `USER $APP_UID`.

### Integration pass against a running server

Every screen and state was captured in both clients against one real server with a stand-in streaming model:
`docs/screenshots/native-integration/` (pictures, the differences scene by scene, and how they were taken). The
pass fixed what it found in the native port: the memory and files rails were empty in a trimmed build (a generic
`Make<T>` without the trimming annotation), Enter in a field or the composer could send before the last letters
arrived, a text file's card took its width from its neighbour, and several settings screens' layouts. Still open,
in order of how much they stand in the way of retiring the web client:

Tool and delegation cards appeared as empty frames for seconds before they were drawn, and a waiting call showed only
Approve and Deny: each card queued its first draw behind the conversation's other work. They are now drawn before
they are first measured (see "The conversation's pieces").

Opening a conversation took about 10 s in the browser build and showed "Nothing said yet" on the way. The
conversation was made once, as `TalkViewModelTests` now holds; the empty words came from the message list, which
was shown before it had drawn the loaded messages, and which said a conversation still being read had nothing in it.
The list now says nothing about an empty conversation until it has been read, and is drawn before it is shown. The
reads that do not depend on each other are made together (see "The conversation workspace"). Measured on 2026-11-19
against a Release server with twelve messages: on the desktop build, moving to the conversation from another screen
took 136 to 150 ms (five runs; 160 to 181 ms before), and the first conversation after start 244 to 264 ms (480 ms
before), against 239 to 262 ms for the same move in the web client. The browser build's figure is in the limits table.

Links measured without a role or name in this pass; the browser adapter merged alongside it now gives them the
link role (see the limits table).

1. Sign-in has autocomplete names but no `form` and no `type="email"` (see the limits table).
2. Layout differences listed in the pictures' README (usage table columns, rail buttons wrapping, focus on
   sign-in at load).

Interaction smoke on the browser build (Playwright, through the semantic tree and the keyboard): signing in with
Enter, sending with Enter and watching the reply stream, Stop, Try again on a failed run, Approve on a waiting tool
call, opening each rail and closing it with its close button, making a side chat from `/side/new`, browser back
and forward, back after opening a rail, a deep link straight to each rail, and Tab, Shift+Tab, Enter through the main
flows all worked. Reload on a rail's deep link failed before the memory rail was fixed and was not run again after.
Escape closes an open rail in both clients and returns the focus to the way that opened it (see Escape above). In the crew, the arrow keys did not move the focus in the browser
(they do on desktop), and Tab order passes through the page body between some controls.

### Linux desktop startup

The original Release desktop app created a native window under Xorg but failed before its first frame:
`SKTypeface.GetTableTags()` threw
`Unable to read the tables for the file`, reached through `SkiaFontProvider.GetDefaultFont` during text measure.
A separate probe using the same SkiaSharp 4.156.0 packages reproduced an empty default typeface, while an
explicit `DejaVu Sans` typeface and its TrueType file both loaded and exposed their tables. This also reproduced
with a private fontconfig containing only valid DejaVu fonts, after excluding the host's WOFF2 defaults.

The pinned Uno [fallback source](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/src/Uno.UI.Composition.Skia/Composition/Uno/Drawing/SkiaFontProvider.cs#L197)
constructs a font face from the default without rejecting an empty typeface. Its
[font-loading path](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/src/Uno.UI/UI/Xaml/Documents/TextFormatting/FontDetailsCache.cs#L176)
uses that fallback while file fonts load asynchronously. The Linux head uses Uno's font-provider registration to
supply bundled Inter for that default. Font file loading, named families, and glyph fallback continue through the
Skia provider. The desktop smoke check opens the Release app with fresh settings, waits for a mapped connection
window, and checks that pixels have been drawn in it.

### Retirement gate

Each production route has an implemented Uno screen. The published browser smoke check covers the primary
screens, including conversation, bot editing, the sandbox terminal and the empty share state, at 360 and 1280
pixels in both themes. Until the retirement it also ran the same fixtures against the Blazor client for reference.

This gate said to keep the existing Blazor and MAUI hosts while the remaining verification was open. The owner
decided otherwise: the hosts were retired on 2026-11-20, with that verification still open. Each item stays an
open follow-up against the Uno client:

- device sharing;
- push and OAuth returns;
- Windows and macOS runtime checks;
- browser and device assistive technology;
- IME input;
- long conversations;
- the sandbox with a real provider;
- the production release and upgrade paths.

## Upstream grounding

Checked on 2026-11-18 against `unoplatform/uno` commit
`6405b51d91625198d9a2b5348cb7eb0bd9015284` and the `7.0.65` tag
(`30034eefe42ecf5f3e3fff7d49a05340aaa0152e`). The resolved UI packages are 7.0.65 under Uno.Sdk 7.0.22. GitHub’s latest-release endpoint on that date also reports 7.0.65 (published 2026-11-13); the accessibility startup source is unchanged between that tag and the checked upstream commit.

- [.NET 11 support](https://github.com/unoplatform/uno/blob/6405b51d91625198d9a2b5348cb7eb0bd9015284/doc/articles/migrating-from-net10-to-net11.md).
- [Skia rendering and removal of Mac Catalyst](https://github.com/unoplatform/uno/blob/6405b51d91625198d9a2b5348cb7eb0bd9015284/doc/articles/migrating-to-uno-7/project-setup.md).
- [Accessibility support matrix](https://github.com/unoplatform/uno/blob/6405b51d91625198d9a2b5348cb7eb0bd9015284/doc/articles/features/accessibility/index.md): Android and iOS Skia accessibility are listed as work in progress.
- [Accessibility activation and its cost](https://github.com/unoplatform/uno/blob/6405b51d91625198d9a2b5348cb7eb0bd9015284/doc/articles/features/accessibility/testing-with-screen-readers.md).
- [File-picker behavior and platform limitations](https://github.com/unoplatform/uno/blob/6405b51d91625198d9a2b5348cb7eb0bd9015284/doc/articles/features/windows-storage-pickers.md).

These upstream statements are constraints to test against. They do not establish that Drover's controls or
platform integrations have met them. Historical observations earlier in this document are not a current parity
sign-off.

## Size levers

Measured on Release publishes of the browser build; "Brotli" is the sum a browser fetches when every file is
served as its Brotli copy.

| Lever | Result |
| --- | --- |
| Serving the build's own Brotli copies | First load 44.1 MB to 12.1 MB on the wire. Kept (`PrecompressedFiles`) |
| Trimming | Already `TrimMode=full` in a Release publish of this SDK |
| IL stripping (`WasmStripILAfterAOT`) | Applies only with ahead-of-time compilation, which is off |
| XAML resource trimming (`UnoXamlResourcesTrimming`) | Files 50.5 to 46.2 MB, Brotli 15.3 to 14.5 MB, first load 12.1 to 11.2 MB (Uno.UI 2.12 to 1.58 MB, Uno.UI.FluentTheme 0.52 to 0.34 MB). Kept, for a Release build. Checked: sign-in, the workspace, bots, extensions, models, tools, usage and status drew with no console error and no policy violation |
| Lazy assets | Fonts are fetched only when drawn: the 12 MB of Open Sans the platform ships are never fetched by Drover, whose text is Inter; the platform's symbol font (0.23 MB) is |
| Bundled color emoji font | The modified Noto Color Emoji file is 5,044,436 bytes after its unused 20 MB OpenType SVG table was removed. It ships in every target's assets but is kept out of the worker's shell list, so a first visit does not fetch or store it; it will be fetched when drawing lands in a later layer. No Release publish measurement was made for this layer, because the font is not in the first-load path |
