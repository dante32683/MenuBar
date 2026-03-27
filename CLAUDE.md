# CLAUDE.md

Guidance for Claude Code (claude.ai/code) when working in this repo.

## What This Is

A macOS-style menu bar for Windows built with WinUI 3. It runs as a top-docked Windows AppBar and shows the active window title, media controls, network/battery status, virtual desktop label, and a clock. Unpackaged, non-MSIX, self-contained.

## Build & Run

```bash
# Debug build
dotnet build

# Full Release publish
dotnet publish -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64

# Single-File Release publish
dotnet publish -c Release -r win-x64 -p:Platform=x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64-single
```

**Important:** Whenever publishing, you MUST publish to **both** `publish/win-x64` (normal) and `publish/win-x64-single` (single-file version).

## Architecture

Single-window app. `MainWindow.xaml` / `MainWindow.xaml.cs` coordinates all services and UI.

**Data flow — event-driven with minimal polling:**
- Active window title: `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND` + `EVENT_OBJECT_NAMECHANGE`)
- Virtual desktop: `SetWinEventHook` (`EVENT_SYSTEM_DESKTOPSWITCH`) + 1s clock timer poll (hook unreliable for AppBar windows)
- Media: `MediaService` subscribes to `CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`, and `TimelinePropertiesChanged`.
- High-frequency update (100ms) toggles when the Media flyout is `Opened`/`Closed`.
- Metadata and thumbnails are cached to avoid expensive I/O during progress updates.
- Network: `NetworkInformation.NetworkStatusChanged`
- Clock: `DispatcherTimer` every 1s
- Battery: `Battery.AggregateBattery.ReportUpdated` event (instant, background thread → `DispatcherQueue.TryEnqueue`) + `DispatcherTimer` every 10s (fallback for power overlay changes)

**Key components:**
- `ViewModels/ObservableObject` — Base `INotifyPropertyChanged` with `SetProperty`.
- `ViewModels/MainViewModel` — `x:Bind OneWay` bindings for all bar segments. Scaling properties (`HostCornerRadius`, `HostPadding`, `BatteryIconWidth`, `IconFontSize`, `TextFontSize`) are computed from `bar_height` in `LoadSettings()`.
- `Services/MediaService` — SMTC session API (no keyboard simulation).
- `Services/HardwareService` — Battery via `Battery.AggregateBattery.GetReport()` + `GetSystemPowerStatus`. Network via `Windows.Networking.Connectivity.NetworkInformation`.
- `Services/VirtualDesktopService` — Hybrid COM/Registry; uses `IVirtualDesktopManager.GetWindowDesktopId` for window identification and Registry for custom names/ordinals.
- `Services/NativeMethods` — Win32 P/Invoke: AppBar, window management, subclassing, `IVirtualDesktopManager` COM.
- `Services/SettingsService` — Reads/writes `settings.json`; hot-reload from context menu.

**Window behavior:** Top-edge AppBar via `SHAppBarMessage` using `DisplayArea.OuterBounds` for monitor-aware docking. Win32 subclassing (`SetWindowSubclass`) handles `WM_DPICHANGED` (0x02E0), `WM_DISPLAYCHANGE` (0x007E), and `TaskbarCreated`.

**Flyout pattern:** Logo, media, network, battery use `FlyoutBase.AttachedFlyout` on the **inner Border** (not the outer wrapper Grid). Always call `ToggleAttachedFlyout(GetHostBorder(sender))` — never `(FrameworkElement)sender` — because the Tapped event fires on the outer Grid. The clock sends `Win+N` to open notification center.

**Flyout styling:** All flyouts: `ShouldConstrainToRootBounds="False"`, `FlyoutBase.SystemBackdrop = DesktopAcrylicBackdrop`, `AcrylicBrush` (`FlyoutAcrylicBackground`) as tint with `SystemAccentColorDark2`. `xmlns:media="using:Microsoft.UI.Xaml.Media"` declared on `Window` element.

## Fitts's Law Hit-Test Expansion

All 5 interactive widgets (Logo, Media, Network, Battery, Clock) use an outer/inner wrapper pattern:

- **Outer transparent `Grid`**: `Margin="0,-3,0,0"` extends hit area 3px above the StackPanel to reach y=0 (screen top edge). Carries Visibility binding, Pointer events, and Tapped.
- **Inner `Border`** (`x:Name="XxxHost"`): `Margin="0,3,0,0"` compensates, keeping the squircle visual at the original position. Carries `FlyoutBase.AttachedFlyout`.

`GetHostBorder(sender)` resolves the inner Border from a Grid sender (static widgets) or Border sender (dynamic AppMenuPanel items).

**Clock right-edge extension:** A separate transparent `Border` in `RootGrid` (`HorizontalAlignment="Right"`, `Width="12"`) adds a 12px hit zone at the screen's right edge. It's outside the column layout to avoid affecting widget positions. Its pointer events directly set `ClockHost.Background`; its Tapped calls `ClockHost_Tapped`.

Do not use negative right margins on StackPanel items to extend hit areas — this shrinks the Auto-width column and shifts the entire StackPanel.

## Battery Widget

**Bar icon:** Two overlapping `TextBlock` elements (`BatteryFillGlyphText` + `BatteryOutlineGlyphText`) in a `Grid` with `Width="{x:Bind ViewModel.BatteryIconWidth}"`. Fill glyph (`\uEBA0`–`\uEBAA`) is colored; outline glyph is white. No lightning bolt; charging shown by color only.

**Icon color priority (evaluated in order):**

1. Charging (`ChargeRateInMilliwatts > 0`) → Green `#6AC45B`
2. Plugged in / conservation (`ACLineStatus == 1`, not charging) → Teal `#00B7C3`
3. Battery Saver on (`SystemStatusFlag == 1`) → Amber `#EAA300`
4. Discharging ≤ 20% → Amber `#EAA300`
5. Normal discharging → White

**Projected Runtime:**
Calculated as `[Full Charge Capacity (Wh)] / [1m Rolling Average Wattage (W)]` using `FullChargeCapacityInMilliwattHours` and `AverageChargeRateInMilliwatts` from `HardwareService`.
- **Discharging:** Shown as "Full charge: Xh Ym".
- **Charging:** Shown as "Projected until full: Xh Ym".
- **Smoothing:** Uses a 1-minute rolling average (6 samples @ 10s) to stabilize estimates.

**Flyout layout (redesigned):**
The battery flyout uses a two-row `Grid` (`Padding="20"`, `CornerRadius="12"`). Top row: battery icon (Segoe Fluent Icons, 38pt, tinted to match bar fill color via `{x:Bind BatteryFillGlyphText.Foreground}`) paired with a right column `StackPanel` containing:
- A horizontal `StackPanel` with the percentage (34pt SemiBold) and a compact wattage chip (icon + value, baseline-aligned at `VerticalAlignment="Bottom"`, `Margin="12,0,0,8"`).
- Status text (14pt, `TextFillColorSecondaryBrush`) below.

Bottom row: a `StackPanel` (`Spacing="10"`, `Margin="0,16,0,0"`) with time-remaining and projected-runtime `TextBlock`s, each bound to a `Visibility` property. Wattage chip uses `\uE74A` (Up) for charging and `\uE74B` (Down) for discharging. Color tiers: charging → `#6AC45B` green; discharging < 9 W → white; 9–15 W → `#EAA300` amber (same as low-battery bar); > 15 W → `#C42B1C` red. The flyout uses a `Flyout.Opened` event (`BatteryFlyout_Opened`) that calls `UpdateBattery()` + `UpdateBatteryFlyout()` to force a fresh 1m rolling average sample at open time. All flyout state is driven by `MainViewModel` properties (`BatteryFlyoutPercent`, `BatteryFlyoutStatus`, `BatteryFlyoutWattage`, `BatteryFlyoutWattageIcon`, `BatteryFlyoutWattageBrush`, `BatteryFlyoutWattageVisibility`, `BatteryFlyoutTime`, `BatteryFlyoutTimeVisibility`, `BatteryFlyoutProjected`, `BatteryFlyoutProjectedVisibility`) populated by `UpdateBatteryFlyout()` in `MainWindow.xaml.cs`. No `x:Name` procedural manipulation in the flyout.

`EnergySaverOn` is true when either `SYSTEM_POWER_STATUS.SystemStatusFlag == 1` (Windows Battery Saver) OR `PowerGetEffectiveOverlayScheme` returns `961cc777-2547-4f9d-8174-7d86181b8a7a` (Power Saver overlay / Power Mode slider leftmost). Do NOT use `Windows.System.Power.PowerManager.EnergySaverStatus` — it throws in unpackaged apps. `SystemStatusFlag` alone also does not reflect the Power Mode overlay. Prefer `ChargeRateInMilliwatts > 0` for charging detection; some Lenovo drivers misreport `BatteryFlag & 8`. Do not use `GetSystemPowerStatus.BatteryFlag & 8` alone.

## Virtual Desktop Widget

Registry-only via `VirtualDesktopService.GetCurrentDesktopLabel()`:
1. Read `CurrentVirtualDesktop` (16-byte GUID, little-endian fields) from `HKCU\...\Explorer\VirtualDesktops`
2. Find ordinal by scanning `VirtualDesktopIDs` (packed 16-byte GUIDs)
3. Read name from `Desktops\{GUID}\Name`; fall back to `"Desktop N"`

**Timing:** The registry `CurrentVirtualDesktop` updates correctly and immediately on every desktop switch. However, `EVENT_SYSTEM_DESKTOPSWITCH` does not fire reliably from an AppBar window. The label is therefore polled every 1s via the clock `DispatcherTimer` (which calls `UpdateVirtualDesktop()` alongside `UpdateClock()`). `OnDesktopSwitchEvent` also calls `UpdateVirtualDesktop()` directly for an instant update when it does fire. Do not use a deferred timer — the registry is already correct by the time any code runs.

`VirtualDesktops\Desktops\` may contain orphaned subkeys from deleted desktops; only GUIDs present in `VirtualDesktopIDs` are active.

## Clock Toggle (Notification Center)

`ClockHost_Tapped` sends `Win+N`. To avoid reopening when already open:
- `OnForegroundEvent` stores `_lastExternalForegroundHwnd` when `hwnd != _hwnd`.
- Skip `Win+N` if `_lastExternalForegroundHwnd` is `ShellExperienceHost` (checked via process name), then clear the field.

Do not use `FindWindow("Shell_NotificationCenter", ...)` or snapshot state in `PointerPressed`.

## Configuration

`settings.json` next to the executable controls visibility, `bar_height` (28–56px), clock format, accent color. Copied to output with `PreserveNewest`. Scaling ViewModel properties (`HostCornerRadius`, `HostPadding`, `BatteryIconWidth`, `IconFontSize`, `TextFontSize`) are recomputed in `LoadSettings()` from `bar_height`.

## Development Process

Before starting any task, follow this strict **Research -> Strategy -> Execution** cycle:

1.  **Phase-Based Planning:** Break features or fixes into explicit, logical phases.
2.  **Audit:** Audit the entire plan for correctness, stability, and WinUI 3 constraints before implementation.
3.  **Phase-by-Phase Execution:**
    - Implement exactly one phase at a time.
    - **Verify:** After each phase, manually review all code changes for accuracy.
    - **Build:** Run `dotnet build` after every phase.
    - **Gate:** Fix any compilation errors **BEFORE** moving to the next phase.
4.  **Finalization:** Only publish (generate `publish/` folders) after all phases are complete and the final build is successful.

## Key Constraints

- Target: .NET 8, Windows App SDK 1.5, minimum Windows 10 1809
- Unpackaged (`WindowsPackageType=None`): no MSIX, no package identity
- Icons: Segoe Fluent Icons glyphs only, not image assets
- Root `Grid` uses `RequestedTheme="Dark"` for consistent dark flyouts
- Use `FontFamily="Segoe UI Variable"` per `TextBlock`; never on `Grid`/`Panel`
- Publish requires `-p:Platform=x64`
- `AcrylicBrush.BackgroundSource` does not exist in WinUI 3 — use `FlyoutBase.SystemBackdrop` + `AcrylicBrush` as tint only
- `FlyoutBase.SystemBackdrop` only works with `ShouldConstrainToRootBounds="False"`
- `{StaticResource SystemAccentColorDark2}` is static; runtime accent updates only affect bar background

## Planning & Audit Workflow

Before implementing any change, write a plan and audit it for correctness, stability, compile safety, and WinUI 3 constraints. If the audit fails, rewrite and re-audit before implementing.
