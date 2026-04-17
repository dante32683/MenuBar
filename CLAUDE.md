# CLAUDE.md

Guidance for Claude Code when working in this repo.

## What This Is

macOS-style menu bar for Windows (WinUI 3, .NET 8). Top-docked AppBar showing active window title, app menus, media controls, network/battery, virtual desktop, and clock. Unpackaged, no MSIX.

## Build & Run

```bash
# Debug build
dotnet build MenuBar.csproj

# Publish directly to publish/ folder
dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -o publish --no-self-contained
```

## Architecture

Single-window app. `MainWindow.xaml.cs` coordinates all services. AppBar registered via `SHAppBarMessage` using `DisplayArea.OuterBounds` for per-monitor docking. Win32 subclassing (`SetWindowSubclass`) handles `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, and `TaskbarCreated`.

**Services:**
- `MediaService` — SMTC session API (no keyboard simulation)
- `HardwareService` — Battery + Network
- `VirtualDesktopService` — Hybrid COM/Registry for desktop names/ordinals
- `UiaMenuService` — App menu extraction via UI Automation; runs on a background MTA thread
- `NativeMethods` — All Win32 P/Invoke
- `SettingsService` — `settings.json` with hot-reload via context menu

**Event sources (event-driven, minimal polling):**
- Active window title: `EVENT_SYSTEM_FOREGROUND` + `EVENT_OBJECT_NAMECHANGE`
- Virtual desktop: `EVENT_SYSTEM_DESKTOPSWITCH` + 1s clock timer
- Volume scroll: `PointerWheelChanged` on RootGrid; cumulative delta threshold (120 units) → `keybd_event`
- Clock / virtual desktop / fullscreen check: `DispatcherTimer` every 1s
- Battery: `Battery.AggregateBattery.ReportUpdated` + `DispatcherTimer` every 10s
- Media: `MediaService` event subscriptions; 100ms high-frequency updates while flyout is open

## Key Patterns & Gotchas

**Flyout attachment:** Flyouts attach to the **inner `Border`** (`XxxHost`), not the outer wrapper `Grid`. Always call `ToggleAttachedFlyout(GetHostBorder(sender))`.

**Flyout constraints:**
- `ShouldConstrainToRootBounds="False"` is required for `FlyoutBase.SystemBackdrop` to work
- No `AcrylicBrush.BackgroundSource` in WinUI 3 — use `FlyoutBase.SystemBackdrop = DesktopAcrylicBackdrop` and `AcrylicBrush` as tint only

**Hit-test expansion:** Widgets use outer `Grid` (`Margin="0,-3,0,0"`) for hit area + inner `Border` (`Margin="0,3,0,0"`) for visual positioning. Do not use negative right margins on StackPanel items.

**Fullscreen auto-hide:** `IsWindowFullscreen` compares `GetWindowRect` to `GetMonitorInfo.rcMonitor` on the same monitor as the bar. Fullscreen is distinguished from maximized by absence of `WS_CAPTION`. Shell windows and cloaked windows are excluded. State tracked in `_isFullscreenActive`.

**Battery / energy saver:** Do NOT use `Windows.System.Power.PowerManager.EnergySaverStatus` — throws in unpackaged apps. Use `ChargeRateInMilliwatts > 0` for charging; do not use `BatteryFlag & 8` alone (Lenovo misreports it).

**Battery flyout:** `battery_show_progress_bar` and `battery_show_usage_time` both default `true`. Usage tracker (`BatteryUsageTracker`) persists to `usage_tracker.json`; bootstraps anchor from `fullMWh` on first discharge if no full-charge event recorded.

**Virtual desktop:** `EVENT_SYSTEM_DESKTOPSWITCH` fires before the registry is updated. `OnDesktopSwitchEvent` defers `UpdateVirtualDesktop()` by 50ms. Do not call it synchronously in the event.

**Clock / Notification Center:** `ClockHost_Tapped` sends `Win+N`. Skip if `_lastExternalForegroundHwnd` process is `ShellExperienceHost`.

**Threading:** `UiaMenuService` and `VirtualDesktopService` COM calls run on background MTA threads. All UI updates must be dispatched via `DispatcherQueue`.

**Font size scaling:** `IconFontSize = barHeight * 0.62`, `TextFontSize = barHeight * 0.46`, unless overridden by `font_size_icon` / `font_size_text` in settings. `BatteryTextMargin` uses a negative top margin (not positive bottom) to avoid inflating measured height.

**Exception safety — native callbacks:** `NewWindowProc`, `OnForegroundEvent`, `OnTitleChangeEvent`, `OnDesktopSwitchEvent` are native callbacks. Exceptions escaping them cross the managed/native boundary and are immediately fatal. All must keep their top-level `try { } catch { }`.

**Exception safety — `async void`:** `async void` bypasses `UnhandledException` — unhandled exceptions go directly to the sync context and crash. Every `async void` method (e.g. `RefreshAppMenu`) must have a top-level `try { } catch { }`.

**Exception safety — global handler:** `App.xaml.cs` registers `UnhandledException` (`e.Handled = true`) and `TaskScheduler.UnobservedTaskException` (`e.SetObserved()`). Do not remove these.

**MediaService shutdown order:** `Dispose()` stops `_progressTimer` first, then sets `StateChanged = null`, then calls `AttachSession(null)`. This order prevents post-close UI callbacks.

## Key Constraints

- .NET 8, Windows App SDK 1.5, minimum Windows 10 1809
- Unpackaged (`WindowsPackageType=None`): no MSIX, no package identity
- Segoe Fluent Icons glyphs only (no image assets); `FontFamily="Segoe UI Variable"` on `TextBlock` only — never on `Grid`/`Panel`
- Root `Grid` has `RequestedTheme="Dark"` for consistent dark flyouts
- `{StaticResource SystemAccentColorDark2}` is static — runtime accent updates only affect bar background
- Publish requires `-p:Platform=x64`

## Development Process

1. **Plan:** Break into explicit phases before writing any code.
2. **Build after each phase:** `dotnet build MenuBar.csproj`. Fix all errors before proceeding.
3. **Finalize:** Always publish to the `publish/` folder after every completed change — use the `dotnet publish` command from Build & Run above.
