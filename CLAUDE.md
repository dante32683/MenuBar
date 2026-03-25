# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## What This Is

A macOS-style menu bar for Windows, built with WinUI 3. It registers as a Windows AppBar docked to the top of the screen, displaying the active window title, media controls, network/battery status, and a clock. Designed to run as an unpackaged (non-MSIX) self-contained executable.

## Build & Run

```bash
# Debug build (AnyCPU)
dotnet build

# Release publish (self-contained, x64) to local publish folder
dotnet publish -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64
```

No solution file — build from the project directory directly. No tests exist in the project.

## Architecture

Single-window app with one XAML page. `MainWindow.xaml` / `MainWindow.xaml.cs` coordinates services and UI.

**Data flow — event-driven with minimal polling:**
- Active window title: event-driven via `SetWinEventHook` (`EVENT_SYSTEM_FOREGROUND` + `EVENT_OBJECT_NAMECHANGE`)
- Media: event-driven via `MediaService` subscribing to `CurrentSessionChanged`, `MediaPropertiesChanged`, `PlaybackInfoChanged`
- Network: event-driven via `NetworkInformation.NetworkStatusChanged`
- Clock: `DispatcherTimer` at 1s interval
- Battery: `DispatcherTimer` at 30s interval (no event API for percentage changes)

**Key components:**
- `ViewModels/ObservableObject` — Base INotifyPropertyChanged class with `SetProperty` helper.
- `ViewModels/MainViewModel` — Inherits ObservableObject. x:Bind OneWay bindings for all bar segments.
- `Services/MediaService` — Event-driven media session handler. Subscribes to WinRT SMTC events, exposes `StateChanged` event. Uses session APIs (`TryTogglePlayPauseAsync` etc.) instead of keyboard simulation.
- `Services/HardwareService` — Battery via `GetSystemPowerStatus` P/Invoke; network via WinRT `Windows.Networking.Connectivity.NetworkInformation` (SSID, signal bars, adapter speed).
- `Services/NativeMethods` — Win32 P/Invoke declarations (AppBar, window management, keyboard, WinEvent hooks).
- `Services/SettingsService` — Reads/writes `settings.json` from `AppContext.BaseDirectory`. Settings are hot-reloadable via right-click context menu.

**Window behavior:** Registers as a top-edge AppBar via `SHAppBarMessage` so other windows respect its space. Window is styled as a tool window (`WS_EX_TOOLWINDOW`) to hide from Alt-Tab. Mica backdrop when supported.

**Flyout pattern:** Each bar segment (logo, media, network, battery) uses `FlyoutBase.AttachedFlyout` toggled on tap. The clock tap sends Win+N (notification center).

## Configuration

`settings.json` sits next to the executable and controls visibility of bar segments, bar height (28–56px), clock format, and accent color usage. The file is copied to output on build via `<CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>`.

## Key Constraints

- Target: .NET 8, Windows App SDK 1.5, minimum Windows 10 1809
- Unpackaged app (`WindowsPackageType=None`) — no MSIX, no package identity
- Icons use Segoe Fluent Icons font glyphs, not image assets
- All foreground colors are white — bar assumes a dark translucent background (`#B01C222A`)
- XAML uses `RequestedTheme="Dark"` on root Grid for consistent dark flyouts
- Text uses Segoe UI Variable font (set per-TextBlock; do NOT set `FontFamily` on Grid/Panel — crashes WinUI 3 v1.5 XAML compiler)
- Publish requires explicit platform: `-p:Platform=x64` (self-contained mode rejects AnyCPU)
