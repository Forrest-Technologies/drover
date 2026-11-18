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
| Desktop: Linux, macOS, Windows | `net11.0-desktop` | Builds. Run on Linux under X11 in this work; macOS and Windows not run |
| Android | `net11.0-android` | Project files are in place. Not built in this work: the build machine had no Android SDK |
| iOS, Mac Catalyst | `net11.0-ios`, `net11.0-maccatalyst` | Added to the target list only when `DroverEnableApple` is true. Not built |

## Commands

```bash
# Build one target (-warnaserror holds the same rules as the rest of Drover)
dotnet build src/Drover.Native -f net11.0-desktop -warnaserror
dotnet build src/Drover.Native -f net11.0-browserwasm -warnaserror     # needs: dotnet workload install wasm-tools
dotnet build src/Drover.Native -f net11.0-android -warnaserror         # needs the Android SDK

# Run on desktop
dotnet run --project src/Drover.Native -f net11.0-desktop

# Run in a browser, development build, at http://127.0.0.1:5199/ (the gallery is at /dev/gallery)
ASPNETCORE_URLS=http://127.0.0.1:5199 dotnet run --project src/Drover.Native -f net11.0-browserwasm

# Publish the browser build. It expects to be served under /native/.
dotnet publish src/Drover.Native -f net11.0-browserwasm -c Release -o <folder>
# The same, with the gallery in it, for the parity bench
dotnet publish src/Drover.Native -f net11.0-browserwasm -c Release -p:DroverShowGallery=true -o <folder>

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
- **Calling the browser.** Use `[JSImport]` on a browser function, as `Platforms/WebAssembly/Browser.cs` does. Never `WebAssemblyRuntime.InvokeJS`: it is `eval`.

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

- **A font is not variable here.** The Skia renderer does not vary a font along an axis. `Assets/Fonts/Inter.ttf.manifest` maps weights 400, 500, 600 to static files. A new weight needs a new file and a manifest row. Large headings use the display cut through `DvFontDisplay`, because the web's variable font switches cut by size.
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
| Accessibility in a browser | The interface is a canvas. The platform builds a parallel tree of real elements (headings with levels, buttons, inputs with their labels, tabs, images with names, landmarks), but only after the person activates a hidden "Enable accessibility" element that the first press of Tab reveals. Keeping the tree on for everyone is possible (`FeatureConfiguration.AutomationPeer.AutoEnableAccessibility`) and the platform's documentation warns of its cost |
| Password managers | Not yet tested with a real password manager. What the platform does (read from the runtime script of SDK 7.0.22): a `PasswordBox` gets a real `input` with `type="password"` and `autocomplete="password"` while it has the focus, so a manager can recognize and fill it. What may still hinder pairing and saving: the element exists only while focused, the same element is reused for the next field, it is outside any `form`, and the user name field carries no `autocomplete="username"`. Test fill and save in Chromium, Firefox, and Safari with their built-in managers before the web client is retired |
| Text entry and input methods | Typing goes through that one hidden `input`. `inputmode` follows the field's type. Composition for languages that need it was not tested |
| Find in page, reader mode, translation, selection across the page | None works: there is no text in the page. Selection works inside one text block that enables it |
| Browser zoom and text size | Page zoom scales the canvas. The browser's text-size setting has no effect |
| Clipboard | Copying is asked of the browser without waiting for its answer, so a refusal cannot be told to the person |
| Download size | The published browser build is 17.9 MB compressed with Brotli (57 MB of files), and a first load fetched 44 MB from a host that does not compress. The web client's publish is 3.6 MB compressed. Ahead-of-time compilation is not on, and would add to it |
| Service worker, install, push in a browser | Not set up. The published app registers no service worker |
| Script evaluation | The runtime evaluates script strings. Five are known and answered without `eval`; another may appear with an SDK upgrade or a platform feature |
| Credential store on desktop | Not implemented by the platform, so the desktop app keeps the sign-in only until it closes |
| Variable fonts | Not supported by the renderer |
| Search engines | Nothing to index. Drover's screens are behind sign-in, so this costs nothing today |

## Not done

- No feature screen has moved. Every address but `/sign-in` and `/dev/gallery` opens a placeholder that says the screen is in the web app and links to it.
- `Drover.Server` does not serve the native browser build. What it needs: static files under `/native` from a folder the deployment fills with the published `wwwroot`; a fallback of `/native/{**path}` to `/native/index.html`; the content type `application/json` for `.manifest`; and in `ClientFileCaching`, keep-for-good for `/native/_framework/` files named by content and for everything under `/native/package_<hash>/`.
- The gallery does not show the feature components the web gallery shows: notifications, crew list, side chats, conversation, handed work, composer. They move with their screens.
- The notifications dialog of the account menu says notifications are turned on in the web app. Push on a device and in the native browser build is not built.
- `ShareInbox` and `ThemeService` stayed in `Drover.Ui`: the first carries a Blazor file type, the second is script interop. The starters stayed whole, as their own rules require.
- `ConnectViewModel` lives in the app (it needs the device transport) and has no tests.
- Android, iOS, and Mac Catalyst were not built. macOS and Windows desktop were not run.
- No continuous integration job builds `Drover.Native.slnx`.
- Files shared to the app, incoming links from the system, and sign-in returns from a provider are not handled natively.
