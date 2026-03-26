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
using Microsoft.UI.Xaml.Media.Imaging;
using System.Runtime.InteropServices.WindowsRuntime;
using MenuBar.Services;
using MenuBar.ViewModels;
using Windows.Devices.Power;
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
        private readonly SolidColorBrush _pillNormalBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(0x1A, 255, 255, 255));
        private readonly SolidColorBrush _batteryDefaultBrush =
            new SolidColorBrush(Microsoft.UI.Colors.White);
        private readonly SolidColorBrush _batteryChargingBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x6A, 0xC4, 0x5B));
        private readonly SolidColorBrush _batteryPluggedBrush =
            new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 0x00, 0xB7, 0xC3));
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
        private IntPtr _lastForegroundHwnd = IntPtr.Zero;
        private IntPtr _lastExternalForegroundHwnd;
        private MenuBarSettings _settings = MenuBarSettings.CreateDefault();
        private readonly Windows.UI.ViewManagement.UISettings _uiSettings = new();
        private HardwareService.BatteryInfo _batteryInfo = new HardwareService.BatteryInfo();
        private HardwareService.NetworkInfo _networkInfo = new HardwareService.NetworkInfo();
        private IntPtr _desktopSwitchHook;
        private NativeMethods.WinEventDelegate _desktopSwitchDelegate;
        private IntPtr _appMenuTargetHwnd;

        private uint _taskbarCreatedMsg;
        private NativeMethods.SUBCLASSPROC _subclassProc;

        private sealed class AppMenuItem
        {
            public uint Index;
            public IntPtr TargetHwnd;
            public IntPtr SubMenuHandle; // Win32 path — non-zero when using TrackPopupMenu
            public object UiaElement;   // UIA path — non-null when using ExpandOrInvoke
        }

        public MainWindow()
        {
            InitializeComponent();
            _hwnd = WindowNative.GetWindowHandle(this);
            _mediaService = new MediaService(DispatcherQueue);
            Closed += Window_Closed;

            // Register shell messages and subclass window for system events
            _taskbarCreatedMsg = NativeMethods.RegisterWindowMessage("TaskbarCreated");
            _subclassProc = NewWindowProc;
            NativeMethods.SetWindowSubclass(_hwnd, _subclassProc, 0, IntPtr.Zero);

            LoadSettings(applyLayout: false);
            ConfigureWindow();
            SetupTimers();
            SetupForegroundHook();

            UpdateClock();
            UpdateActiveWindow();
            UpdateVirtualDesktop();
            UpdateBattery();
            UpdateNetwork();

            _mediaService.StateChanged += OnMediaStateChanged;
            _ = _mediaService.InitializeAsync();

            Windows.Networking.Connectivity.NetworkInformation.NetworkStatusChanged += OnNetworkStatusChanged;
            _uiSettings.ColorValuesChanged += OnAccentColorChanged;
        }

        private IntPtr NewWindowProc(IntPtr hWnd, uint uMsg, IntPtr wParam, IntPtr lParam, uint uIdSubclass, IntPtr dwRefData)
        {
            if (uMsg == _taskbarCreatedMsg)
            {
                // Explorer restarted — re-register AppBar to restore docking
                RegisterOrUpdateAppBar(registerIfNeeded: true);
            }
            else if (uMsg == NativeMethods.WM_DPICHANGED)
            {
                // DPI changed (e.g. moved between monitors) — recalculate physical layout
                RegisterOrUpdateAppBar(registerIfNeeded: false);
            }
            else if (uMsg == NativeMethods.WM_DISPLAYCHANGE)
            {
                // Resolution or monitor count changed — verify bar bounds
                RegisterOrUpdateAppBar(registerIfNeeded: false);
            }

            return NativeMethods.DefSubclassProc(hWnd, uMsg, wParam, lParam);
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
            // Keep the bar out of Alt-Tab while still behaving like a normal top-docked AppBar.
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
            // Use DisplayArea to find the monitor where our window is (or should be).
            // This is much more reliable on multi-monitor setups than SM_XVIRTUALSCREEN.
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd);
            var displayArea = DisplayArea.GetFromWindowId(windowId, DisplayAreaFallback.Primary);
            
            var monitorBounds = displayArea.OuterBounds;
            var workArea = displayArea.WorkArea;

            // barHeight is in DIPs (logical pixels) as configured by the user.
            int barHeight = _settings.GetEffectiveBarHeight();
            uint dpi = NativeMethods.GetDpiForWindow(_hwnd);
            double dpiScale = dpi > 0 ? dpi / 96.0 : 1.0;
            int physBarHeight = (int)Math.Round(barHeight * dpiScale);

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

            // Target the specific monitor's bounds in virtual screen coordinates
            abd.rc.left = monitorBounds.X;
            abd.rc.top = monitorBounds.Y;
            abd.rc.right = monitorBounds.X + monitorBounds.Width;
            abd.rc.bottom = monitorBounds.Y + physBarHeight;

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
            // Re-assert our desired height in case QUERYPOS moved it
            abd.rc.bottom = abd.rc.top + physBarHeight;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

            // XAML properties are in DIPs
            BackgroundBorder.Height = barHeight;
            MainContentGrid.Height = barHeight;

            // Final window placement on the correct monitor
            NativeMethods.SetWindowPos(
                _hwnd,
                (IntPtr)NativeMethods.HWND_TOPMOST,
                abd.rc.left,
                abd.rc.top,
                abd.rc.right - abd.rc.left,
                physBarHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        #endregion

        #region Timers & Event Hooks

        private void SetupTimers()
        {
            _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            _clockTimer.Tick += (_, _) => { UpdateClock(); UpdateVirtualDesktop(); };
            _clockTimer.Start();

            _batteryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _batteryTimer.Tick += (_, _) => UpdateBattery();
            _batteryTimer.Start();

            Battery.AggregateBattery.ReportUpdated += OnBatteryReportUpdated;
        }

        private void OnBatteryReportUpdated(Battery sender, object args)
            => DispatcherQueue.TryEnqueue(UpdateBattery);

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

            _desktopSwitchDelegate = OnDesktopSwitchEvent;
            _desktopSwitchHook = NativeMethods.SetWinEventHook(
                NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH,
                NativeMethods.EVENT_SYSTEM_DESKTOPSWITCH,
                IntPtr.Zero, _desktopSwitchDelegate, 0, 0,
                NativeMethods.WINEVENT_OUTOFCONTEXT);
        }

        private void OnForegroundEvent(IntPtr hook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint threadId, uint time)
        {
            if (hwnd != IntPtr.Zero && hwnd != _hwnd)
                _lastExternalForegroundHwnd = hwnd;
            UpdateActiveWindow();
            // UpdateVirtualDesktop is NOT called here — CurrentVirtualDesktop only changes
            // on desktop switches, which are handled by OnDesktopSwitchEvent exclusively.
        }

        private void OnDesktopSwitchEvent(IntPtr hook, uint eventType, IntPtr hwnd,
            int idObject, int idChild, uint threadId, uint time)
        {
            _appMenuTargetHwnd = IntPtr.Zero; // force menu rebuild on next foreground event
            UpdateVirtualDesktop();
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
            ViewModel.VirtualDesktopVisibility = ToVisibility(_settings.ShowVirtualDesktop);
            AppMenuPanel.Visibility = ToVisibility(_settings.ShowAppMenu);
            ViewModel.LogoTooltip = "Power and system menu";

            int barHeight = _settings.GetEffectiveBarHeight();
            ViewModel.IconFontSize = _settings.FontSizeIcon > 0 ? _settings.FontSizeIcon : barHeight * 0.62;
            ViewModel.TextFontSize = _settings.FontSizeText > 0 ? _settings.FontSizeText : barHeight * 0.44;
            double hPad = Math.Round(barHeight * 0.29);
            ViewModel.HostCornerRadius = new CornerRadius(Math.Round(barHeight * 0.21));
            ViewModel.HostPadding = new Thickness(hPad, 0, hPad, 0);
            ViewModel.BatteryIconWidth = ViewModel.IconFontSize + 4;

            ApplyMediaState(_mediaService?.CurrentState ?? MediaService.MediaState.Empty);
            ApplyBackgroundColor();

            if (_settings.ShowAppMenu)
            {
                _appMenuTargetHwnd = IntPtr.Zero;  // force rebuild with new font size
                _lastForegroundHwnd = IntPtr.Zero; // force UpdateActiveWindow to call RefreshAppMenu
                RefreshAppMenu();                  // also try now in case a non-bar window is focused
            }
            else
            {
                AppMenuPanel.Children.Clear();
            }
        }

        #endregion

        #region Update Methods

        private void UpdateClock()
        {
            DateTime now = DateTime.Now;
            string timeFmt = _settings.Clock24h
                ? (_settings.ClockShowSeconds ? "HH:mm:ss" : "HH:mm")
                : (_settings.ClockShowSeconds ? "h:mm:ss tt" : "h:mm tt");

            string clockText;
            if (_settings.ClockShowDate)
            {
                try
                {
                    clockText = now.ToString(_settings.ClockDateFormat + "  " + timeFmt);
                }
                catch
                {
                    clockText = now.ToString("MM/dd/yyyy  " + timeFmt);
                }
            }
            else
            {
                clockText = now.ToString(timeFmt);
            }

            ViewModel.ClockText = clockText;
            ViewModel.ClockTooltip = now.ToString("dddd, MMMM dd, yyyy h:mm:ss tt");
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

            ViewModel.ActiveWindowTitleTooltip = title;
            if (_settings.TitleMaxLength > 0 && title.Length > _settings.TitleMaxLength)
                title = title[.._settings.TitleMaxLength] + "\u2026";
            ViewModel.ActiveWindowTitle = title;

            // Only re-fetch icon and menu when the foreground window itself changes, not on title-only updates.
            // This avoids GDI allocation on every browser tab title change, etc.
            if (foreground != _lastForegroundHwnd)
            {
                _lastForegroundHwnd = foreground;
                ImageSource icon = GetWindowIconBitmap(foreground);
                ViewModel.ActiveWindowIcon = icon;
                ViewModel.ActiveWindowIconVisibility = icon != null ? Visibility.Visible : Visibility.Collapsed;

                if (_settings.ShowAppMenu)
                    RefreshAppMenu();
            }
        }

        private static ImageSource GetWindowIconBitmap(IntPtr hwnd)
        {
            IntPtr hIcon = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL2, 0);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, NativeMethods.ICON_SMALL, 0);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.SendMessage(hwnd, NativeMethods.WM_GETICON, NativeMethods.ICON_BIG, 0);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICONSM);
            if (hIcon == IntPtr.Zero)
                hIcon = NativeMethods.GetClassLongPtr(hwnd, NativeMethods.GCLP_HICON);
            if (hIcon == IntPtr.Zero)
                return null;

            return HIconToWriteableBitmap(hIcon);
        }

        private static WriteableBitmap HIconToWriteableBitmap(IntPtr hIcon)
        {
            int size = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXSMICON);
            if (size <= 0) size = 16;

            var bmi = new NativeMethods.BITMAPINFOHEADER
            {
                biSize = Marshal.SizeOf<NativeMethods.BITMAPINFOHEADER>(),
                biWidth = size,
                biHeight = -size,   // negative = top-down scan order
                biPlanes = 1,
                biBitCount = 32,
                biCompression = 0   // BI_RGB
            };

            IntPtr hdc = NativeMethods.CreateCompatibleDC(IntPtr.Zero);
            if (hdc == IntPtr.Zero) return null;

            IntPtr hBitmap = NativeMethods.CreateDIBSection(
                hdc, ref bmi, NativeMethods.DIB_RGB_COLORS,
                out IntPtr ppvBits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero)
            {
                NativeMethods.DeleteDC(hdc);
                return null;
            }

            IntPtr oldBitmap = NativeMethods.SelectObject(hdc, hBitmap);
            NativeMethods.DrawIconEx(hdc, 0, 0, hIcon, size, size, 0, IntPtr.Zero, NativeMethods.DI_NORMAL);
            NativeMethods.SelectObject(hdc, oldBitmap);

            byte[] pixels = new byte[size * size * 4];
            Marshal.Copy(ppvBits, pixels, 0, pixels.Length);

            NativeMethods.DeleteObject(hBitmap);
            NativeMethods.DeleteDC(hdc);

            // Legacy icons have no alpha channel — synthesize opacity from color data
            bool hasAlpha = false;
            for (int i = 3; i < pixels.Length; i += 4)
            {
                if (pixels[i] != 0) { hasAlpha = true; break; }
            }
            if (!hasAlpha)
            {
                for (int i = 0; i < pixels.Length; i += 4)
                {
                    if (pixels[i] != 0 || pixels[i + 1] != 0 || pixels[i + 2] != 0)
                        pixels[i + 3] = 255;
                }
            }

            // WriteableBitmap.PixelBuffer expects premultiplied BGRA8
            for (int i = 0; i < pixels.Length; i += 4)
            {
                byte a = pixels[i + 3];
                if (a == 0 || a == 255) continue;
                pixels[i]     = (byte)(pixels[i]     * a / 255);
                pixels[i + 1] = (byte)(pixels[i + 1] * a / 255);
                pixels[i + 2] = (byte)(pixels[i + 2] * a / 255);
            }

            var wb = new WriteableBitmap(size, size);
            using (var stream = wb.PixelBuffer.AsStream())
                stream.Write(pixels, 0, pixels.Length);
            wb.Invalidate();
            return wb;
        }

        private void UpdateBattery()
        {
            _batteryInfo = _hwService.GetBatteryInfo();
            if (_batteryInfo.HasBattery)
            {
                if (_batteryInfo.IsCalculating)
                {
                    ViewModel.BatteryText = "--%";
                    ViewModel.BatteryIcon = MobileBatteryGlyphs[5];
                    BatteryFillGlyphText.Foreground = _batteryDefaultBrush;
                    BatteryOutlineGlyphText.Visibility = Visibility.Collapsed;
                    ViewModel.BatteryTooltip = "Battery: Calculating...";
                }
                else
                {
                    ViewModel.BatteryText = $"{_batteryInfo.Percent}%";
                    ViewModel.BatteryIcon = GetBatteryFillGlyph(_batteryInfo.Percent);
                    var brush = GetBatteryFillBrush(_batteryInfo.Percent, _batteryInfo.Charging, _batteryInfo.PluggedIn, _batteryInfo.EnergySaverOn);
                    BatteryFillGlyphText.Foreground = brush;

                    BatteryOutlineGlyphText.Visibility = (brush == _batteryDefaultBrush)
                        ? Visibility.Collapsed
                        : Visibility.Visible;

                    string status = _batteryInfo.Charging
                        ? "Charging"
                        : (_batteryInfo.PluggedIn ? "Plugged in, fully charged" : "On battery");
                    ViewModel.BatteryTooltip = $"Battery: {_batteryInfo.Percent}%\n{status}";
                }
            }
            else
            {
                ViewModel.BatteryText = "AC";
                ViewModel.BatteryIcon = "\uEC02";
                ViewModel.BatteryTooltip = "Battery: AC power";
                BatteryFillGlyphText.Foreground = _batteryDefaultBrush;
                BatteryOutlineGlyphText.Visibility = Visibility.Collapsed;
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
                ViewModel.NetworkTooltip = _networkInfo.IsLimited
                    ? $"Network: {ssid} (Limited)"
                    : $"Network: {ssid}";
            }
            else
            {
                ViewModel.NetworkIcon = "\uE839";
                ViewModel.NetworkTooltip = _networkInfo.IsLimited
                    ? "Network: Ethernet (Limited)"
                    : "Network: Ethernet";
            }
        }

        private void UpdateVirtualDesktop()
        {
            if (!_settings.ShowVirtualDesktop) return;

            // Uses IVirtualDesktopManager if possible, falling back to registry
            string label = VirtualDesktopService.GetCurrentDesktopLabel(_hwnd);
            if (label != null)
                ViewModel.VirtualDesktopText = label;
        }

        private async void RefreshAppMenu()
        {
            if (!_settings.ShowAppMenu) return;

            IntPtr foreground = NativeMethods.GetForegroundWindow();
            if (foreground == IntPtr.Zero || foreground == _hwnd)
            {
                AppMenuPanel.Children.Clear();
                _appMenuTargetHwnd = IntPtr.Zero;
                return;
            }

            // Walk to root — GetMenu only works on the top-level frame, not child controls
            IntPtr hwnd = NativeMethods.GetAncestor(foreground, NativeMethods.GA_ROOT);
            if (hwnd == IntPtr.Zero || hwnd == _hwnd) hwnd = foreground;

            // Skip rebuild if targeting the same window
            if (hwnd == _appMenuTargetHwnd) return;
            _appMenuTargetHwnd = hwnd;

            AppMenuPanel.Children.Clear();

            IntPtr hMenu = NativeMethods.GetMenu(hwnd);
            if (hMenu == IntPtr.Zero)
            {
                // No Win32 HMENU — try UI Automation (WPF apps, VS Code, Win11 Notepad, etc.) on background thread
                var uiaItems = await UiaMenuService.GetMenuItemsAsync(hwnd);
                if (uiaItems != null && hwnd == _appMenuTargetHwnd)
                {
                    DispatcherQueue.TryEnqueue(() =>
                    {
                        // Verify we are still targeting the same window after the async wait
                        if (hwnd != _appMenuTargetHwnd) return;

                        double uiaFontSize = ViewModel.TextFontSize;
                        foreach (var uiaItem in uiaItems)
                        {
                            AddMenuItemBorder(uiaItem.Label, false, uiaFontSize,
                                new AppMenuItem { TargetHwnd = hwnd, UiaElement = uiaItem.Element });
                        }
                    });
                }
                return;
            }

            int count = NativeMethods.GetMenuItemCount(hMenu);
            if (count <= 0) return;

            double fontSize = ViewModel.TextFontSize;

            // Allocate a single native buffer for all GetMenuItemInfo calls in this loop.
            // Using IntPtr avoids relying on the managed-string P/Invoke marshaling path,
            // which does not reliably round-trip the written text back into mii.dwTypeData.
            const int bufChars = 256;
            IntPtr textBuf = Marshal.AllocHGlobal(bufChars * 2); // UTF-16, 2 bytes per char
            try
            {
                for (uint i = 0; i < (uint)count; i++)
                {
                    // Zero the buffer so PtrToStringUni gets a clean null-terminated string
                    // even if GetMenuItemInfo writes fewer characters than expected.
                    Marshal.WriteInt16(textBuf, 0);

                    var mii = new NativeMethods.MENUITEMINFO
                    {
                        cbSize = (uint)Marshal.SizeOf<NativeMethods.MENUITEMINFO>(),
                        fMask = NativeMethods.MIIM_FTYPE | NativeMethods.MIIM_STATE |
                                NativeMethods.MIIM_STRING | NativeMethods.MIIM_SUBMENU,
                        dwTypeData = textBuf,
                        cch = bufChars - 1  // leave room for null terminator
                    };

                    if (!NativeMethods.GetMenuItemInfo(hMenu, i, true, ref mii)) continue;
                    if ((mii.fType & NativeMethods.MFT_SEPARATOR) != 0) continue;
                    if ((mii.fType & NativeMethods.MFT_BITMAP) != 0) continue;

                    string raw = Marshal.PtrToStringUni(textBuf) ?? string.Empty;
                    string label = raw.Split('\t')[0].Replace("&", string.Empty).Trim();
                    if (string.IsNullOrWhiteSpace(label)) continue;

                    bool grayed = (mii.fState & NativeMethods.MFS_GRAYED) != 0;
                    IntPtr subMenu = mii.hSubMenu;

                    AddMenuItemBorder(label, grayed, fontSize,
                        new AppMenuItem { Index = i, TargetHwnd = hwnd, SubMenuHandle = subMenu });
                }
            }
            finally
            {
                Marshal.FreeHGlobal(textBuf);
            }
        }

        private void AddMenuItemBorder(string label, bool grayed, double fontSize, AppMenuItem tag)
        {
            var textBlock = new TextBlock
            {
                Text = label,
                FontFamily = new FontFamily("Segoe UI Variable, Segoe UI"),
                FontSize = fontSize,
                Foreground = grayed
                    ? new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 120, 120, 120))
                    : new SolidColorBrush(Microsoft.UI.Colors.White),
                VerticalAlignment = VerticalAlignment.Center
            };

            var border = new Border
            {
                Background = _transparentBrush,
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 0, 8, 0),
                VerticalAlignment = VerticalAlignment.Stretch,
                Tag = tag,
                Child = textBlock
            };

            if (!grayed)
            {
                border.PointerEntered += Host_PointerEntered;
                border.PointerExited += Host_PointerExited;
                border.PointerPressed += Host_PointerPressed;
                border.PointerReleased += Host_PointerReleased;
                border.Tapped += AppMenuItem_Tapped;
            }

            AppMenuPanel.Children.Add(border);
        }

        private void ApplyMediaState(MediaService.MediaState state)
        {
            if (_settings.ShowMedia && state.HasContent)
            {
                string display = BuildMediaDisplay(state);
                ViewModel.MediaTooltip = display;
                if (_settings.MediaMaxLength > 0 && display.Length > _settings.MediaMaxLength)
                    display = display[.._settings.MediaMaxLength] + "\u2026";
                ViewModel.MediaText = display;
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
                BatteryFlyoutPercent.Text = "AC Power";
                BatteryFlyoutStatus.Text = "No battery detected";
                BatteryFlyoutIcon.Text = "\uEC02";
                BatteryFlyoutIcon.Foreground = _batteryDefaultBrush;
                BatteryFlyoutTime.Visibility = Visibility.Collapsed;
                return;
            }

            if (_batteryInfo.IsCalculating)
            {
                BatteryFlyoutPercent.Text = "--%";
                BatteryFlyoutStatus.Text = "Calculating...";
                BatteryFlyoutIcon.Text = MobileBatteryGlyphs[5];
                BatteryFlyoutIcon.Foreground = _batteryDefaultBrush;
                BatteryFlyoutTime.Visibility = Visibility.Collapsed;
                return;
            }

            BatteryFlyoutPercent.Text = $"{_batteryInfo.Percent}%";
            BatteryFlyoutStatus.Text = _batteryInfo.Charging
                ? "Charging"
                : (_batteryInfo.PluggedIn
                    ? (_batteryInfo.Percent >= 99 ? "Plugged in, fully charged" : "Smart charging")
                    : "On battery power");
            BatteryFlyoutIcon.Text = GetBatteryFillGlyph(_batteryInfo.Percent);
            BatteryFlyoutIcon.Foreground = GetBatteryFillBrush(_batteryInfo.Percent, _batteryInfo.Charging, _batteryInfo.PluggedIn, _batteryInfo.EnergySaverOn);

            string remaining = FormatRemainingTime(_batteryInfo.SecondsRemaining);
            if (string.IsNullOrWhiteSpace(remaining))
            {
                BatteryFlyoutTime.Visibility = Visibility.Collapsed;
            }
            else
            {
                BatteryFlyoutTime.Text = remaining;
                BatteryFlyoutTime.Visibility = Visibility.Visible;
            }
        }

        private void UpdateNetworkFlyout()
        {
            if (!_networkInfo.Connected)
            {
                NetworkFlyoutIcon.Text = "\uEB55";
                NetworkFlyoutName.Text = "Not connected";
                NetworkFlyoutStatus.Text = "No internet access";
                NetworkFlyoutSpeed.Visibility = Visibility.Collapsed;
                return;
            }

            if (_networkInfo.IsWifi)
            {
                NetworkFlyoutIcon.Text = "\uE701";
                string ssid = string.IsNullOrWhiteSpace(_networkInfo.Ssid) ? "Wi-Fi" : _networkInfo.Ssid;
                NetworkFlyoutName.Text = ssid;
                NetworkFlyoutStatus.Text = _networkInfo.IsLimited
                    ? "Limited connectivity"
                    : _networkInfo.SignalLevel switch
                    {
                        1 => "Wi-Fi  \u00b7  Weak signal",
                        2 => "Wi-Fi  \u00b7  Fair signal",
                        3 => "Wi-Fi  \u00b7  Strong signal",
                        _ => "Wi-Fi"
                    };
            }
            else
            {
                NetworkFlyoutIcon.Text = "\uE839";
                NetworkFlyoutName.Text = "Ethernet";
                NetworkFlyoutStatus.Text = _networkInfo.IsLimited ? "Limited connectivity" : "Connected";
            }

            if (_networkInfo.ReceiveRateMbps.HasValue)
            {
                NetworkFlyoutSpeed.Text = $"Link speed: {_networkInfo.ReceiveRateMbps.Value} Mbps";
                NetworkFlyoutSpeed.Visibility = Visibility.Visible;
            }
            else
            {
                NetworkFlyoutSpeed.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region Event Handlers

        private void LogoHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleAttachedFlyout(GetHostBorder(sender));
        }

        private void MediaHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            ToggleAttachedFlyout(GetHostBorder(sender));
        }

        private void NetworkHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            UpdateNetworkFlyout();
            ToggleAttachedFlyout(GetHostBorder(sender));
        }

        private void BatteryHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            UpdateBattery();
            UpdateBatteryFlyout();
            ToggleAttachedFlyout(GetHostBorder(sender));
        }

        private void VirtualDesktopHost_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b) b.Background = _hoverBrush;
        }

        private void VirtualDesktopHost_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b) b.Background = _pillNormalBrush;
        }

        private void VirtualDesktopHost_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b) b.Background = _pressedBrush;
        }

        private void VirtualDesktopHost_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (sender is Border b) b.Background = _hoverBrush;
        }

        private void VirtualDesktopHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            SendKeyChord(NativeMethods.VK_LWIN, NativeMethods.VK_TAB);
        }

        private void AppMenuItem_Tapped(object sender, TappedRoutedEventArgs e)
        {
            if (sender is not Border border) return;
            if (border.Tag is not AppMenuItem item) return;

            if (item.SubMenuHandle != IntPtr.Zero)
            {
                // Win32 path: show the app's submenu as a native popup from our bar
                GeneralTransform transform = border.TransformToVisual(null);
                Windows.Foundation.Point pt = transform.TransformPoint(
                    new Windows.Foundation.Point(0, border.ActualHeight));
                double scale = Content.XamlRoot?.RasterizationScale ?? 1.0;
                int sx = (int)(pt.X * scale) + NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
                int sy = (int)(pt.Y * scale) + NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);

                int cmd = NativeMethods.TrackPopupMenu(
                    item.SubMenuHandle,
                    NativeMethods.TPM_TOPALIGN | NativeMethods.TPM_LEFTALIGN |
                    NativeMethods.TPM_RETURNCMD | NativeMethods.TPM_NONOTIFY,
                    sx, sy, 0, _hwnd, IntPtr.Zero);

                if (cmd > 0)
                    NativeMethods.PostMessage(item.TargetHwnd, NativeMethods.WM_COMMAND,
                        (IntPtr)cmd, IntPtr.Zero);
            }
            else if (item.UiaElement != null)
            {
                // UIA path: ask the app to expand its own menu dropdown.
                // The dropdown opens in the target app's window at its original position.
                UiaMenuService.ExpandOrInvokeMenuItem(item.UiaElement);
            }
        }

        private void ClockHost_Tapped(object sender, TappedRoutedEventArgs e)
        {
            // PointerPressed is too late here; WinUI has already triggered the window activation/focus change.
            if (IsShellExperienceHostWindow(_lastExternalForegroundHwnd))
            {
                _lastExternalForegroundHwnd = IntPtr.Zero;
                return;
            }
            SendKeyChord(NativeMethods.VK_LWIN, NativeMethods.VK_N);
        }

        private static bool IsShellExperienceHostWindow(IntPtr hwnd)
        {
            if (hwnd == IntPtr.Zero) return false;
            NativeMethods.GetWindowThreadProcessId(hwnd, out uint pid);
            try
            {
                // ShellExperienceHost is the notification center host on Windows 11; class names are not reliable.
                return Process.GetProcessById((int)pid).ProcessName
                    .Equals("ShellExperienceHost", StringComparison.OrdinalIgnoreCase);
            }
            catch { return false; }
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

        // When a host widget uses a transparent outer Grid for Fitts's Law hit-test expansion,
        // the sender is a Grid; find the first Border child which carries the squircle background.
        private static Border GetHostBorder(object sender)
        {
            if (sender is Border b) return b;
            if (sender is Grid g && g.Children.Count > 0 && g.Children[0] is Border child) return child;
            return null;
        }

        private void Host_PointerEntered(object sender, PointerRoutedEventArgs e)
        {
            if (GetHostBorder(sender) is Border border)
                border.Background = _hoverBrush;
        }

        private void Host_PointerExited(object sender, PointerRoutedEventArgs e)
        {
            if (GetHostBorder(sender) is Border border)
                border.Background = _transparentBrush;
        }

        private void Host_PointerPressed(object sender, PointerRoutedEventArgs e)
        {
            // Visual feedback only; do not use this for clock state detection.
            if (GetHostBorder(sender) is Border border)
                border.Background = _pressedBrush;
        }

        private void Host_PointerReleased(object sender, PointerRoutedEventArgs e)
        {
            if (GetHostBorder(sender) is Border border)
                border.Background = _hoverBrush;
        }

        private void ClockEdge_PointerEntered(object sender, PointerRoutedEventArgs e)
            => ClockHost.Background = _hoverBrush;

        private void ClockEdge_PointerExited(object sender, PointerRoutedEventArgs e)
            => ClockHost.Background = _transparentBrush;

        private void ClockEdge_PointerPressed(object sender, PointerRoutedEventArgs e)
            => ClockHost.Background = _pressedBrush;

        private void ClockEdge_PointerReleased(object sender, PointerRoutedEventArgs e)
            => ClockHost.Background = _hoverBrush;

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

        private SolidColorBrush GetBatteryFillBrush(int percent, bool charging, bool pluggedIn, bool energySaverOn)
        {
            if (charging) return _batteryChargingBrush;
            if (pluggedIn) return _batteryPluggedBrush;
            if (energySaverOn) return _batterySaverBrush;
            return percent <= 20 ? _batterySaverBrush : _batteryDefaultBrush;
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
            Battery.AggregateBattery.ReportUpdated -= OnBatteryReportUpdated;

            if (_foregroundHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_foregroundHook);
            }

            if (_titleChangeHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_titleChangeHook);
            }

            if (_desktopSwitchHook != IntPtr.Zero)
            {
                NativeMethods.UnhookWinEvent(_desktopSwitchHook);
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
