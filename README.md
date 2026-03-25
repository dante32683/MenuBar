# MenuBar

A macOS-style menu bar for Windows. Built with WinUI 3.

Sits at the top of your screen and shows the active window title, media controls, network/battery info, and a clock. Click on any section to get more details in a dropdown.

## What's on the bar

- Active window title
- Media controls (play/pause, track info)
- Network info (SSID, signal, speed)
- Battery percentage
- Clock (tap it to open Notification Center)

## Running it

Just run `MenuBar.exe`. No installer needed.

To build it yourself:

```bash
# debug
dotnet build

# release
dotnet publish -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64
```

Needs .NET 8 and Windows App SDK 1.5. Windows 10 1809 or newer.

## Settings

Edit `settings.json` next to the exe. Right-click the bar > Reload Settings to apply changes.

| Key | Default | What it does |
|-----|---------|--------------|
| `bar_height` | `28` | Height in px (28–56) |
| `show_title` | `true` | Show window title |
| `show_media` | `true` | Show media controls |
| `show_network` | `true` | Show network info |
| `show_battery` | `true` | Show battery |
| `show_clock` | `true` | Show clock |
| `show_windows_logo` | `false` | Show a Windows logo on the left |
| `clock_24h` | `false` | Use 24h time |
| `use_accent_color` | `true` | Tint bar with your accent color |
