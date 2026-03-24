using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.NetworkInformation;
using System.Text.RegularExpressions;

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
            public int? SecondsRemaining { get; set; }
        }

        public class NetworkInfo
        {
            public bool Connected { get; set; }
            public bool IsWifi { get; set; }
            public string Ssid { get; set; }
            public int SignalLevel { get; set; }
            public int? ReceiveRateMbps { get; set; }
            public int? TransmitRateMbps { get; set; }
        }

        public BatteryInfo GetBatteryInfo()
        {
            var info = new BatteryInfo { HasBattery = false, PluggedIn = true };

            try
            {
                if (NativeMethods.GetSystemPowerStatus(out var status))
                {
                    bool hasBattery = status.BatteryFlag != 128;
                    info.HasBattery = hasBattery;

                    if (!hasBattery)
                    {
                        return info;
                    }

                    info.PluggedIn = status.ACLineStatus == 1;
                    info.Percent = status.BatteryLifePercent == byte.MaxValue ? 0 : status.BatteryLifePercent;
                    info.Charging = info.PluggedIn && info.Percent < 99;
                    if (status.BatteryLifeTime != uint.MaxValue)
                    {
                        info.SecondsRemaining = (int)status.BatteryLifeTime;
                    }
                }
            }
            catch
            {
            }

            return info;
        }

        public NetworkInfo GetNetworkInfo()
        {
            var info = new NetworkInfo { Connected = false, IsWifi = false, Ssid = string.Empty };

            try
            {
                var interfaces = NetworkInterface.GetAllNetworkInterfaces()
                    .Where(i => i.OperationalStatus == OperationalStatus.Up &&
                                i.NetworkInterfaceType != NetworkInterfaceType.Loopback &&
                                i.NetworkInterfaceType != NetworkInterfaceType.Tunnel);

                var activeInterface = interfaces.FirstOrDefault(i => i.GetIPProperties().GatewayAddresses.Any());
                var wlan = ParseWlanDetails();

                if (activeInterface != null)
                {
                    info.Connected = true;
                    if (activeInterface.NetworkInterfaceType == NetworkInterfaceType.Wireless80211)
                    {
                        info.IsWifi = true;
                        info.Ssid = activeInterface.Name;
                    }
                    else if (activeInterface.Speed > 0)
                    {
                        int speedMbps = (int)(activeInterface.Speed / 1_000_000);
                        info.ReceiveRateMbps = speedMbps;
                        info.TransmitRateMbps = speedMbps;
                    }
                }

                if (string.Equals(GetValue(wlan, "State"), "connected", StringComparison.OrdinalIgnoreCase))
                {
                    info.Connected = true;
                    info.IsWifi = true;
                    info.Ssid = GetValue(wlan, "SSID");
                    info.SignalLevel = ParseSignal(GetValue(wlan, "Signal"));
                    info.ReceiveRateMbps = ParseInt(GetValue(wlan, "Receive rate (Mbps)"));
                    info.TransmitRateMbps = ParseInt(GetValue(wlan, "Transmit rate (Mbps)"));
                }
            }
            catch
            {
            }

            return info;
        }

        private static Dictionary<string, string> ParseWlanDetails()
        {
            var data = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var process = new Process();
                process.StartInfo.FileName = "netsh";
                process.StartInfo.Arguments = "wlan show interfaces";
                process.StartInfo.UseShellExecute = false;
                process.StartInfo.RedirectStandardOutput = true;
                process.StartInfo.RedirectStandardError = true;
                process.StartInfo.CreateNoWindow = true;

                process.Start();
                string output = process.StandardOutput.ReadToEnd();
                process.WaitForExit(3000);

                foreach (string rawLine in output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries))
                {
                    string line = rawLine.Trim();
                    int separator = line.IndexOf(':');
                    if (separator <= 0)
                    {
                        continue;
                    }

                    string key = line.Substring(0, separator).Trim();
                    string value = line.Substring(separator + 1).Trim();
                    if (!data.ContainsKey(key))
                    {
                        data[key] = value;
                    }
                }
            }
            catch
            {
            }

            return data;
        }

        private static string GetValue(Dictionary<string, string> data, string key)
        {
            return data.TryGetValue(key, out string value) ? value : string.Empty;
        }

        private static int ParseSignal(string value)
        {
            int percent = ParseInt(Regex.Replace(value ?? string.Empty, @"[^\d]", string.Empty)) ?? 0;
            if (percent >= 67)
            {
                return 3;
            }

            if (percent >= 34)
            {
                return 2;
            }

            return percent > 0 ? 1 : 0;
        }

        private static int? ParseInt(string value)
        {
            return int.TryParse(value, out int parsed) ? parsed : null;
        }
    }
}
