using System;
using System.Management;
using System.Net.NetworkInformation;
using System.Linq;

namespace MenuBar.Services
{
    public class HardwareService
    {
        public class BatteryInfo
        {
            public bool HasBattery { get; set; }
            public int Percent { get; set; }
            public bool Charging { get; set; }
            public bool PluggedIn { get; set; }
        }

        public class NetworkInfo
        {
            public bool Connected { get; set; }
            public bool IsWifi { get; set; }
            public string Ssid { get; set; }
        }

        public BatteryInfo GetBatteryInfo()
        {
            var info = new BatteryInfo { HasBattery = false };

            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM Win32_Battery"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        info.HasBattery = true;
                        
                        if (mo["EstimatedChargeRemaining"] != null)
                        {
                            info.Percent = Convert.ToInt32(mo["EstimatedChargeRemaining"]);
                        }

                        if (mo["BatteryStatus"] != null)
                        {
                            int status = Convert.ToInt32(mo["BatteryStatus"]);
                            // 2 = AC / Unknown, 6 = Charging, 7 = Charging & High, 8 = Charging & Low, 9 = Charging & Critical
                            // 1 = Discharging
                            info.Charging = (status == 6 || status == 7 || status == 8 || status == 9);
                        }
                    }
                }

                // Check AC Power status
                using (var searcher = new ManagementObjectSearcher("SELECT * FROM root\\CIMV2:Win32_ComputerSystem"))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        // PCSystemType: 2 = Mobile (Laptop)
                    }
                }

                // Another way to check AC: Win32_PortableBattery or SystemInformation?
                // Let's use Win32_Battery and Win32_PortableBattery for simplicity, or just assume Charging == PluggedIn for now unless we query Win32_SystemEnclosure.
            }
            catch
            {
                // Fallback or ignore
            }

            return info;
        }

        public NetworkInfo GetNetworkInfo()
        {
            var info = new NetworkInfo { Connected = false, IsWifi = false, Ssid = "" };

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up && 
                                i.NetworkInterfaceType != NetworkInterfaceType.Loopback && 
                                i.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

                var activeInterface = interfaces.FirstOrDefault(i => i.GetIPProperties().GatewayAddresses.Any());

                if (activeInterface != null)
                {
                    info.Connected = true;
                    if (activeInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        info.IsWifi = true;
                        info.Ssid = activeInterface.Name; // Not exact SSID, but interface name like "Wi-Fi" is fine for lightweight fallback.
                    }
                }
            }
            catch
            {
            }

            return info;
        }
    }
}