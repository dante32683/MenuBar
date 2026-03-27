# CLAUDE.md

Guidance for Claude Code when working in this repo.

## What This Is

macOS-style menu bar for Windows (WinUI 3, .NET 8). Top-docked AppBar showing active window title, app menus, media controls, network/battery, virtual desktop, and clock. Unpackaged, no MSIX.

## Build & Run

```bash
# Debug build
dotnet build MenuBar.csproj

# Always publish to BOTH targets
dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64
dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64-single
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
- Virtual desktop: `EVENT_SYSTEM_DESKTOPSWITCH` + 1s clock timer (hook unreliable from AppBar)
- Clock / virtual desktop / fullscreen check: `DispatcherTimer` every 1s
- Battery: `Battery.AggregateBattery.ReportUpdated` + `DispatcherTimer` every 10s
- Media: `MediaService` event subscriptions; 100ms high-frequency updates while flyout is open

## Key Patterns & Gotchas

**Flyout attachment:** Flyouts attach to the **inner `Border`** (`XxxHost`), not the outer wrapper `Grid`. Always call `ToggleAttachedFlyout(GetHostBorder(sender))` — `Tapped` fires on the outer `Grid`, so using `(FrameworkElement)sender` directly targets the wrong element.

**Flyout constraints:**
- `ShouldConstrainToRootBounds="False"` is required for `FlyoutBase.SystemBackdrop` to work
- `AcrylicBrush.BackgroundSource` does not exist in WinUI 3 — use `FlyoutBase.SystemBackdrop = DesktopAcrylicBackdrop` and `AcrylicBrush` as tint only

**Hit-test expansion:** Widgets use outer `Grid` (`Margin="0,-3,0,0"`) for hit area + inner `Border` (`Margin="0,3,0,0"`) for visual positioning. Do not use negative right margins on StackPanel items — this collapses the Auto-width column and shifts the layout.

**Fullscreen auto-hide:** `IsWindowFullscreen` compares `GetWindowRect` to `GetMonitorInfo.rcMonitor` (full monitor bounds, not work area — maximized windows do not trigger this) on the same monitor as the bar. `HideBarForFullscreen`: `ABM_REMOVE` + `ShowWindow(SW_HIDE)`. `ShowBarAfterFullscreen`: `ShowWindow(SW_SHOWNA)` + `RegisterOrUpdateAppBar`. State tracked in `_isFullscreenActive`. Triggered from `OnForegroundEvent` (instant) and the 1s clock timer (catches in-place fullscreen like browser video). `TaskbarCreated` in `NewWindowProc` skips re-registration when `_isFullscreenActive` is true.

**Battery / energy saver:** `EnergySaverOn` = `SystemStatusFlag == 1` OR `PowerGetEffectiveOverlayScheme` returns `961cc777-...` (Power Saver overlay). Do NOT use `Windows.System.Power.PowerManager.EnergySaverStatus` — throws in unpackaged apps. Use `ChargeRateInMilliwatts > 0` for charging detection; do not use `BatteryFlag & 8` alone (Lenovo misreports it).

**Virtual desktop:** `EVENT_SYSTEM_DESKTOPSWITCH` is unreliable from AppBar windows. The registry `CurrentVirtualDesktop` is correct by the time any code runs, so polling every 1s via clock timer is the primary mechanism. No deferred timers needed.

**Clock / Notification Center:** `ClockHost_Tapped` sends `Win+N`. Skip if `_lastExternalForegroundHwnd` process is `ShellExperienceHost` (notification center already open). Do not use `FindWindow("Shell_NotificationCenter", ...)`.

**Threading:** All UI Automation (`UiaMenuService`) and VirtualDesktopService COM calls run on background MTA threads to prevent UI hangs.

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
3. **Finalize:** Publish to both `publish/` folders only after all phases pass.
