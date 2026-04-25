using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Encodings.Web;

namespace MenuBar.Services
{
    public sealed class MenuBarSettings
    {
        [JsonPropertyName("bar_height")]
        public int BarHeight { get; set; } = 24;

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
        public bool ShowVirtualDesktop { get; set; } = true;

        [JsonPropertyName("enable_volume_scroll")]
        public bool EnableVolumeScroll { get; set; } = true;

        [JsonPropertyName("volume_scroll_threshold")]
        public int VolumeScrollThreshold { get; set; } = 120;

        [JsonPropertyName("battery_show_progress_bar")]
        public bool BatteryShowProgressBar { get; set; } = false;

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

        private static readonly IReadOnlyDictionary<string, string> DefaultHelp = new Dictionary<string, string>
        {
            ["bar_height"] = "Bar height in pixels. Range: 24–56. Font sizes scale automatically unless overridden.",
            ["show_battery"] = "Show the battery icon and percentage. Set false to hide on desktop PCs.",
            ["show_network"] = "Show the Wi-Fi / Ethernet icon.",
            ["show_clock"] = "Show the clock / date widget.",
            ["show_media"] = "Show the media widget when something is playing. Auto-hides when nothing is playing.",
            ["show_title"] = "Show the active window title on the left side.",
            ["show_windows_logo"] = "Show the Windows logo button (power menu: sleep, restart, shut down).",
            ["clock_24h"] = "Use 24-hour time (true) or 12-hour with AM/PM (false).",
            ["clock_show_seconds"] = "Include seconds in the clock display.",
            ["clock_show_date"] = "Include the date alongside the time.",
            ["clock_date_format"] = "Date format string (C# DateTime). Examples: 'M/d/yyyy', 'dd/MM/yyyy', 'ddd MMM d', 'yyyy-MM-dd'.",
            ["use_accent_color"] = "Tint the bar background with your Windows accent color. false = dark grey.",
            ["title_max_length"] = "Hard-truncate the window title at N characters (appends …). 0 = no limit.",
            ["media_max_length"] = "Hard-truncate the media bar text at N characters (appends …). 0 = no limit.",
            ["font_size_text"] = "Override text font size in pixels (battery %, time, title). 0 = auto-scale from bar_height.",
            ["font_size_icon"] = "Override icon font size in pixels (battery, network glyphs). 0 = auto-scale from bar_height.",
            ["show_app_menu"] = "Show the active window's application menu (File, Edit, …) as buttons on the left side. Requires the foreground app to have a Win32 menu.",
            ["show_virtual_desktop"] = "Show the current virtual desktop label (Desktop 1, Desktop 2, …) on the left side.",
            ["enable_volume_scroll"] = "Enable system volume control by scrolling anywhere on the bar.",
            ["volume_scroll_threshold"] = "Cumulative scroll delta required to change volume (120 = one standard mouse wheel click).",
            ["show_projected_runtime"] = "Show battery runtime prediction in the battery flyout when enough charge-rate data is available.",
            ["battery_show_progress_bar"] = "Show a charge level progress bar in the battery flyout.",
            ["battery_show_usage_time"] = "Show 'usage since full charge' time in the battery flyout (requires discharge history to build up).",
            ["show_eye_break"] = "Show the 20-20-20 eye break dot indicator (driven by AHK via IPC)."
        };

        private static string SettingsDirectory
        {
            get
            {
                // Standard unpackaged-app convention: store per-user settings under LocalAppData.
                string baseDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                if (string.IsNullOrWhiteSpace(baseDir))
                {
                    // Fallback: still allow portable usage if LocalAppData isn't available.
                    return AppContext.BaseDirectory;
                }
                return Path.Combine(baseDir, "MenuBar");
            }
        }

        public static string SettingsPath => Path.Combine(SettingsDirectory, "settings.json");

        public static MenuBarSettings Load()
        {
            var settings = MenuBarSettings.CreateDefault();

            try
            {
                Directory.CreateDirectory(SettingsDirectory);

                if (File.Exists(SettingsPath))
                {
                    // The file may include an "_help" object. Unknown properties are ignored.
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
                    // Don't overwrite old settings. Move the broken file aside.
                    MoveAsideAsOld(SettingsPath);
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
            Directory.CreateDirectory(SettingsDirectory);
            File.WriteAllText(SettingsPath, SerializeWithHelp(settings, DefaultHelp));
        }

        public static void EnsureExists()
        {
            if (!File.Exists(SettingsPath))
            {
                Save(MenuBarSettings.CreateDefault());
            }
        }

        private static void MoveAsideAsOld(string path)
        {
            if (!File.Exists(path)) return;

            string dir = Path.GetDirectoryName(path) ?? SettingsDirectory;
            string oldPath = Path.Combine(dir, "settings.json.old");
            if (!File.Exists(oldPath))
            {
                File.Move(path, oldPath);
                return;
            }

            // Keep prior backups; pick the first available suffix.
            for (int i = 2; i <= 99; i++)
            {
                string candidate = Path.Combine(dir, $"settings.json.old{i}");
                if (!File.Exists(candidate))
                {
                    File.Move(path, candidate);
                    return;
                }
            }
        }

        private static string SerializeWithHelp(MenuBarSettings settings, IReadOnlyDictionary<string, string> help)
        {
            // Write a stable, human-editable settings file that always includes the _help section.
            using var settingsDoc = JsonDocument.Parse(JsonSerializer.Serialize(settings, JsonOptions));
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
            {
                Indented = true,
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            }))
            {
                writer.WriteStartObject();

                writer.WritePropertyName("_help");
                writer.WriteStartObject();
                foreach (var kvp in help)
                {
                    writer.WriteString(kvp.Key, kvp.Value);
                }
                writer.WriteEndObject();

                foreach (var prop in settingsDoc.RootElement.EnumerateObject())
                {
                    // In case we ever add internal/metadata properties, keep the on-disk format clean.
                    if (prop.NameEquals("_help")) continue;
                    prop.WriteTo(writer);
                }

                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }
    }
}
