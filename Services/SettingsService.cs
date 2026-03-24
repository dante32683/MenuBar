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

        public static MenuBarSettings CreateDefault()
        {
            return new MenuBarSettings();
        }

        public int GetEffectiveBarHeight()
        {
            return Math.Clamp(BarHeight, 28, 56);
        }

        public void Normalize()
        {
            BarHeight = GetEffectiveBarHeight();
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
