using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MenuBar.Services
{
    public sealed class MenuBarSettings
    {
        [JsonPropertyName("bar_height")]
        public int BarHeight { get; set; } = 28;

        [JsonPropertyName("show_battery")]
        public bool ShowBattery { get; set; } = true;

        [JsonPropertyName("show_network")]
        public bool ShowNetwork { get; set; } = true;

        [JsonPropertyName("show_clock")]
        public bool ShowClock { get; set; } = true;

        [JsonPropertyName("show_media")]
        public bool ShowMedia { get; set; } = true;

        [JsonPropertyName("show_title")]
        public bool ShowTitle { get; set; } = true;

        [JsonPropertyName("clock_24h")]
        public bool Clock24h { get; set; } = false;

        [JsonPropertyName("use_accent_color")]
        public bool UseAccentColor { get; set; } = true;

        [JsonPropertyName("show_windows_logo")]
        public bool ShowWindowsLogo { get; set; } = false;

        [JsonPropertyName("title_max_length")]
        public int TitleMaxLength { get; set; } = 0;

        [JsonPropertyName("media_max_length")]
        public int MediaMaxLength { get; set; } = 0;

        [JsonPropertyName("clock_show_seconds")]
        public bool ClockShowSeconds { get; set; } = false;

        [JsonPropertyName("clock_show_date")]
        public bool ClockShowDate { get; set; } = true;

        [JsonPropertyName("clock_date_format")]
        public string ClockDateFormat { get; set; } = "M/d/yyyy";

        [JsonPropertyName("show_projected_runtime")]
        public bool ShowProjectedRuntime { get; set; } = true;

        [JsonPropertyName("font_size_text")]
        public double FontSizeText { get; set; } = 0;

        [JsonPropertyName("font_size_icon")]
        public double FontSizeIcon { get; set; } = 0;

        [JsonPropertyName("show_app_menu")]
        public bool ShowAppMenu { get; set; } = false;

        [JsonPropertyName("show_virtual_desktop")]
        public bool ShowVirtualDesktop { get; set; } = false;

        [JsonPropertyName("enable_volume_scroll")]
        public bool EnableVolumeScroll { get; set; } = true;

        [JsonPropertyName("volume_scroll_threshold")]
        public int VolumeScrollThreshold { get; set; } = 120;

        [JsonPropertyName("battery_show_progress_bar")]
        public bool BatteryShowProgressBar { get; set; } = true;

        [JsonPropertyName("battery_show_usage_time")]
        public bool BatteryShowUsageTime { get; set; } = true;

        [JsonPropertyName("show_eye_break")]
        public bool ShowEyeBreak { get; set; } = true;

        public static MenuBarSettings CreateDefault()
        {
            return new MenuBarSettings();
        }

        public int GetEffectiveBarHeight()
        {
            return Math.Clamp(BarHeight, 24, 56);
        }

        public void Normalize()
        {
            BarHeight = GetEffectiveBarHeight();
            if (string.IsNullOrWhiteSpace(ClockDateFormat))
                ClockDateFormat = "M/d/yyyy";
            if (VolumeScrollThreshold <= 0)
                VolumeScrollThreshold = 120;
        }
    }

    public static class SettingsService
    {
        private static readonly JsonSerializerOptions JsonOptions = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = true
        };

        public static string SettingsPath => Path.Combine(AppContext.BaseDirectory, "settings.json");

        public static MenuBarSettings Load()
        {
            var settings = MenuBarSettings.CreateDefault();

            try
            {
                if (File.Exists(SettingsPath))
                {
                    var loaded = JsonSerializer.Deserialize<MenuBarSettings>(File.ReadAllText(SettingsPath), JsonOptions);
                    if (loaded != null)
                    {
                        settings = loaded;
                    }
                }
                else
                {
                    Save(settings);
                }
            }
            catch
            {
                try
                {
                    var backup = SettingsPath + ".bad-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    File.Move(SettingsPath, backup, overwrite: true);
                }
                catch { }
                Save(settings);
            }

            settings.Normalize();
            return settings;
        }

        public static void Save(MenuBarSettings settings)
        {
            settings.Normalize();
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOptions));
        }

        public static void EnsureExists()
        {
            if (!File.Exists(SettingsPath))
            {
                Save(MenuBarSettings.CreateDefault());
            }
        }
    }
}
