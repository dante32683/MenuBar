using System;
using System.Collections.Generic;
using System.Linq;
using Windows.Devices.Power;
using Windows.Networking.Connectivity;
using Windows.System.Power;

namespace MenuBar.Services
{
    public class HardwareService
    {
        private readonly Queue<int> _chargeRateHistory = new Queue<int>();
        private const int MaxChargeRateHistory = 6;

        public class BatteryInfo
        {
            public bool HasBattery { get; set; }
            public int Percent { get; set; }
            public bool IsCalculating { get; set; }
            public bool Charging { get; set; }
            public bool PluggedIn { get; set; }
            public bool EnergySaverOn { get; set; }
            public int? SecondsRemaining { get; set; }
            public int? AverageChargeRateInMilliwatts { get; set; }
            public int? RemainingCapacityInMilliwattHours { get; set; }
            public int? FullChargeCapacityInMilliwattHours { get; set; }
        }

        public class NetworkInfo
        {
            public bool Connected { get; set; }
            public bool IsLimited { get; set; }
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
                    info.EnergySaverOn = status.SystemStatusFlag == 1
                        || (NativeMethods.PowerGetEffectiveOverlayScheme(out Guid overlayGuid) == 0
                            && overlayGuid == NativeMethods.PowerSaverOverlayGuid);
                    info.IsCalculating = status.BatteryLifePercent == byte.MaxValue;
                    info.Percent = info.IsCalculating ? 0 : status.BatteryLifePercent;
                    var batteryReport = Battery.AggregateBattery.GetReport();
                    info.RemainingCapacityInMilliwattHours = batteryReport.RemainingCapacityInMilliwattHours;
                    info.FullChargeCapacityInMilliwattHours = batteryReport.FullChargeCapacityInMilliwattHours;
                    
                    if (batteryReport.ChargeRateInMilliwatts.HasValue)
                    {
                        _chargeRateHistory.Enqueue(batteryReport.ChargeRateInMilliwatts.Value);
                        while (_chargeRateHistory.Count > MaxChargeRateHistory)
                        {
                            _chargeRateHistory.Dequeue();
                        }
                        
                        if (_chargeRateHistory.Count > 0)
                        {
                            info.AverageChargeRateInMilliwatts = (int)_chargeRateHistory.Average();
                        }
                    }
                    
                    // Lenovo conservation mode keeps Status=Charging but ChargeRateInMilliwatts=0.
                    // Use actual current flow as ground truth when available; fall back to status flag otherwise.
                    info.Charging = batteryReport.ChargeRateInMilliwatts.HasValue
                        ? batteryReport.ChargeRateInMilliwatts.Value > 0
                        : batteryReport.Status == BatteryStatus.Charging;
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
                var profile = NetworkInformation.GetInternetConnectionProfile();
                if (profile == null)
                {
                    return info;
                }

                var connectivity = profile.GetNetworkConnectivityLevel();
                if (connectivity == NetworkConnectivityLevel.None)
                {
                    return info;
                }

                info.Connected = true;
                info.IsLimited = connectivity < NetworkConnectivityLevel.InternetAccess;

                if (profile.IsWlanConnectionProfile)
                {
                    info.IsWifi = true;
                    var wlanDetails = profile.WlanConnectionProfileDetails;
                    info.Ssid = wlanDetails?.GetConnectedSsid() ?? string.Empty;

                    var signalBars = profile.GetSignalBars();
                    info.SignalLevel = signalBars.HasValue ? (int)signalBars.Value switch
                    {
                        >= 4 => 3,
                        >= 2 => 2,
                        >= 1 => 1,
                        _ => 0
                    } : 0;
                }

                var adapter = profile.NetworkAdapter;
                if (adapter != null)
                {
                    if (adapter.InboundMaxBitsPerSecond > 0)
                    {
                        info.ReceiveRateMbps = (int)(adapter.InboundMaxBitsPerSecond / 1_000_000);
                    }

                    if (adapter.OutboundMaxBitsPerSecond > 0)
                    {
                        info.TransmitRateMbps = (int)(adapter.OutboundMaxBitsPerSecond / 1_000_000);
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
