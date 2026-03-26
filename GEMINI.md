# Gemini Project Context: MenuBar

## Overview
**MenuBar** is a macOS-style menu bar for Windows built with **WinUI 3** and **.NET 8**. It docks to the top of the screen and displays active window info, app menus, media controls, network/battery status, and a clock.

**Key Tech:** WinUI 3, .NET 8.0, MVVM, Win32 P/Invoke (window management, event hooks), UI Automation (app menu extraction).

## Building and Running
- **Build:** `dotnet build`
- **Publish:** `dotnet publish -c Release -r win-x64 -p:Platform=x64 -o publish/win-x64`
- **Run:** Execute `MenuBar.exe`.

## Architecture & Conventions
- **UI & Logic (`MainWindow.xaml/.cs`):** Manages AppBar registration, UI rendering, and system hooks (`EVENT_SYSTEM_FOREGROUND`, etc.).
- **Services (`Services/`):**
  - **Win32 & Interop:** `NativeMethods.cs` for P/Invoke, `UiaMenuService.cs` for UI Automation menu extraction.
  - **System Data:** `HardwareService.cs` (Battery/Network), `MediaService.cs` (SMTC), `VirtualDesktopService.cs`.
- **Thread Safety:** Use `DispatcherQueue.TryEnqueue` when updating UI from background threads/hooks.
- **Resource Cleanup:** Always unhook events (`UnhookWinEvent`) and remove AppBar (`ABM_REMOVE`) on window close.
- **DPI Scaling:** Be mindful of logical (DIPs) vs physical pixels when mixing WinUI XAML and Win32 APIs (like `SetWindowPos`).
- **Configuration:** Settings are managed by `SettingsService.cs` and saved in `settings.json`.
