# MenuBar

A macOS-style menu bar for Windows. Built with WinUI 3.

Sits at the top of your screen and shows the active window title, app menus, media controls, network/battery info, and a clock. Click on most sections to get more details in a dropdown.

## What's on the bar

- Active window title & icon
- App menus (File, Edit, etc. for supported apps)
- Media controls (play/pause, track info)
- Network info (SSID, signal, speed)
- Battery percentage
- Virtual desktop name
- Clock (tap it to open Notification Center)

## Multi-monitor & Stability

- **Per-Monitor Docking:** Automatically adjusts when screen resolution or DPI changes using Win32 subclassing.
- **Explorer Recovery:** If `explorer.exe` crashes or restarts, MenuBar re-registers itself automatically.
- **High Performance:** UI Automation (app menus) and system COM calls (virtual desktops) run on background threads to ensure the UI never hangs.
- **Hybrid Desktop Tracking:** Uses both COM and Registry for accurate virtual desktop labels across all Windows 10/11 versions.

## Running it

Just run `MenuBar.exe` from the `publish/win-x64` folder. No installer needed.

To build it yourself:

```powershell
# Build and publish to a single folder
dotnet publish MenuBar.csproj -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64
```

Needs .NET 8 and Windows App SDK 1.5. Windows 10 1809 or newer.

## Settings

Edit `settings.json` next to the exe. Right-click the bar > **Reload Settings** to apply changes.

| Key | Default | What it does |
|-----|---------|--------------|
| `bar_height` | `28` | Height in px (28–56) |
| `show_title` | `true` | Show window title |
| `show_app_menu` | `false` | Show the app's menus (File, Edit, etc.) |
| `show_media` | `true` | Show media controls |
| `show_network` | `true` | Show network info |
| `show_battery` | `true` | Show battery |
| `show_projected_runtime` | `true` | Show estimated battery time remaining based on wattage |
| `show_clock` | `true` | Show clock |
| `show_virtual_desktop` | `false` | Show the current virtual desktop name |
| `show_windows_logo` | `false` | Show a Windows logo on the left |
| `clock_24h` | `false` | Use 24h time |
| `clock_show_seconds` | `false` | Show seconds in the clock |
| `clock_show_date` | `true` | Show the date |
| `clock_date_format` | `"MM/dd/yyyy"` | Format for the date |
| `use_accent_color` | `true` | Tint bar with your accent color |
| `title_max_length` | `0` | Max characters for title (0 = no limit) |
| `media_max_length` | `0` | Max characters for media (0 = no limit) |
| `font_size_text` | `0` | Manual text size (0 = auto) |
| `font_size_icon` | `0` | Manual icon size (0 = auto) |
