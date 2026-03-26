using System;
using Microsoft.Win32;

namespace MenuBar.Services
{
    public static class VirtualDesktopService
    {
        private const string VdRoot =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

        private static readonly NativeMethods.IVirtualDesktopManager _manager =
            (NativeMethods.IVirtualDesktopManager)new NativeMethods.VirtualDesktopManager();

        /// <summary>
        /// Returns the current virtual desktop's display name ("Work", "Desktop 2", etc.).
        /// Prefers IVirtualDesktopManager to identify which desktop a window is on,
        /// then falls back to Registry for the user-defined name or ordinal.
        /// </summary>
        public static string GetCurrentDesktopLabel(IntPtr hwnd)
        {
            try
            {
                // 1. Get the GUID of the desktop the window is on
                if (_manager.GetWindowDesktopId(hwnd, out Guid desktopId) != 0)
                {
                    // Fallback to active desktop if window ID fails (e.g. not yet fully initialized)
                    return GetActiveDesktopLabelFromRegistry();
                }

                return GetDesktopLabelFromGuid(desktopId);
            }
            catch
            {
                return GetActiveDesktopLabelFromRegistry();
            }
        }

        private static string GetDesktopLabelFromGuid(Guid desktopId)
        {
            try
            {
                // Custom name lives under Desktops\{GUID}\Name
                string guidKey = $@"{VdRoot}\Desktops\{desktopId:B}";
                using var desktopKey = Registry.CurrentUser.OpenSubKey(guidKey);
                string name = desktopKey?.GetValue("Name") as string;
                if (!string.IsNullOrWhiteSpace(name)) return name;

                // Ordinal calculation: find the GUID's index in the VirtualDesktopIDs blob
                using var vdKey = Registry.CurrentUser.OpenSubKey(VdRoot);
                if (vdKey?.GetValue("VirtualDesktopIDs") is byte[] blob)
                {
                    int count = blob.Length / 16;
                    for (int i = 0; i < count; i++)
                    {
                        byte[] chunk = new byte[16];
                        Array.Copy(blob, i * 16, chunk, 0, 16);
                        if (new Guid(chunk) == desktopId) return $"Desktop {i + 1}";
                    }
                }

                return "Desktop 1";
            }
            catch { return "Desktop 1"; }
        }

        private static string GetActiveDesktopLabelFromRegistry()
        {
            try
            {
                using var vdKey = Registry.CurrentUser.OpenSubKey(VdRoot);
                if (vdKey == null) return null;

                if (vdKey.GetValue("CurrentVirtualDesktop") is not byte[] currentBytes
                    || currentBytes.Length < 16)
                    return null;

                return GetDesktopLabelFromGuid(new Guid(currentBytes));
            }
            catch { return null; }
        }
    }
}
