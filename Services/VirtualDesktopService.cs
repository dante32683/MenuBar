using System;
using Microsoft.Win32;

namespace MenuBar.Services
{
    public static class VirtualDesktopService
    {
        private const string VdRoot =
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VirtualDesktops";

        /// <summary>
        /// Returns the current virtual desktop's display name ("Work", "Desktop 2", etc.)
        /// by reading the registry directly. No window handle or COM required — works on
        /// empty desktops with no focused app.
        /// </summary>
        public static string GetCurrentDesktopLabel()
        {
            try
            {
                using var vdKey = Registry.CurrentUser.OpenSubKey(VdRoot);
                if (vdKey == null) return null;

                // CurrentVirtualDesktop = 16-byte packed GUID of the active desktop.
                // Windows updates this atomically before EVENT_SYSTEM_DESKTOPSWITCH fires.
                if (vdKey.GetValue("CurrentVirtualDesktop") is not byte[] currentBytes
                    || currentBytes.Length < 16)
                    return null;

                Guid currentId = new Guid(currentBytes);

                // VirtualDesktopIDs = packed array of 16-byte GUIDs in display order.
                if (vdKey.GetValue("VirtualDesktopIDs") is not byte[] blob
                    || blob.Length < 16)
                    return null;

                int ordinal = -1;
                int count = blob.Length / 16;
                for (int i = 0; i < count; i++)
                {
                    byte[] chunk = new byte[16];
                    Array.Copy(blob, i * 16, chunk, 0, 16);
                    if (new Guid(chunk) == currentId) { ordinal = i + 1; break; }
                }

                if (ordinal < 0) return null;

                // Custom name lives under Desktops\{GUID}\Name; absent = user never renamed it.
                string guidKey = $@"{VdRoot}\Desktops\{currentId:B}";
                using var desktopKey = Registry.CurrentUser.OpenSubKey(guidKey);
                string name = desktopKey?.GetValue("Name") as string;

                return !string.IsNullOrWhiteSpace(name) ? name : $"Desktop {ordinal}";
            }
            catch
            {
                return null;
            }
        }
    }
}
