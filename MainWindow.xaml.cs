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

namespace MenuBar
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new MainViewModel();

        private readonly HardwareService _hwService = new HardwareService();
        private readonly MediaService _mediaService;

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

        private IntPtr _hwnd;
        private DispatcherTimer _clockTimer;
        private DispatcherTimer _batteryTimer;
        private IntPtr _foregroundHook;
        private IntPtr _titleChangeHook;
        private NativeMethods.WinEventDelegate _foregroundDelegate;
        private NativeMethods.WinEventDelegate _titleChangeDelegate;
        private bool _appBarRegistered;
        private MenuBarSettings _settings = MenuBarSettings.CreateDefault();
        private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
        private HardwareService.BatteryInfo _batteryInfo = new HardwareService.BatteryInfo();
        private HardwareService.NetworkInfo _networkInfo = new HardwareService.NetworkInfo();

        public MainWindow()
        {
            InitializeComponent();
            _hwnd = WindowNative.GetWindowHandle(this);
            _mediaService = new MediaService(DispatcherQueue);
            Closed += Window_Closed;

            LoadSettings(applyLayout: false);
            ConfigureWindow();
            SetupTimers();
            SetupForegroundHook();

            UpdateClock();
            UpdateActiveWindow();
            UpdateBattery();
            UpdateNetwork();

            _mediaService.StateChanged += OnMediaStateChanged;
            _ = _mediaService.InitializeAsync();

            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            _uiSettings.ColorValuesChanged += OnAccentColorChanged;
        }

        #region Window Configuration

        private void ConfigureWindow()
        {
            if (DesktopAcrylicController.IsSupported())
            {
                SystemBackdrop = new DesktopAcrylicBackdrop();
            }
            else if (MicaController.IsSupported())
            {
                SystemBackdrop = new MicaBackdrop { Kind = MicaKind.Base };
            }

            AppWindow appWindow = AppWindow.GetFromWindowId(
                Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
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
                0, 0, 0, 0,
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

        #endregion

        #region Timers & Event Hooks

        private void SetupTimers()
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => UpdateClock();
            _clockTimer.Start();

            _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(30) };
            _batteryTimer.Tick += (_, _) => UpdateBattery();
            _batteryTimer.Start();
        }

        private void SetupForegroundHook()
        {
            _foregroundDelegate = OnForegroundEvent;
            _foregroundHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                NativeMethods.EVENT_SYSTEM_FOREGROUND,
                IntPtr.Zero, _foregroundDelegate, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);

            _titleChangeDelegate = OnTitleChangeEvent;
            _titleChangeHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_OBJECT_NAMECHANGE,
                NativeMethods.EVENT_OBJECT_NAMECHANGE,
                IntPtr.Zero, _titleChangeDelegate, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        private void OnForegroundEvent(IntPtr hook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint threadId, uint time)
        {
            UpdateActiveWindow();
        }

        private void OnTitleChangeEvent(IntPtr hook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint threadId, uint time)
        {
            if (hwnd == NativeMethods.GetForegroundWindow())
            {
                UpdateActiveWindow();
            }
        }

        private void OnNetworkStatusChanged(object sender)
        {
            DispatcherQueue.TryEnqueue(UpdateNetwork);
        }

        private void OnAccentColorChanged(Windows.UI.ViewManagement.UISettings sender, object args)
        {
            DispatcherQueue.TryEnqueue(ApplyBackgroundColor);
        }

        private void OnMediaStateChanged(MediaService.MediaState state)
        {
            ApplyMediaState(state);
        }

        #endregion

        #region Settings

        private void LoadSettings(bool applyLayout)
        {
            SettingsService.EnsureExists();
            _settings = SettingsService.Load();
            ApplySettings();

            if (applyLayout)
            {
                RegisterOrUpdateAppBar(registerIfNeeded: false);
                UpdateClock();
                ApplyMediaState(_mediaService?.CurrentState ?? MediaService.MediaState.Empty);
                UpdateBattery();
                UpdateNetwork();
            }
        }

        private void ApplyBackgroundColor()
        {
            Windows.UI.Color c;
            if (_settings.UseAccentColor)
            {
                var accent = _uiSettings.GetColorValue(Windows.UI.ViewManagement.UIColorType.AccentDark2);
                c = Microsoft.UI.ColorHelper.FromArgb(0xB0, accent.R, accent.G, accent.B);
            }
            else
            {
                c = Microsoft.UI.ColorHelper.FromArgb(0xB0, 0x1C, 0x22, 0x2A);
            }
            BackgroundBorder.Background = new SolidColorBrush(c);
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

            ApplyMediaState(_mediaService?.CurrentState ?? MediaService.MediaState.Empty);
            ApplyBackgroundColor();
        }

        #endregion

        #region Update Methods

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

        private void UpdateBattery()
        {
            _batteryInfo = _hwService.GetBatteryInfo();
            if (_batteryInfo.HasBattery)
            {
                ViewModel.BatteryText = $"{_batteryInfo.Percent}%";
                ViewModel.BatteryIcon = GetBatteryFillGlyph(_batteryInfo.Percent);
                var brush = GetBatteryFillBrush(_batteryInfo.Percent, _batteryInfo.Charging, _batteryInfo.PluggedIn);
                BatteryFillGlyphText.Foreground = brush;

                BatteryOutlineGlyphText.Visibility = (brush == _batteryDefaultBrush)
                    ? Visibility.Collapsed
                    : Visibility.Visible;

                BatteryBoltPath.Visibility = _batteryInfo.Charging
                    ? Visibility.Visible
                    : Visibility.Collapsed;

                string status = _batteryInfo.Charging
                    ? "Charging"
                    : (_batteryInfo.PluggedIn ? "Plugged in, fully charged" : "On battery");
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
        }

        private void UpdateNetwork()
        {
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

        private void ApplyMediaState(MediaService.MediaState state)
        {
            if (_settings.ShowMedia && state.HasContent)
            {
                string display = BuildMediaDisplay(state);
                ViewModel.MediaText = display;
                ViewModel.MediaTooltip = display;
                ViewModel.MediaIndicatorBrush = state.Playing ? _mediaPlayingBrush : _mediaPausedBrush;
                ViewModel.MediaVisibility = Visibility.Visible;

                ViewModel.MediaTitle = state.Title;
                ViewModel.MediaArtist = state.Artist;
                ViewModel.MediaAlbumCover = state.AlbumCover;
                ViewModel.MediaPlayPauseSymbol = state.Playing ? Symbol.Pause : Symbol.Play;
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

        #endregion

        #region Flyout Helpers

        private void UpdateBatteryFlyout()
        {
            if (!_batteryInfo.HasBattery)
            {
                BatteryMenuPrimaryItem.Text = "AC power \u2014 no battery detected";
                BatteryMenuSecondaryItem.Visibility = Visibility.Collapsed;
                BatteryMenuTimeItem.Visibility = Visibility.Collapsed;
                return;
            }

            BatteryMenuPrimaryItem.Text = $"{_batteryInfo.Percent}%";
            BatteryMenuSecondaryItem.Text = _batteryInfo.Charging
                ? "Charging..."
                : (_batteryInfo.PluggedIn ? "Plugged in, fully charged" : "On battery power");
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
                NetworkMenuSecondaryItem.Text = _networkInfo.SignalLevel switch
                {
                    1 => "Wi-Fi  \u00b7  Weak",
                    2 => "Wi-Fi  \u00b7  Fair",
                    3 => "Wi-Fi  \u00b7  Strong",
                    _ => "Wi-Fi"
                };
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

        #endregion

        #region Event Handlers

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
            _ = _mediaService.SendPreviousAsync();
        }

        private void MediaPlayPause_Click(object sender, RoutedEventArgs e)
        {
            _ = _mediaService.SendPlayPauseAsync();
        }

        private void MediaNext_Click(object sender, RoutedEventArgs e)
        {
            _ = _mediaService.SendNextAsync();
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

        #endregion

        #region Utilities

        private static Visibility ToVisibility(bool value)
        {
            return value ? Visibility.Visible : Visibility.Collapsed;
        }

        private static string BuildMediaDisplay(MediaService.MediaState state)
        {
            if (!string.IsNullOrWhiteSpace(state.Title) && !string.IsNullOrWhiteSpace(state.Artist))
            {
                return $"{state.Title} \u2014 {state.Artist}";
            }

            return !string.IsNullOrWhiteSpace(state.Title) ? state.Title : state.Artist;
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

        private static string GetBatteryFillGlyph(int percent)
        {
            int bucket = Math.Clamp((int)Math.Round(percent / 10.0), 0, 10);
            return MobileBatteryGlyphs[bucket];
        }

        private SolidColorBrush GetBatteryFillBrush(int percent, bool charging, bool pluggedIn)
        {
            if (charging || pluggedIn)
            {
                return _batteryChargingBrush;
            }

            return percent <= 20 ? _batterySaverBrush : _batteryDefaultBrush;
        }

        private static void SetSpeedMenuItem(MenuFlyoutItem item, string label, int? speed, int? compareWith = null)
        {
            if (!speed.HasValue || (compareWith.HasValue && compareWith.Value == speed.Value))
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

        #endregion

        #region Cleanup

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            _clockTimer?.Stop();
            _batteryTimer?.Stop();

            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
            }

            if (_titleChangeHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_titleChangeHook);
            }

            _mediaService?.Dispose();

            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged -= OnNetworkStatusChanged;
            _uiSettings.ColorValuesChanged -= OnAccentColorChanged;

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

        #endregion
    }
}
