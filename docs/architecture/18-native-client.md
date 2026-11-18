# 18. The native client

One codebase that draws Drover's interface natively on the web, on Android, and on desktop. It is the foundation of the move away from the Razor component library and its two hosts. The decision is [ADR 0015](../adr/0015-one-native-client.md). This section is the working manual: a person or an agent moving a screen should need nothing else.

The web client (`Drover.Ui`, `Drover.Web`) and the Android shell (`Drover.App`) still build, still pass their tests, and are still what ships. They are retired screen by screen, not in one step.

## What it is

- `src/Drover.Native` is one Uno Platform project (Uno.Sdk 7.0.22, Apache-2.0). Only the open source platform is used. The design tools and the app inspection server that the SDK offers in development builds are switched off in the project file (`UnoDisableHotDesign`, `UnoDisableMCPSupport`).
- Every target is drawn by the Skia renderer, so one set of controls looks the same everywhere. Nothing is an HTML element: in a browser the app is a canvas, with a hidden tree of elements beside it for assistive technology.
- The interface logic that does not depend on a UI framework lives in `src/Drover.Presentation`, a plain class library that both clients reference. Words, drafts, tool cards, timelines, bot looks, icon shapes, the markdown node model, session state, and the toast service moved there unchanged, under the `Drover.Ui.*` namespaces they always had. View models and the router are new code there, under `Drover.Presentation.*`.

## Projects

| Project | Kind | Holds |
| --- | --- | --- |
| `src/Drover.Presentation` | `net11.0` library, in `Drover.slnx` | The moved framework-free code (`Drover.Ui.*`), view models (`Drover.Presentation.ViewModels`), the router (`Drover.Presentation.Routing`), the readers for drawings kept as data (`Drover.Presentation.Drawing`), `IDeviceSettings` |
| `src/Drover.Native` | Uno single project, in `Drover.Native.slnx` | The app: tokens, controls, layouts, shell, pages, platform code |
| `src/Shared/Transport` | Source files, linked | `AntiforgeryHandler` (compiled into `Drover.Web` and the native browser build); `AppRequests`, `NativeSession`, `NativeSessionTokens`, `ServerAddress` (compiled into `Drover.App` and the native device builds) |
| `src/Shared/Web` | Browser files, linked | `boot.js`, `cookies.js`, `push.js`, `push-worker.js`, `service-worker.published.js`, the icons: the web client's own, served by `Drover.Web` at the root and by the native browser build under `/native/` |
| `src/Shared/Android` | Source files, linked | The Android shell's UnifiedPush registration, transport, receivers and delivery service, notifications, shares in (`AndroidShares`), files out (`AndroidFileDelivery`, `DocumentDestination`), and `IncomingIntents`; compiled into `Drover.App` and the native Android build |
| `src/Shared/Chat` | Source file, linked | `ShareInbox`, compiled into `Drover.Ui` (with Blazor's `IBrowserFile`) and the native app (with `Shell/IBrowserFile.cs`, the same shape) |
| `tests/Drover.Presentation.Tests` | xunit, in `Drover.slnx` | Router, view models, token parity and contrast, the interface rules for native source |

`Drover.Native` has its own solution file because it needs workloads the main solution's build does not have (`wasm-tools`, Android), and because the Android preview workflow formats and license-checks everything in `Drover.Apps.slnx` without restoring it.

```
src/Drover.Native/
  Drover.Native.csproj     targets, switches, linked transport
  App.xaml(.cs)            resources, start, restart
  Theme/                   Tokens.xaml, Styles.xaml, and their readers (Tokens, Styles, Motion, Targets, Pill, Themed)
  Controls/                one class per component
  Layouts/                 EntryLayout, WorkLayout, PhoneNav, NavLink
  Session/                 SessionGate, AccountMenu, ServerChoice
  Shell/                   AppServices, Screens, ShellView, Screen, PendingScreen
  Pages/                   one class per screen, in the folders the web client uses
  Device/                  compiled for desktop and phones only: server choice, settings, credential store
  Platforms/WebAssembly/   compiled for the browser only: storage, history, cookies, the eval table
  Platforms/Desktop/       Program.cs
  Platforms/Android/       application, activity, manifest
  Assets/Fonts/            Inter and JetBrains Mono, with their licenses
```

## Platform matrix

| Target | Framework | State |
| --- | --- | --- |
| Web | `net11.0-browserwasm` | Builds, publishes, and runs in Chromium under the server's security headers |
| Desktop: Linux, macOS, Windows | `net11.0-desktop` | Linux Release builds; startup under a virtual X11 display fails in the Skia font fallback before the first frame. macOS and Windows not run |
| Android | `net11.0-android` | Release build and signed development APK verified with SDK 37 and Java 21; no emulator or device run |
| iOS | `net11.0-ios` | Enabled by `DroverEnableApple`. Not built; startup and device integration remain unverified |

Uno 7 removed Mac Catalyst. macOS uses the desktop head. This is confirmed by the upstream [migration guide](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/doc/articles/migrating-to-uno-7/project-setup.md#mac-catalyst-is-removed).

## Commands

```bash
# Build one target (-warnaserror holds the same rules as the rest of Drover)
dotnet build src/Drover.Native -f net11.0-desktop -p:DroverNativeTargetFrameworks=net11.0-desktop -warnaserror
dotnet build src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm -warnaserror     # needs: dotnet workload install wasm-tools
dotnet build src/Drover.Native -f net11.0-android -p:DroverNativeTargetFrameworks=net11.0-android -warnaserror         # needs the Android SDK

# Run on desktop
dotnet run --project src/Drover.Native -f net11.0-desktop -p:DroverNativeTargetFrameworks=net11.0-desktop

# Run in a browser, development build, at http://127.0.0.1:5199/ (the gallery is at /dev/gallery)
ASPNETCORE_URLS=http://127.0.0.1:5199 dotnet run --project src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm

# Publish the browser build. It expects to be served under /native/.
dotnet publish src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm -c Release -o <folder>
# The same, with the gallery in it, for the parity bench
dotnet publish src/Drover.Native -f net11.0-browserwasm -p:DroverNativeTargetFrameworks=net11.0-browserwasm -c Release -p:DroverShowGallery=true -o <folder>

# Tests for everything that is not drawing
dotnet test --project tests/Drover.Presentation.Tests
```

Taking a screenshot of the browser build: serve the published `wwwroot` under `/native/` with any static host that falls back to `index.html`, open it in Playwright, wait until the element with class `uno-loader` is gone, then capture. The app is one canvas, so Playwright's full-page capture gives one viewport: use a tall viewport, or send wheel events of about 1300 pixels and capture again. A development build takes about fifteen seconds to start in headless Chromium; a published build about three.

Taking a screenshot of the desktop build on a machine with no display: `xvfb-run -a --server-args="-screen 0 1280x800x24"` around a script that starts `bin/Debug/net11.0-desktop/Drover.Native.dll`, waits, and runs `import -window root out.png`.

The pictures taken for this work are in `docs/screenshots/native-foundation/`, native beside web, at 1280 and 360 pixels, dark and light.

## Tokens

`Theme/Tokens.xaml` is the only XAML file that holds raw values. It carries every token of `src/Drover.Ui/wwwroot/tokens.css`, and `TokenParityTests` fails when the two files disagree. The web file stays the specification: a new token is added there first.

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

`Theme/Styles.xaml` holds what is derived from tokens: the text styles, the line heights (size times leading), the layout breakpoints (`DvBreakPhone` 560, `DvBreakEntry` 600, `DvBreakSheet` 640, which the web writes inside media queries), and the control templates. It is the only other file where a number may appear.

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

`ThemeModel` (in `Drover.Presentation`) holds the choice. The app is dark until a person picks light or asks it to follow the device. The choice is kept through `IDeviceSettings` under the name `drover.theme`: in a browser that is the same local storage entry the web client writes, so the two clients agree; elsewhere it is the platform's settings store. `ShellView` applies it by setting `RequestedTheme` on the root.

## Controls

Every component of `src/Drover.Ui/Components` has a control of the same name in `src/Drover.Native/Controls`, with the same parameters as dependency properties. Where a name could not be kept, the table says why.

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
| `Field` | `Label`, `Value` (two-way), `Type`, `Placeholder`, `Hint`, `Error`, `Disabled`, the `Submitted` event |
| `Icon` | `NameOfIcon` (the web's `Name`), `Size`, `Foreground`. Drawn from `IconShapes` |
| `IconButton` | `Icon`, `Label` (required: it throws without one) |
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
| `TextArea` | `Label`, `Value` (two-way), `Rows`, `Placeholder`, `Hint`, `Machine`, `Disabled` |
| `Timeline` | `Model`, `Label`, `EmptyText`, `AsList`, `PageSize`, `Detail` |
| `ToastHost` | `Model`. The shell has the one instance |
| `Tooltip` | `Text`, `Described` |

Native only, because the web does these in CSS:

| Control | What it is |
| --- | --- |
| `Facet` | The faceted frame: `Cut`, `Fill`, `Edge`, `Ring`. A panel; what is put in it sits on the surface |
| `Yard` | The dot grid behind the working area |
| `Link` | An anchor: `Text`, `Address`. An in-app address goes through the navigator |
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

## The shell

- **Start.** `App` builds the services (`AppServices.Build()`), starts the platform's side (`PlatformShell.Start`), and shows `ShellView`: a `Frame` for the screen and the `ToastHost` over it.
- **Services.** `Shell/AppServices.cs` is the one place services are registered. `AddPlatform()` is defined once in `Device/PlatformServices.cs` and once in `Platforms/WebAssembly/PlatformServices.cs`, and adds what differs. A view model is not registered: a screen makes its own with `Make<T>()`, which fills the constructor from the services.
- **Addresses.** `Shell/Screens.cs` is the one place a screen is given its address patterns. They are the patterns of the web client's `@page` directives; `NativeScreensTests` fails when the two lists differ. `RouteTable` matches literal segments before ids and ids before free text, and hands the screen a `RouteMatch` with the path parameters, the query, and the fragment.
- **Going somewhere.** Ask the `Navigator`: `navigator.NavigateTo(Addresses.Bots)`, or with `replace: true` to leave no step behind. In XAML use `<dv:Link Address="/bots" Text="..." />`. Never navigate the frame directly.
- **In a browser** the address bar and the browser's history follow the navigator, and the browser's back and forward buttons move the app, so every address is a link that can be opened, reloaded, and shared.
- **Layouts.** A screen's root element is its layout: `EntryLayout` for sign-in and setup, `WorkLayout` for a screen of the workspace. `WorkLayout` holds the `SessionGate`, the bar, and below 560 pixels the dock. Children are stacked in the layout's column.

## Transport and security

Unchanged from the clients this one replaces, because it compiles the same source.

- **In a browser:** the app talks to its own origin. The browser holds the session cookie, which script cannot read. `AntiforgeryHandler` adds the token to every request that changes something. No token is in storage. The only thing in local storage is the theme choice.
- **On a device:** `AppRequests` sends requests only to the HTTPS origin the person chose, follows no redirects, and keeps cookies in memory. `ServerAddress` keeps the address in plain settings. `NativeSession` keeps the sign-in that outlives the app through `ISecureStorage`: on a phone, the platform's credential store; on desktop, nothing is written and the person signs in again next launch.
- **Content security policy.** The published browser build runs under the policy `SecurityHeaders` sends, with nothing added. The platform's runtime evaluates five small expressions as script at startup, which the policy forbids. `Platforms/WebAssembly/WasmScripts/NoEval.js` answers exactly those five with functions and passes anything else to the browser's own `eval`, which the policy refuses. So a new expression fails loudly instead of being allowed. After any SDK upgrade, and after first using a platform feature (clipboard, launcher, file picker, share), run the published build under the policy and read the console.
- **Calling the browser.** Use `[JSImport]` on a browser function, as `Platforms/WebAssembly/Browser.cs` does. Never `WebAssemblyRuntime.InvokeJS`: it is `eval`. A shared script module is imported with `JSHost.ImportAsync` relative to `_framework`, as `PageCookies` (`cookies.js`) and `BrowserPushRegistration` (`push.js`) do.
- **The page and its worker.** The browser build's page is `Platforms/WebAssembly/index.html`; `DroverIndexHtml` in the project file writes its `<base>` (`/native/` when published, `/` in development) and gives it to the platform as its page template. It loads the web client's `boot.js` before anything is drawn: the stored theme is on the page from the first frame (`boot.css` colors the loader in it), and the service worker is registered with the same update behavior as the web client's (a new build takes over at once, and the page reloads unless the person is writing, then when they look away). The worker is the web client's `service-worker.published.js`: `DroverServiceWorker` in the project file puts it in place of the platform's own worker after publish (the platform's PWA switch stays off, so its worker is never registered) and writes `service-worker-assets.js`, the build's files with a SHA-256 of each that the worker stores at install. The worker keeps to its own client: the web client's worker answers nothing under `/native/`, each keeps its own stored shell, and a notification opens the client that registered it (`NativeServiceWorkerTests`). `manifest.webmanifest` makes `/native/` installable as its own app. The loader's picture comes from the app's own package; the platform's default asks another site for one, which the policy refuses.
- **Notifications in a browser** go through the same `push.js` and `push-worker.js` as the web client, behind `IPushRegistration` (`BrowserPushRegistration`). A browser failure is reported as `DeviceCallFailedException`. The development build has no service worker (the platform's own module worker is what a development build serves), so notifications need a published build.
- **On Android** the native build uses the Android shell's code, shared (`src/Shared/Android`): UnifiedPush registration and delivery, notifications with replies, the sign-in return scheme, shares in, and saving and sharing files. The few things that code asks of its host by the shell's framework's names (main thread, current activity, the notification permission, the cache folder, services) are in `Platforms/Android/AndroidShims.cs`; the one share call is behind `ShareSheet`, which each app defines. The application id is `com.forresttechnologies.drover.client`, so it installs beside the preview (`.preview`, ADR 0014), and the server's `ToolSignInOptions.AppSchemes` accepts both. A notification that arrives with no interface running is handled with services made for that work (`IPlatformApplication` in the shims).
- **On a desktop** a conversation file is saved through the system's save dialog (`DesktopFileDelivery`). Sharing puts a copy in a folder of the app's own and opens the folder: the file itself is never opened, because a bot can send a program.

## How to move a screen

A screen is one `.razor` file with a `@code` block or a `.razor.cs`. Moving it makes three files and changes none that are shared.

1. **Read the screen and its style sheet.** Name the screen's job in one sentence. List its states: loading, empty, failed, each kind of content.
2. **Write the view model** in `src/Drover.Presentation/ViewModels/<Screen>ViewModel.cs`. Move the logic of the `@code` block as it is: the same service calls, the same messages, the same order. `[ObservableProperty]` for what the screen shows, `[RelayCommand]` for what it does. It takes `DroverApi`, `SessionState`, `Navigator`, `ToastService`, a logger, in its constructor. It never touches a UI type. Replace `NavigationManager.NavigateTo` with `Navigator.NavigateTo`, `StateHasChanged` with nothing, a route parameter with a method the screen calls (`Open(Guid botId)`).
3. **Write its tests** in `tests/Drover.Presentation.Tests/ViewModels/`. Use `FakeServer` to say what each address answers, as the web client's screen tests do, and port those tests' cases: they are the specification of the screen's behavior.
4. **Write the page.** Replace the placeholder class in `src/Drover.Native/Pages/<Folder>/<Screen>Page.cs` with `<Screen>Page.xaml` and `<Screen>Page.xaml.cs`. Keep the class name, so `Shell/Screens.cs` does not change.

   ```xml
   <shell:Screen x:Class="Drover.Native.Pages.BotListPage"
                 xmlns="http://schemas.microsoft.com/winfx/2006/xaml/presentation"
                 xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
                 xmlns:dv="using:Drover.Native.Controls"
                 xmlns:layouts="using:Drover.Native.Layouts"
                 xmlns:shell="using:Drover.Native.Shell">
     <layouts:WorkLayout>
       <TextBlock Style="{StaticResource DvTextH1}" Text="Bots" />
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

       protected override async void Opened(RouteMatch<Type> route) => await Model.Load();
   }
   ```

   `Pages/SignInPage.xaml` is the worked example. Read parameters from `route` in `Opened`: `route.GuidParameter("BotId")`, `route.QueryValue("section")`.
5. **Translate the style sheet.** Every `var(--dv-x)` becomes its token. `display: flex; flex-direction: column; gap` is a `StackPanel` with `Spacing`. `flex-wrap` is a `FlowPanel`. `class="dv-facet"` is a `Facet`. `@media (max-width: ...)` is a `SizeChanged` handler that reads a breakpoint from `Styles.xaml`. `::deep` rules become properties set on the child.
6. **Add its samples to the gallery** if it brings a component the gallery does not show.
7. **Compare.** Screenshot the web screen and the native one at 1280 and 360, dark and light, and each state. Fix what differs. Put the pictures in the pull request.

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
- **The development build logs a failed connection to a development server** on every start. It is the hot reload client looking for an editor, and it is not in a release build.
- **Starting is slow in a development browser build**: about fifteen seconds in headless Chromium, three for a published build.

## Known differences from the web client

Each was seen in the screenshots or in the browser's element tree. None is hidden by a workaround.

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

Verified in Chromium against the build of this work. Each needs a decision before the web client can be retired.

| Area | What was found |
| --- | --- |
| Accessibility in a browser | The app now sets `FeatureConfiguration.AutomationPeer.AutoEnableAccessibility` before creating its UI, so the semantic tree does not require a hidden opt-in. The resolved 7.0.65 runtime activates too early and can leave an empty semantic root, so the shell rebuilds it through the runtime’s own managed accessibility exports after loading. The published smoke check requires actual headings on every tested route and invokes the download through its accessible button. This deliberately accepts the runtime cost. Screen-reader testing across supported platforms is still required. Historical foundation observation: the interface is a canvas. The platform builds a parallel tree of real elements (headings with levels, buttons, inputs with their labels, tabs, images with names, landmarks), but only after the person activates a hidden "Enable accessibility" element that the first press of Tab reveals. Keeping the tree on for everyone is possible (`FeatureConfiguration.AutomationPeer.AutoEnableAccessibility`) and the platform's documentation warns of its cost |
| Password managers | Not yet tested with a real password manager. What the platform does (read from the runtime script of SDK 7.0.22): a `PasswordBox` gets a real `input` with `type="password"` and `autocomplete="password"` while it has the focus, so a manager can recognize and fill it. What may still hinder pairing and saving: the element exists only while focused, the same element is reused for the next field, it is outside any `form`, and the user name field carries no `autocomplete="username"`. Test fill and save in Chromium, Firefox, and Safari with their built-in managers before the web client is retired |
| Text entry and input methods | Typing goes through that one hidden `input`. `inputmode` follows the field's type. Composition for languages that need it was not tested |
| Find in page, reader mode, translation, selection across the page | None works: there is no text in the page. Selection works inside one text block that enables it |
| Browser zoom and text size | Page zoom scales the canvas. The browser's text-size setting has no effect |
| Clipboard | Copying is asked of the browser without waiting for its answer, so a refusal cannot be told to the person |
| Download size | Measured 2026-11-18 on a Release publish: 50.5 MB of files, 15.3 MB if every file is fetched as its Brotli copy (the web client's publish: 12.5 MB, 4.1 MB). A first visit to `/native/sign-in` with no service worker fetched 104 files: 44.1 MB when nothing is compressed, 12.1 MB from Drover's server, which serves the build's own Brotli copies (`PrecompressedFiles`). Trimming is already full; IL stripping applies only to ahead-of-time compilation, which is off. See "Size levers" below |
| Service worker, install, push in a browser | The web client's worker, start script and notification script, shared. Verified in Chromium against Drover's server: the worker installs, stores all 169 shell files with matching hashes, and takes control. A real push delivered to the worker, and the notification tap, are covered by `NativeServiceWorkerTests`; a subscription against a real push service was not made from this machine |
| Script evaluation | The runtime evaluates script strings. Five are known and answered without `eval`; another may appear with an SDK upgrade or a platform feature |
| Credential store on desktop | Not implemented by the platform, so the desktop app keeps the sign-in only until it closes |
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

The following three classes still explicitly derive from `PendingScreen`:

- `TalkPage`: crew, main and side conversations, live runs, attachments, approvals, delegation, memory, activity,
  summary, files, and screen panels.
- `BotEditorPage`: definition, model, grants, rules, memory, routines, sandbox, team, limits, and unsaved changes.
- `ShareToPage`: incoming content and recipient selection.

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

The native browser build is served from `wwwroot/native` (or the folder `NativeClient:Path` names) by
`src/Drover.Server/Clients/NativeClientFiles.cs`, with `/native/` and its non-file deep links falling back to its `index.html`
and `/native` sent to `/native/`. A missing file is a 404, never the page. The existing client continues to own `/`.
Manifest files are JSON; the build's Brotli and gzip copies are served to a browser that accepts them, with the
file's own type and `Vary: Accept-Encoding`; content-named framework files and content-named native package
directories can be cached immutably; entry files are revalidated. The CSP is unchanged. Verified on 2026-11-18
against a real server and PostgreSQL: `/native/sign-in` loaded in Chromium with no policy violation and no console
error but the expected `401` for the session and `404` for closed setup; signing in through the drawn form sent the
antiforgery header, set only `HttpOnly` session cookies (plus the readable `XSRF-TOKEN`), left local storage empty,
and showed the workspace. To include the native publish in a container built from this repository:

```bash
docker build --build-arg DROVER_BUILD_NATIVE=true -t drover:native-preview .
```

The default image still contains only the existing client. The opt-in build installs `python3` in the build stage,
which the WebAssembly native linker (`emcc`) needs and the SDK image lacks. It must meet the same container memory
budget as the existing deployment before becoming the default. This environment accepted a 2 GB Docker builder configuration but exposed a shared 16 GB cgroup, so its reported memory use cannot verify that budget. The local opt-in container build was stopped after fetching the pinned base images; container publishing and startup still need the real CI/deployment check.

`.github/workflows/native.yml` builds Android on hosted Linux, builds desktop on hosted Linux, Windows and macOS, and publishes the browser head. It uploads a development-signed Android APK and the browser publish; it does not release either. This configuration is not evidence those remote jobs passed. iOS builds, device tests, production signing,
packaging, and real provider flows remain to be verified. Incoming links, tool sign-in returns, UnifiedPush
registration and notifications, and shares in are built on Android from the shell's own code and compile in the
APK (its manifest was read back: receivers, the file provider, the return scheme, the share targets), but were not
run on a device. Credential persistence on desktop is not implemented by the platform. `ConnectViewModel` still
lives in the native app and lacks unit tests. A desktop has no system share target or link scheme, so nothing
comes into the desktop app from outside it.

Screenshots in `docs/screenshots/native-parity-pass` cover dark and light at 1280 and 360 px with explicitly marked sample data. The browser check includes loading and failed extension states, empty setup/usage/tool states, tool creation and review. These are regression evidence, not a full visual comparison: desktop usage still uses cards instead of the original table; spacing differences remain. Each port still needs complete state coverage, 200 percent zoom, keyboard, screen-reader, and pixel comparisons against the existing client. Keep both existing hosts until these checks and
the feature ports above are complete. Passing tests for the existing clients must not be reported as native parity.

### Verification recorded for this pass

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

### Linux runtime failure discovered during verification

The Release desktop app was launched under a new Xorg dummy display with isolated settings. It created its
native window but failed before mapping its first frame: `SKTypeface.GetTableTags()` threw
`Unable to read the tables for the file`, reached through `SkiaFontProvider.GetDefaultFont` during text measure.
A separate probe using the same SkiaSharp 4.156.0 packages reproduced an empty default typeface, while an
explicit `DejaVu Sans` typeface and its TrueType file both loaded and exposed their tables. This also reproduced
with a private fontconfig containing only valid DejaVu fonts, after excluding the host's WOFF2 defaults.

The pinned Uno [fallback source](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/src/Uno.UI.Composition.Skia/Composition/Uno/Drawing/SkiaFontProvider.cs#L197)
constructs a font face from the default without rejecting an empty typeface. Its
[font-loading path](https://github.com/unoplatform/uno/blob/30034eefe42ecf5f3e3fff7d49a05340aaa0152e/src/Uno.UI/UI/Xaml/Documents/TextFormatting/FontDetailsCache.cs#L176)
uses that fallback while file fonts load asynchronously. This needs a supported font/fallback correction and a
successful runtime check; compiling the desktop head is insufficient. No exception suppression or fabricated
startup screenshot has been added. The virtual display also lacks an EWMH window manager; that limitation is
separate from the independently reproduced font failure.

### Completion estimate and retirement gate

At the original head the overall migration was approximately 30 percent complete. After this pass the engineering
estimate is approximately **45 percent**, with a wide uncertainty range: twelve of sixteen routed screen classes (including the development gallery)
now have implementations, but conversations, chat panels, bot editing and sandbox interaction carry much more
work than a simple settings page. Screen counts, test counts and successful compilation are not percentages of
feature parity. No claim of 100 percent visual, feature, accessibility, deployment or security parity has been made.

The estimate includes reuse of the existing transport, API and presentation logic, implemented native screens,
foundation controls and the initial build/hosting path. It discounts the four placeholders, incomplete platform
services and the outstanding verification above. The existing Blazor and MAUI hosts **cannot yet be retired**.
Actual Apple/Windows runs and device accessibility need their target environments; provider flows, signing and
push need configured services. The current Linux/browser tests cannot establish those outcomes.

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
| XAML resource trimming (`UnoXamlResourcesTrimming`) | Uno.UI 2.12 to 1.58 MB and Uno.UI.FluentTheme 0.52 to 0.34 MB Brotli, about 0.75 MB less on a first load. See the project file for whether it is on |
| Lazy assets | Fonts are fetched only when drawn: the 12 MB of Open Sans the platform ships are never fetched by Drover, whose text is Inter; the platform's symbol font (0.23 MB) is |
