# CLAUDE.md

macOS-style menu bar for Windows (WinUI 3, .NET 8). Unpackaged (no MSIX). Single window, top-docked AppBar.

## Build & Run

```bash
dotnet build MenuBar.csproj

dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -o publish --no-self-contained
```

## Architecture

`MainWindow.xaml.cs` coordinates services + UI. AppBar docking via `SHAppBarMessage` + `DisplayArea.OuterBounds`. Win32 subclassing (`SetWindowSubclass`) handles `WM_DPICHANGED`, `WM_DISPLAYCHANGE`, `TaskbarCreated`.

**Core services (high level):** `MediaService`, `HardwareService`, `VirtualDesktopService`, `UiaMenuService`, `SettingsService`, `NativeMethods`.

**Event sources:** foreground/title WinEvent hooks; desktop switch WinEvent hook; 1s clock timer (also fullscreen check); battery event + 10s timer; media SMTC events; volume scroll wheel handler.

## Key Patterns & Gotchas

**Performance:** `ProgressRing` must have `IsActive="False"` when hidden; SMTC can be chatty—keep `MediaService` caching.

**Flyout constraints:**
- `ShouldConstrainToRootBounds="False"` is required for `FlyoutBase.SystemBackdrop` to work
- No `AcrylicBrush.BackgroundSource` in WinUI 3 — use `FlyoutBase.SystemBackdrop = DesktopAcrylicBackdrop` and `AcrylicBrush` as tint only

**Hit-test expansion:** outer `Grid` uses `Margin="0,-3,0,0"`; inner `Border` uses `Margin="0,3,0,0"`. Avoid negative right margins on StackPanel items.

**Fullscreen auto-hide:** `IsWindowFullscreen` compares `GetWindowRect` to `GetMonitorInfo.rcMonitor` on the same monitor as the bar. Fullscreen is distinguished from maximized by absence of `WS_CAPTION`. Shell windows and cloaked windows are excluded. State tracked in `_isFullscreenActive`.

**Borderless window / hit-testing (critical):**
- Presenter APIs alone can be insufficient.
- Non-client frame + edge hit-testing are controlled by Win32 `GWL_STYLE`: clear `WS_CAPTION` / `WS_THICKFRAME` (and related frame styles) and apply `SWP_FRAMECHANGED`.

**Battery / energy saver:** Do NOT use `Windows.System.Power.PowerManager.EnergySaverStatus` — throws in unpackaged apps. Use `ChargeRateInMilliwatts > 0` for charging; do not use `BatteryFlag & 8` alone (Lenovo misreports it).

**Battery flyout:** `battery_show_progress_bar` and `battery_show_usage_time` both default `true`. Usage tracker (`BatteryUsageTracker`) persists to `usage_tracker.json`; bootstraps anchor from `fullMWh` on first discharge if no full-charge event recorded.

**Virtual desktop:** `EVENT_SYSTEM_DESKTOPSWITCH` fires before the registry is updated. `OnDesktopSwitchEvent` defers `UpdateVirtualDesktop()` by 50ms. Do not call it synchronously in the event.

**Clock / Notification Center:** `ClockHost_Tapped` sends `Win+N`. Skip if `_lastExternalForegroundHwnd` process is `ShellExperienceHost`. **Quick settings / media:** `TrySendWinAForQuickSettings` sends `Win+A`; same toggle pattern — skip if `_lastExternalForegroundHwnd` is `ShellExperienceHost` (bar is already foreground by Tapped time, so check the last external hwnd directly).

**Threading:** `UiaMenuService` and `VirtualDesktopService` COM calls run on background MTA threads. All UI updates must be dispatched via `DispatcherQueue`.

**Font size scaling:** `IconFontSize = barHeight * 0.62`, `TextFontSize = barHeight * 0.46`, unless overridden by `font_size_icon` / `font_size_text` in settings. `BatteryTextMargin` uses a negative top margin (not positive bottom) to avoid inflating measured height.

**Exception safety — native callbacks:** `NewWindowProc`, `OnForegroundEvent`, `OnTitleChangeEvent`, `OnDesktopSwitchEvent` are native callbacks. Exceptions escaping them cross the managed/native boundary and are immediately fatal. All must keep their top-level `try { } catch { }`.

**Exception safety — `async void`:** `async void` bypasses `UnhandledException` — unhandled exceptions go directly to the sync context and crash. Every `async void` method (e.g. `RefreshAppMenu`) must have a top-level `try { } catch { }`.

**Exception safety — global handler:** `App.xaml.cs` registers `UnhandledException` (`e.Handled = true`) and `TaskScheduler.UnobservedTaskException` (`e.SetObserved()`). Do not remove these.

**MediaService shutdown order:** `Dispose()` stops `_progressTimer` first, then sets `StateChanged = null`, then calls `AttachSession(null)`. This order prevents post-close UI callbacks.

## Key Constraints

- .NET 8, Windows App SDK 1.8, minimum Windows 10 1809
- Unpackaged (`WindowsPackageType=None`): no MSIX, no package identity
- Segoe Fluent Icons glyphs only (no image assets); `FontFamily="Segoe UI Variable"` on `TextBlock` only — never on `Grid`/`Panel`
- Root `Grid` has `RequestedTheme="Dark"` for consistent dark flyouts
- `{StaticResource SystemAccentColorDark2}` is static — runtime accent updates only affect bar background
- Publish requires `-p:Platform=x64`

## Dev loop

- Make a focused change.
- `dotnet build MenuBar.csproj`
- Republish to `publish/` using the command above.
