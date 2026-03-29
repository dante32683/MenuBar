using System;
using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace MenuBar.Services
{
    /// <summary>
    /// Tracks equivalent discharge time since the last full charge.
    /// Persists state across app restarts. Accounts for partial charges,
    /// sleep/wake cycles, and variable drain rates.
    /// </summary>
    public sealed class BatteryUsageTracker : IDisposable
    {
        private sealed class PersistedState
        {
            public int AnchorMWh { get; set; }          // FullChargeCapacity at last 100%
            public double TotalDrainedMWh { get; set; } // mWh drained since last full charge
            public long TotalDischargeMs { get; set; }  // ms spent discharging since last full charge
        }

        private static readonly string _statePath =
            Path.Combine(AppContext.BaseDirectory, "usage_tracker.json");

        private readonly object _lock = new();
        private PersistedState _state = new();

        // In-flight discharge (not yet committed — current unplug session)
        private int? _dischargeStartMWh;
        private DateTime _dischargeStartTime;

        // Sleep/wake tracking
        private bool _resumedFromSleep;
        private int _energyAtSuspend;
        private DateTime _suspendTime;

        // Last reported energy (readable from the PowerModeChanged thread)
        private int _lastKnownMWh;

        public BatteryUsageTracker()
        {
            Load();
            SystemEvents.PowerModeChanged += OnPowerModeChanged;
        }

        /// <summary>
        /// Call on every battery update. Returns a display string like "3h 22m equivalent use",
        /// or null if there is not enough data yet.
        /// </summary>
        public string Update(int remainingMWh, int fullMWh, bool pluggedIn, bool isFullyCharged)
        {
            lock (_lock)
            {
                _lastKnownMWh = remainingMWh;

                // Post-sleep: commit the sleep drain and start fresh discharge tracking
                if (_resumedFromSleep)
                {
                    _resumedFromSleep = false;
                    if (!pluggedIn && _energyAtSuspend > 0)
                    {
                        double sleepDrain = Math.Max(0, _energyAtSuspend - remainingMWh);
                        double sleepMs = (DateTime.UtcNow - _suspendTime).TotalMilliseconds;
                        if (sleepDrain > 0 && sleepMs > 0)
                        {
                            _state.TotalDrainedMWh += sleepDrain;
                            _state.TotalDischargeMs += (long)sleepMs;
                        }
                    }
                    _energyAtSuspend = 0;
                    if (!pluggedIn)
                    {
                        _dischargeStartMWh = remainingMWh;
                        _dischargeStartTime = DateTime.UtcNow;
                    }
                    Save();
                }

                // State 1: Full charge — reset everything
                if (isFullyCharged)
                {
                    FinalizeDischarge(remainingMWh);
                    _state = new PersistedState { AnchorMWh = fullMWh };
                    _dischargeStartMWh = null;
                    Save();
                    return null;
                }

                if (pluggedIn)
                {
                    // State 3: Charging — finalize any in-flight discharge
                    if (_dischargeStartMWh.HasValue)
                    {
                        FinalizeDischarge(remainingMWh);
                        Save();
                    }
                }
                else
                {
                    // State 2: Discharging — start tracking if not already
                    if (!_dischargeStartMWh.HasValue)
                    {
                        _dischargeStartMWh = remainingMWh;
                        _dischargeStartTime = DateTime.UtcNow;
                    }
                }

                return ComputeDisplay(remainingMWh, pluggedIn);
            }
        }

        private string ComputeDisplay(int remainingMWh, bool pluggedIn)
        {
            if (_state.AnchorMWh <= 0) return null;

            double deficitMWh = _state.AnchorMWh - remainingMWh;
            if (deficitMWh <= 0) return null;

            // Include in-flight discharge session in rate calculation
            double totalDrained = _state.TotalDrainedMWh;
            double totalMs = _state.TotalDischargeMs;
            if (!pluggedIn && _dischargeStartMWh.HasValue)
            {
                totalDrained += Math.Max(0, _dischargeStartMWh.Value - remainingMWh);
                totalMs += (DateTime.UtcNow - _dischargeStartTime).TotalMilliseconds;
            }

            // Require at least 200 mWh of history for a reliable rate (~15-20 min of use)
            if (totalDrained < 200 || totalMs < 1) return null;

            double minutes = deficitMWh * totalMs / (totalDrained * 60_000.0);
            int h = (int)(minutes / 60);
            int m = (int)(minutes % 60);
            if (h == 0 && m == 0) return null;
            return h > 0 ? $"{h}h {m}m equivalent use" : $"{m}m equivalent use";
        }

        private void FinalizeDischarge(int currentMWh)
        {
            if (!_dischargeStartMWh.HasValue) return;
            double drained = Math.Max(0, _dischargeStartMWh.Value - currentMWh);
            double ms = (DateTime.UtcNow - _dischargeStartTime).TotalMilliseconds;
            if (drained > 0 && ms > 0)
            {
                _state.TotalDrainedMWh += drained;
                _state.TotalDischargeMs += (long)ms;
            }
            _dischargeStartMWh = null;
        }

        // State 4: Sleep — fired on a background thread; only touches lock-protected fields
        private void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            lock (_lock)
            {
                if (e.Mode == PowerModes.Suspend)
                {
                    _energyAtSuspend = _lastKnownMWh;
                    _suspendTime = DateTime.UtcNow;
                    // Commit discharge up to suspend time; post-sleep drain handled on Resume
                    if (_dischargeStartMWh.HasValue)
                    {
                        FinalizeDischarge(_lastKnownMWh);
                        Save();
                    }
                }
                else if (e.Mode == PowerModes.Resume)
                {
                    _resumedFromSleep = true;
                }
            }
        }

        private void Load()
        {
            try
            {
                if (File.Exists(_statePath))
                    _state = JsonSerializer.Deserialize<PersistedState>(
                        File.ReadAllText(_statePath)) ?? new();
            }
            catch { _state = new(); }
        }

        private void Save()
        {
            try { File.WriteAllText(_statePath, JsonSerializer.Serialize(_state)); }
            catch { }
        }

        public void Dispose()
        {
            SystemEvents.PowerModeChanged -= OnPowerModeChanged;
        }
    }
}
