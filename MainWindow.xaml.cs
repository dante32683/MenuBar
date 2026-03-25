using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Composition.SystemBackdrops;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using MenuBar.Services;
using MenuBar.ViewModels;
using WinRT.Interop;
using Windows.Media.Control;

namespace MenuBar
{
    public sealed partial class MainWindow : Window
    {
        private sealed class MediaState
        {
            public string Title { get; set; } = string.Empty;
            public string Artist { get; set; } = string.Empty;
            public bool Playing { get; set; }
            public Microsoft.UI.Xaml.Media.ImageSource AlbumCover { get; set; }

            public bool HasContent =>
                !string.IsNullOrWhiteSpace(Title) || !string.IsNullOrWhiteSpace(Artist);

            public static MediaState Empty => new MediaState();
        }

        public MainViewModel ViewModel { get; } = new MainViewModel();

        private readonly HardwareService _hwService = new HardwareService();
        private readonly SolidColorBrush _mediaPlayingBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x6A, 0xC4, 0x5B));
        private readonly SolidColorBrush _mediaPausedBrush =
            new SolidColorBrush(Microsoft.UI.Colors.White);
        private readonly SolidColorBrush _mediaInactiveBrush =
            new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private readonly SolidColorBrush _hoverBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(38, 255, 255, 255));
        private readonly SolidColorBrush _pressedBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(25, 255, 255, 255));
        private readonly SolidColorBrush _transparentBrush =
            new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        private readonly SolidColorBrush _batteryDefaultBrush =
            new SolidColorBrush(Microsoft.UI.Colors.White);
        private readonly SolidColorBrush _batteryChargingBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x6A, 0xC4, 0x5B));
        private readonly SolidColorBrush _batterySaverBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0xEA, 0xA3, 0x00));
        private static readonly string[] MobileBatteryGlyphs =
        {
            "\uEBA0", "\uEBA1", "\uEBA2", "\uEBA3", "\uEBA4",
            "\uEBA5", "\uEBA6", "\uEBA7", "\uEBA8", "\uEBA9",
            "\uEBAA"
        };
        private static readonly string[] MobileBatteryChargingGlyphs =
        {
            "\uEBAB", "\uEBAC", "\uEBAD", "\uEBAE", "\uEBAF",
            "\uEBB0", "\uEBB1", "\uEBB2", "\uEBB3", "\uEBB4",
            "\uEBB5"
        };

        private IntPtr _hwnd;
        private DispatcherTimer _timer;
        private GlobalSystemMediaTransportControlsSessionManager _mediaManager;
        private int _tickCount;
        private bool _appBarRegistered;
        private MenuBarSettings _settings = MenuBarSettings.CreateDefault();
        private HardwareService.BatteryInfo _batteryInfo = new HardwareService.BatteryInfo();
        private HardwareService.NetworkInfo _networkInfo = new HardwareService.NetworkInfo();
        private MediaState _mediaState = MediaState.Empty;

        public MainWindow()
        {
            InitializeComponent();
            _hwnd = WindowNative.GetWindowHandle(this);
            Closed += Window_Closed;

            LoadSettings(applyLayout: false);
            ConfigureWindow();
            SetupTimer();
            UpdateClock();
            UpdateActiveWindow();
            UpdateHardware();
            _ = InitMediaManagerAsync();
        }

        private void ConfigureWindow()
        {
            if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            }

            AppWindow appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            int exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle = (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            NativeMethods.SetWindowPos(
                _hwnd,
                IntPtr.Zero,
                0,
                0,
                0,
                0,
                NativeMethods.SWP_NOMOVE |
                NativeMethods.SWP_NOSIZE |
                NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_FRAMECHANGED |
                NativeMethods.SWP_NOACTIVATE);

            RegisterOrUpdateAppBar(registerIfNeeded: true);
        }

        private void RegisterOrUpdateAppBar(bool registerIfNeeded)
        {
            int screenX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int screenY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int barHeight = _settings.GetEffectiveBarHeight();

            NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA
            {
                cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                hWnd = _hwnd,
                uEdge = NativeMethods.ABE_TOP
            };

            if (registerIfNeeded && !_appBarRegistered)
            {
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
                _appBarRegistered = true;
            }

            abd.rc.left = screenX;
            abd.rc.top = screenY;
            abd.rc.right = screenX + screenW;
            abd.rc.bottom = screenY + barHeight;

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
            abd.rc.bottom = abd.rc.top + barHeight;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

            BackgroundBorder.Height = barHeight;
            MainContentGrid.Height = barHeight;
            NativeMethods.SetWindowPos(
                _hwnd,
                (IntPtr)NativeMethods.HWND_TOPMOST,
                screenX,
                screenY,
                screenW,
                barHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        private void SetupTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500);
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private void Timer_Tick(object sender, object e)
        {
            _tickCount++;
            UpdateActiveWindow();

            if (_tickCount % 2 == 0)
            {
                UpdateClock();
            }

            if (_tickCount % 4 == 0)
            {
                UpdateMedia();
            }

            if (_tickCount % 20 == 0)
            {
                UpdateHardware();
            }
        }

        private async Task InitMediaManagerAsync()
        {
            try
            {
                _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                UpdateMedia();
            }
            catch
            {
                _mediaState = MediaState.Empty;
                ApplyMediaState();
            }
        }

        private void LoadSettings(bool applyLayout)
        {
            SettingsService.EnsureExists();
            _settings = SettingsService.Load();
            ApplySettings();

            if (applyLayout)
            {
                RegisterOrUpdateAppBar(registerIfNeeded: false);
                UpdateClock();
                ApplyMediaState();
                UpdateHardware();
            }
        }

        private void ApplySettings()
        {
            ViewModel.LogoVisibility = ToVisibility(_settings.ShowWindowsLogo);
            ViewModel.TitleVisibility = ToVisibility(_settings.ShowTitle);
            ViewModel.NetworkVisibility = ToVisibility(_settings.ShowNetwork);
            ViewModel.BatteryVisibility = ToVisibility(_settings.ShowBattery);
            ViewModel.ClockVisibility = ToVisibility(_settings.ShowClock);
            ViewModel.LogoTooltip = "Power and system menu";

            int barHeight = _settings.GetEffectiveBarHeight();
            ViewModel.IconFontSize = barHeight * 0.62;
            ViewModel.TextFontSize = barHeight * 0.44;

            ApplyMediaState();
        }

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            if (_settings.Clock24h)
            {
                ViewModel.ClockText = now.ToString("MM/dd/yyyy  HH:mm");
            }
            else
            {
                string hour = now.ToString("hh").TrimStart('0');
                if (string.IsNullOrEmpty(hour))
                {
                    hour = "12";
                }

                ViewModel.ClockText = now.ToString($"MM/dd/yyyy  {hour}:mm tt");
            }

            ViewModel.ClockTooltip = now.ToString("dddd, MMMM dd, yyyy hh:mm:ss tt");
        }

        private void UpdateActiveWindow()
        {
            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == _hwnd || foreground == IntPtr.Zero)
            {
                return;
            }

            StringBuilder builder = new StringBuilder(256);
            NativeMethods.GetWindowText(foreground, builder, builder.Capacity);
            string title = builder.ToString();
            if (string.IsNullOrWhiteSpace(title))
            {
                title = "Desktop";
            }

            ViewModel.ActiveWindowTitle = title;
            ViewModel.ActiveWindowTitleTooltip = title;
        }

        private async void UpdateMedia()
        {
            if (_mediaManager == null)
            {
                _mediaState = MediaState.Empty;
                ApplyMediaState();
                return;
            }

            GlobalSystemMediaTransportControlsSession session = _mediaManager.GetCurrentSession();
            if (session == null)
            {
                _mediaState = MediaState.Empty;
                ApplyMediaState();
                return;
            }

            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                var state = new MediaState
                {
                    Title = props?.Title ?? string.Empty,
                    Artist = props?.Artist ?? string.Empty,
                    Playing = playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing
                };
                
                if (props?.Thumbnail != null)
                {
                    try
                    {
                        var stream = await props.Thumbnail.OpenReadAsync();
                        if (stream != null)
                        {
                            var bitmap = new Microsoft.UI.Xaml.Media.Imaging.BitmapImage();
                            await bitmap.SetSourceAsync(stream);
                            state.AlbumCover = bitmap;
                        }
                    }
                    catch
                    {
                    }
                }
                
                _mediaState = state;
            }
            catch
            {
                _mediaState = MediaState.Empty;
            }

            ApplyMediaState();
        }

        private void ApplyMediaState()
        {
            if (_settings.ShowMedia && _mediaState.HasContent)
            {
                string display = BuildMediaDisplay(_mediaState);
                ViewModel.MediaText = display;
                ViewModel.MediaTooltip = display;
                ViewModel.MediaIndicatorBrush = _mediaState.Playing ? _mediaPlayingBrush : _mediaPausedBrush;
                ViewModel.MediaVisibility = Visibility.Visible;
                
                ViewModel.MediaTitle = _mediaState.Title;
                ViewModel.MediaArtist = _mediaState.Artist;
                ViewModel.MediaAlbumCover = _mediaState.AlbumCover;
                ViewModel.MediaPlayPauseSymbol = _mediaState.Playing ? Symbol.Pause : Symbol.Play;
            }
            else
            {
                ViewModel.MediaText = "Nothing playing";
                ViewModel.MediaTooltip = string.Empty;
                ViewModel.MediaIndicatorBrush = _mediaInactiveBrush;
                ViewModel.MediaVisibility = Visibility.Collapsed;
                
                ViewModel.MediaTitle = "Nothing playing";
                ViewModel.MediaArtist = string.Empty;
                ViewModel.MediaAlbumCover = null;
                ViewModel.MediaPlayPauseSymbol = Symbol.Play;
            }
        }

        private void UpdateHardware()
        {
            _batteryInfo = _hwService.GetBatteryInfo();
            if (_batteryInfo.HasBattery)
            {
                ViewModel.BatteryText = $"{_batteryInfo.Percent}%";
                ViewModel.BatteryIcon = GetBatteryFillGlyph(_batteryInfo.Percent);
                var brush = GetBatteryFillBrush(_batteryInfo.Percent, _batteryInfo.Charging);
                BatteryFillGlyphText.Foreground = brush;
                
                // Hide outline if the fill is white to avoid visual clashing
                BatteryOutlineGlyphText.Visibility = (brush == _batteryDefaultBrush) 
                    ? Visibility.Collapsed 
                    : Visibility.Visible;
                
                BatteryBoltPath.Visibility = _batteryInfo.Charging ? Visibility.Visible : Visibility.Collapsed;

                string status = _batteryInfo.Charging
                    ? "Charging"
                    : (_batteryInfo.PluggedIn ? "Plugged in" : "On battery");
                ViewModel.BatteryTooltip = $"Battery: {_batteryInfo.Percent}%\n{status}";
            }
            else
            {
                ViewModel.BatteryText = "AC";
                ViewModel.BatteryIcon = "\uEC02";
                ViewModel.BatteryTooltip = "Battery: AC power";
                BatteryFillGlyphText.Foreground = _batteryDefaultBrush;
                BatteryOutlineGlyphText.Visibility = Visibility.Collapsed;
                BatteryBoltPath.Visibility = Visibility.Collapsed;
            }

            _networkInfo = _hwService.GetNetworkInfo();
            if (!_networkInfo.Connected)
            {
                ViewModel.NetworkIcon = "\uEB55";
                ViewModel.NetworkTooltip = "Network: Not connected";
            }
            else if (_networkInfo.IsWifi)
            {
                ViewModel.NetworkIcon = "\uE701";
                string ssid = string.IsNullOrWhiteSpace(_networkInfo.Ssid) ? "Wi-Fi" : _networkInfo.Ssid;
                ViewModel.NetworkTooltip = $"Network: {ssid}";
            }
            else
            {
                ViewModel.NetworkIcon = "\uE839";
                ViewModel.NetworkTooltip = "Network: Ethernet";
            }
        }

        private static Visibility ToVisibility(bool value)
        {
            return value ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string BuildMediaDisplay(MediaState mediaState)
        {
            if (!string.IsNullOrWhiteSpace(mediaState.Title) && !string.IsNullOrWhiteSpace(mediaState.Artist))
            {
                return $"{mediaState.Title} — {mediaState.Artist}";
            }

            if (!string.IsNullOrWhiteSpace(mediaState.Title))
            {
                return mediaState.Title;
            }

            return mediaState.Artist;
        }

        private static string FormatRemainingTime(int? secondsRemaining)
        {
            if (!secondsRemaining.HasValue || secondsRemaining.Value <= 0)
            {
                return string.Empty;
            }

            TimeSpan span = TimeSpan.FromSeconds(secondsRemaining.Value);
            if (span.Hours > 0)
            {
                return $"{span.Hours}h {span.Minutes}m remaining";
            }

            return $"{Math.Max(1, span.Minutes)}m remaining";
        }

        private void UpdateBatteryFlyout()
        {
            if (!_batteryInfo.HasBattery)
            {
                BatteryMenuPrimaryItem.Text = "AC power — no battery detected";
                BatteryMenuSecondaryItem.Visibility = Visibility.Collapsed;
                BatteryMenuTimeItem.Visibility = Visibility.Collapsed;
                return;
            }

            BatteryMenuPrimaryItem.Text = $"{_batteryInfo.Percent}%";
            BatteryMenuSecondaryItem.Text = _batteryInfo.Charging
                ? "Charging..."
                : (_batteryInfo.PluggedIn ? "Plugged in, not charging" : "On battery power");
            BatteryMenuSecondaryItem.Visibility = Visibility.Visible;

            string remaining = FormatRemainingTime(_batteryInfo.SecondsRemaining);
            if (string.IsNullOrWhiteSpace(remaining))
            {
                BatteryMenuTimeItem.Visibility = Visibility.Collapsed;
            }
            else
            {
                BatteryMenuTimeItem.Text = $"Time remaining: {remaining}";
                BatteryMenuTimeItem.Visibility = Visibility.Visible;
            }
        }

        private void UpdateNetworkFlyout()
        {
            if (!_networkInfo.Connected)
            {
                NetworkMenuPrimaryItem.Text = "Not connected";
                NetworkMenuSecondaryItem.Visibility = Visibility.Collapsed;
                NetworkMenuDownItem.Visibility = Visibility.Collapsed;
                NetworkMenuUpItem.Visibility = Visibility.Collapsed;
                return;
            }

            if (_networkInfo.IsWifi)
            {
                string ssid = string.IsNullOrWhiteSpace(_networkInfo.Ssid) ? "Wi-Fi" : _networkInfo.Ssid;
                NetworkMenuPrimaryItem.Text = ssid;
                NetworkMenuSecondaryItem.Text = BuildWifiDetailText();
            }
            else
            {
                NetworkMenuPrimaryItem.Text = "Ethernet";
                NetworkMenuSecondaryItem.Text = "Connected";
            }

            NetworkMenuSecondaryItem.Visibility = Visibility.Visible;
            SetSpeedMenuItem(NetworkMenuDownItem, "Link speed (\u2193)", _networkInfo.ReceiveRateMbps);
            SetSpeedMenuItem(NetworkMenuUpItem, "Link speed (\u2191)", _networkInfo.TransmitRateMbps, _networkInfo.ReceiveRateMbps);
        }

        private string BuildWifiDetailText()
        {
            return _networkInfo.SignalLevel switch
            {
                1 => "Wi-Fi  ·  Weak",
                2 => "Wi-Fi  ·  Fair",
                3 => "Wi-Fi  ·  Strong",
                _ => "Wi-Fi"
            };
        }

        private static void SetSpeedMenuItem(MenuFlyoutItem item, string label, int? speed, int? compareWith = null)
        {
            if (!speed.HasValue || (compareWith.HasValue && compareWith.Value == speed.Value && item.Name == nameof(NetworkMenuUpItem)))
            {
                item.Visibility = Visibility.Collapsed;
                return;
            }

            item.Text = $"{label}: {speed.Value} Mbps";
            item.Visibility = Visibility.Visible;
        }

        private static void ToggleAttachedFlyout(FrameworkElement element)
        {
            FlyoutBase flyout = FlyoutBase.GetAttachedFlyout(element);
            if (flyout == null)
            {
                return;
            }

            if (flyout.IsOpen)
            {
                flyout.Hide();
                return;
            }

            FlyoutBase.ShowAttachedFlyout(element);
        }

        private void LogoHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleAttachedFlyout((FrameworkElement)sender);
        }

        private void MediaHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleAttachedFlyout((FrameworkElement)sender);
        }

        private void NetworkHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            UpdateNetworkFlyout();
            ToggleAttachedFlyout((FrameworkElement)sender);
        }

        private void BatteryHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            UpdateBatteryFlyout();
            ToggleAttachedFlyout((FrameworkElement)sender);
        }

        private void ClockHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SendKeyChord(NativeMethods.VK_LWIN, NativeMethods.VK_N);
        }

        private void OpenSettingsFile_Click(object sender, RoutedEventArgs e)
        {
            SettingsService.EnsureExists();
            OpenWithShell(SettingsService.SettingsPath);
        }

        private void ReloadSettings_Click(object sender, RoutedEventArgs e)
        {
            LoadSettings(applyLayout: true);
        }

        private void RestartBar_Click(object sender, RoutedEventArgs e)
        {
            RestartApplication();
        }

        private void StopBar_Click(object sender, RoutedEventArgs e)
        {
            App.Current.Exit();
        }

        private void OpenWindowsSettings_Click(object sender, RoutedEventArgs e)
        {
            OpenWithShell("ms-settings:");
        }

        private void SleepSystem_Click(object sender, RoutedEventArgs e)
        {
            RunBackgroundProcess("rundll32.exe", "powrprof.dll,SetSuspendState 0,1,0");
        }

        private void RestartSystem_Click(object sender, RoutedEventArgs e)
        {
            RunBackgroundProcess("shutdown", "/r /t 0");
        }

        private void ShutdownSystem_Click(object sender, RoutedEventArgs e)
        {
            RunBackgroundProcess("shutdown", "/s /t 0");
        }

        private void MediaPrevious_Click(object sender, RoutedEventArgs e)
        {
            SendKey(NativeMethods.VK_MEDIA_PREV_TRACK);
        }

        private void MediaPlayPause_Click(object sender, RoutedEventArgs e)
        {
            SendKey(NativeMethods.VK_MEDIA_PLAY_PAUSE);
            _mediaState.Playing = !_mediaState.Playing;
            ApplyMediaState();
        }

        private void Host_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = _hoverBrush;
            }
        }

        private void Host_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = _transparentBrush;
            }
        }

        private void Host_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = _pressedBrush;
            }
        }

        private void Host_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border border)
            {
                border.Background = _hoverBrush;
            }
        }

        private static string GetBatteryFillGlyph(int percent)
        {
            int bucket = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
            return MobileBatteryGlyphs[bucket];
        }

        private SolidColorBrush GetBatteryFillBrush(int percent, bool charging)
        {
            if (charging)
            {
                return _batteryChargingBrush;
            }

            if (percent <= 20)
            {
                return _batterySaverBrush;
            }

            return _batteryDefaultBrush;
        }

        private void MediaNext_Click(object sender, RoutedEventArgs e)
        {
            SendKey(NativeMethods.VK_MEDIA_NEXT_TRACK);
        }

        private static void SendKey(byte virtualKey)
        {
            NativeMethods.keybd_event(virtualKey, 0, 0, 0);
            NativeMethods.keybd_event(virtualKey, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        }

        private static void SendKeyChord(byte firstKey, byte secondKey)
        {
            NativeMethods.keybd_event(firstKey, 0, 0, 0);
            NativeMethods.keybd_event(secondKey, 0, 0, 0);
            NativeMethods.keybd_event(secondKey, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
            NativeMethods.keybd_event(firstKey, 0, NativeMethods.KEYEVENTF_KEYUP, 0);
        }

        private static void RunBackgroundProcess(string fileName, string arguments)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = fileName,
                    Arguments = arguments,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
            }
            catch
            {
            }
        }

        private static void OpenWithShell(string target)
        {
            try
            {
                Process.Start(new ProcessStartInfo
                {
                    FileName = target,
                    UseShellExecute = true
                });
            }
            catch
            {
            }
        }

        private void RestartApplication()
        {
            string exePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(exePath))
            {
                exePath = Path.Combine(AppContext.BaseDirectory, "MenuBar.exe");
            }

            if (!string.IsNullOrWhiteSpace(exePath) && File.Exists(exePath))
            {
                try
                {
                    Process.Start(new ProcessStartInfo
                    {
                        FileName = exePath,
                        UseShellExecute = true,
                        WorkingDirectory = Path.GetDirectoryName(exePath)
                    });
                }
                catch
                {
                }
            }

            App.Current.Exit();
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
            }

            if (_appBarRegistered)
            {
                NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA
                {
                    cbSize = Marshal.SizeOf<NativeMethods.APPBARDATA>(),
                    hWnd = _hwnd
                };
                NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
                _appBarRegistered = false;
            }
        }
    }
}
