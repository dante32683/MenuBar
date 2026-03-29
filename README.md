# MenuBar

A macOS-style menu bar for Windows. Built with WinUI 3.

Sits at the top of your screen and shows the active window title, app menus, media controls, network/battery info, and a clock. Click on most sections to get more details in a flyout.

## What's on the bar

- **Windows logo** (optional) — tap to open a power menu: Settings, Sleep, Restart, Shut down
- **Active window title & icon** — shows the foreground app's name and icon
- **App menus** (optional) — File, Edit, etc. extracted from the active app via UI Automation or Win32
- **Media controls** — tap to open a flyout with album art, track title/artist/source, progress slider, and shuffle/repeat/prev/play/next buttons
- **Network** — tap to open a flyout with SSID, connection status, and live speed
- **Battery** — icon changes color for charging (green), plugged-in full (white), energy saver (yellow), or low (yellow); tap to open a flyout with percentage, wattage, status, and time remaining
- **Virtual desktop name** (optional) — shows the current desktop as a centered pill
- **Clock** — tap to open Notification Center; shows time, optional seconds, optional date

## Fullscreen Auto-Hide

The bar automatically hides when a fullscreen window is detected on the same monitor and reappears when it exits.

## Multi-Monitor & Stability

- **Per-monitor DPI docking** — adjusts position and size on resolution or DPI changes via Win32 subclassing
- **Explorer recovery** — re-registers automatically if `explorer.exe` restarts
- **Background threads** — UI Automation (app menus) and virtual desktop COM calls run on MTA threads so the UI never hangs
- **Hybrid desktop tracking** — uses COM + Registry for accurate virtual desktop names across Windows 10 and 11

## Running It

Just run `MenuBar.exe` from the `publish/win-x64` folder. No installer needed. A `settings.json` is created next to the exe on first run.

To build it yourself:

```powershell
dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64
dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -o publish/win-x64-single
```

Requires .NET 8 and Windows App SDK 1.5. Windows 10 1809 or newer.

## Context Menu

Right-click anywhere on the bar to access:

- **Open settings** — opens `settings.json` in your default editor
- **Reload settings** — applies changes from `settings.json` without restarting
- **Restart bar** — relaunches the process
- **Stop bar** — exits

## Settings

Edit `settings.json` next to the exe. Right-click the bar > **Reload Settings** to apply changes without restarting.

| Key | Default | What it does |
|-----|---------|--------------|
| `bar_height` | `28` | Height in px (28–56) |
| `show_windows_logo` | `false` | Show a Windows logo with power menu on the left |
| `show_title` | `true` | Show active window title and icon |
| `show_app_menu` | `false` | Show the active app's menus (File, Edit, etc.) |
| `show_media` | `true` | Show media widget with flyout controls |
| `show_network` | `true` | Show network widget with flyout |
| `show_battery` | `true` | Show battery widget with flyout |
| `show_virtual_desktop` | `false` | Show current virtual desktop name as a centered pill |
| `show_clock` | `true` | Show clock; tap to open Notification Center |
| `show_projected_runtime` | `true` | Show estimated runtime in battery flyout (based on wattage) |
| `clock_24h` | `false` | Use 24-hour time format |
| `clock_show_seconds` | `false` | Show seconds in the clock |
| `clock_show_date` | `true` | Show the date alongside the time |
| `clock_date_format` | `"MM/dd/yyyy"` | .NET date format string for the date portion |
| `use_accent_color` | `true` | Tint the bar background with your Windows accent color |
| `title_max_length` | `0` | Truncate window title after N characters (0 = no limit) |
| `media_max_length` | `0` | Truncate media title after N characters (0 = no limit) |
| `font_size_text` | `0` | Override text size in px (0 = auto-scale with bar height) |
| `font_size_icon` | `0` | Override icon size in px (0 = auto-scale with bar height) |
