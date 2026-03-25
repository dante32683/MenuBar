# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repo.

## What This Is

A macOS-style menu bar for Windows built with WinUI 3. It runs as a top-docked Windows AppBar and shows the active window title, media controls, network/battery status, and a clock. It is unpackaged, non-MSIX, and self-contained.

## Build & Run

```bash
# Debug build
dotnet build

# Release publish to local folder
dotnet publish -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64
```

No solution file — build from the project directory directly. No tests exist in the project.

## Architecture

Single-window app with one XAML page. `MainWindow.xaml` / `MainWindow.xaml.cs` coordinates the services and UI.

**Data flow — event-driven with minimal polling:**
- Active window title: `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND` + `EVENT_OBJECT_NAMECHANGE`)
- Media: `MediaService` subscribes to `CurrentSessionChanged`, `MediaPropertiesChanged`, and `PlaybackInfoChanged`
- Network: `NetworkInformation.NetworkStatusChanged`
- Clock: `DispatcherTimer` every 1s
- Battery: `DispatcherTimer` every 30s

**Key components:**
- `ViewModels/ObservableObject` — Base `INotifyPropertyChanged` class with `SetProperty`.
- `ViewModels/MainViewModel` — Uses `x:Bind` `OneWay` bindings for all bar segments.
- `Services/MediaService` — Handles media sessions and SMTC events; uses session APIs instead of keyboard simulation.
- `Services/HardwareService` — Battery via `GetSystemPowerStatus` plus `Battery.AggregateBattery.GetReport()`. Network via `Windows.Networking.Connectivity.NetworkInformation`.
- `Services/NativeMethods` — Win32 P/Invoke declarations for AppBar, window management, keyboard, and WinEvent hooks.
- `Services/SettingsService` — Reads and writes `settings.json` from `AppContext.BaseDirectory`; settings hot-reload from the context menu.

**Window behavior:** Registers as a top-edge AppBar via `SHAppBarMessage`, uses `WS_EX_TOOLWINDOW` to stay out of Alt-Tab, and tries `DesktopAcrylicBackdrop` before falling back to `MicaBackdrop`.

**Flyout pattern:** Logo, media, network, and battery use `FlyoutBase.AttachedFlyout` on tap. The clock sends `Win+N` to open notification center and suppresses a second send if it was already open.

**Flyout styling:** All flyouts use `ShouldConstrainToRootBounds="False"` with `DesktopAcrylicBackdrop` as `FlyoutBase.SystemBackdrop`. A shared `AcrylicBrush` resource (`FlyoutAcrylicBackground`) tints the presenter background with `SystemAccentColorDark2`. `xmlns:media="using:Microsoft.UI.Xaml.Media"` is declared on the `Window` element so `DesktopAcrylicBackdrop` can be created in XAML.

- Logo: `MenuFlyout` with `MenuFlyoutPresenterStyle` background
- Media: custom `Flyout` with `FlyoutPresenterStyle` (`Padding=0`, `MinWidth=300`, `CornerRadius=8`)
- Network: custom `Flyout` with `FlyoutPresenterStyle` (`Padding=0`, `MinWidth=260`, `CornerRadius=8`)
- Battery: custom `Flyout` with `FlyoutPresenterStyle` (`Padding=0`, `MinWidth=240`, `CornerRadius=8`)
- Context menu: `MenuFlyout` on `Grid.ContextFlyout` with the same styling

## Battery Widget

**Bar icon:** Two overlapping `TextBlock` elements in a `Grid` (`BatteryFillGlyphText` + `BatteryOutlineGlyphText`). The fill glyph (`\uEBA0`–`\uEBAA`) is colored and the white outline glyph creates the border. No lightning bolt; charging is shown by color only.

**Icon color states (set in `UpdateBattery()` via `GetBatteryFillBrush()`):**

| State | `BatteryStatus` | Color | Outline visible |
|---|---|---|---|
| Actively charging | `Charging` | Green `#6AC45B` | Yes |
| Plugged in, conservation mode / fully charged | `Idle` | Teal `#00B7C3` | Yes |
| Discharging, percent > 20 | `Discharging` | White | No |
| Discharging, percent ≤ 20 | `Discharging` | Amber `#EAA300` | Yes |
| No battery (AC only) | `NotPresent` | White | No |

Charge state comes from `Windows.Devices.Power.Battery.AggregateBattery.GetReport()`. Prefer `ChargeRateInMilliwatts > 0` when available; some Lenovo drivers report `Charging` and set `BatteryFlag & 8` even during conservation mode, so do not trust `GetSystemPowerStatus.BatteryFlag & 8` or `BatteryStatus` alone.

Brush fields: `_batteryDefaultBrush` (white), `_batteryChargingBrush` (green), `_batteryPluggedBrush` (teal), `_batterySaverBrush` (amber).

**Battery flyout:** Custom `Flyout` with a two-row `Grid`. Row 0 shows `BatteryFlyoutIcon`, `BatteryFlyoutPercent`, and `BatteryFlyoutStatus`; row 1 shows `BatteryFlyoutTime` when available. `BatteryFlyoutStatus` is one of `Charging`, `Plugged in, fully charged`, `Smart charging`, or `On battery power`. `UpdateBatteryFlyout()` fills the fields right before open.

## Network Flyout

Custom `Flyout` with a `Grid` layout matching the battery flyout. Row 0 shows `NetworkFlyoutIcon`, `NetworkFlyoutName`, and `NetworkFlyoutStatus`; row 1 shows `NetworkFlyoutSpeed` when available. `UpdateNetworkFlyout()` fills the fields right before open.

## Clock Toggle (Notification Center)

Tapping `ClockHost` sends `Win+N` to open notification center. To avoid reopening it on a second tap, the bar tracks the last external foreground window via `EVENT_SYSTEM_FOREGROUND`:

- `OnForegroundEvent` stores `_lastExternalForegroundHwnd` when `hwnd != _hwnd`.
- `ClockHost_Tapped` skips `Win+N` if `_lastExternalForegroundHwnd` is `ShellExperienceHost`, then clears the field.
- `IsShellExperienceHostWindow` checks the process name via `GetWindowThreadProcessId` and `Process.GetProcessById`; failures return false.

Do not use `FindWindow("Shell_NotificationCenter", ...)` or snapshot state in `PointerPressed`; both are unreliable here.

## Configuration

`settings.json` sits next to the executable and controls bar segment visibility, bar height (28–56px), clock format, and accent color usage. It is copied to output with `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`.

## Key Constraints

- Target: .NET 8, Windows App SDK 1.5, minimum Windows 10 1809
- Unpackaged app (`WindowsPackageType=None`): no MSIX, no package identity
- Icons use Segoe Fluent Icons glyphs, not image assets
- Bar text is white; battery icon color is state-driven
- Root `Grid` uses `RequestedTheme="Dark"` for consistent dark flyouts
- Use Segoe UI Variable per `TextBlock`; do not set `FontFamily` on `Grid`/`Panel`
- Publish requires `-p:Platform=x64`
- `AcrylicBrush.BackgroundSource` does not exist in WinUI 3; use `FlyoutBase.SystemBackdrop = new DesktopAcrylicBackdrop()` and `AcrylicBrush` only as tint
- `FlyoutBase.SystemBackdrop` only works with `ShouldConstrainToRootBounds="False"`
- `{StaticResource SystemAccentColorDark2}` is static; runtime accent updates only affect the bar background, not flyout tint
- Do not use `GetSystemPowerStatus.BatteryFlag & 8` for charging detection; use `Battery.AggregateBattery.GetReport().Status` instead

## Planning & Audit Workflow

Before implementing any change, write a plan and audit it for correctness, stability, compile safety, and WinUI 3 constraints. If the audit fails, rewrite and re-audit before implementing. This applies to every planning phase.
