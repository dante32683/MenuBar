using System;
using System.Text;
using System.Threading.Tasks;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Composition.SystemBackdrops;
using MenuBar.ViewModels;
using MenuBar.Services;
using WinRT.Interop;
using System.Runtime.InteropServices;
using Windows.Media.Control;

namespace MenuBar
{
    public sealed partial class MainWindow : Window
    {
        public MainViewModel ViewModel { get; } = new MainViewModel();
        private IntPtr _hwnd;
        private DispatcherTimer _timer;
        private GlobalSystemMediaTransportControlsSessionManager _mediaManager;
        private readonly HardwareService _hwService = new HardwareService();
        private int _tickCount;

        public MainWindow()
        {
            this.InitializeComponent();
            _hwnd = WindowNative.GetWindowHandle(this);
            Closed += Window_Closed;

            ConfigureWindow();
            SetupTimer();
            ViewModel.ClockText = DateTime.Now.ToString("MM/dd/yyyy  HH:mm");
            UpdateActiveWindow();
            UpdateHardware();
            _ = InitMediaManagerAsync();
        }

        private void ConfigureWindow()
        {
            // Set Mica Backdrop
            if (MicaController.IsSupported())
            {
                this.SystemBackdrop = new MicaBackdrop() { Kind = MicaKind.Base };
            }

            var appWindow = AppWindow.GetFromWindowId(Microsoft.UI.Win32Interop.GetWindowIdFromWindow(_hwnd));
            var presenter = appWindow.Presenter as OverlappedPresenter;
            if (presenter != null)
            {
                presenter.IsMaximizable = false;
                presenter.IsMinimizable = false;
                presenter.IsResizable = false;
                presenter.SetBorderAndTitleBar(false, false);
            }

            // Hide from Alt-Tab
            int exStyle = NativeMethods.GetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE);
            exStyle = (exStyle | NativeMethods.WS_EX_TOOLWINDOW) & ~NativeMethods.WS_EX_APPWINDOW;
            NativeMethods.SetWindowLong(_hwnd, NativeMethods.GWL_EXSTYLE, exStyle);

            NativeMethods.SetWindowPos(_hwnd, IntPtr.Zero, 0, 0, 0, 0,
                NativeMethods.SWP_NOMOVE | NativeMethods.SWP_NOSIZE | NativeMethods.SWP_NOZORDER |
                NativeMethods.SWP_FRAMECHANGED | NativeMethods.SWP_NOACTIVATE);

            RegisterAppBar();
        }

        private void RegisterAppBar()
        {
            int screenX = NativeMethods.GetSystemMetrics(NativeMethods.SM_XVIRTUALSCREEN);
            int screenY = NativeMethods.GetSystemMetrics(NativeMethods.SM_YVIRTUALSCREEN);
            int screenW = NativeMethods.GetSystemMetrics(NativeMethods.SM_CXVIRTUALSCREEN);
            int barHeight = 28;

            NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = _hwnd;
            abd.uEdge = NativeMethods.ABE_TOP;
            abd.rc.left = screenX;
            abd.rc.top = screenY;
            abd.rc.right = screenX + screenW;
            abd.rc.bottom = screenY + barHeight;

            NativeMethods.SHAppBarMessage(NativeMethods.ABM_NEW, ref abd);
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_QUERYPOS, ref abd);
            abd.rc.bottom = abd.rc.top + barHeight;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_SETPOS, ref abd);

            NativeMethods.SetWindowPos(_hwnd, (IntPtr)NativeMethods.HWND_TOPMOST, screenX, screenY, screenW, barHeight,
                NativeMethods.SWP_NOACTIVATE | NativeMethods.SWP_SHOWWINDOW);
        }

        private void SetupTimer()
        {
            _timer = new DispatcherTimer();
            _timer.Interval = TimeSpan.FromMilliseconds(500); // Fast interval for active window
            _timer.Tick += Timer_Tick;
            _timer.Start();
        }

        private async Task InitMediaManagerAsync()
        {
            try
            {
                _mediaManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                UpdateMedia();
            }
            catch { }
        }

        private void Timer_Tick(object sender, object e)
        {
            _tickCount++;
            UpdateActiveWindow();

            // Update clock every second (2 ticks)
            if (_tickCount % 2 == 0)
            {
                ViewModel.ClockText = DateTime.Now.ToString("MM/dd/yyyy  HH:mm");
            }

            // Update Media every 2 seconds (4 ticks)
            if (_tickCount % 4 == 0)
            {
                UpdateMedia();
            }

            // Update Battery/Network every 10 seconds (20 ticks)
            if (_tickCount % 20 == 0)
            {
                UpdateHardware();
            }
        }

        private void UpdateActiveWindow()
        {
            IntPtr fg = NativeMethods.GetForegroundWindow();
            if (fg == _hwnd || fg == IntPtr.Zero) return;

            StringBuilder sb = new StringBuilder(256);
            NativeMethods.GetWindowText(fg, sb, 256);
            string title = sb.ToString();
            
            if (string.IsNullOrWhiteSpace(title)) title = "Desktop";
            if (title.Length > 52) title = title.Substring(0, 52) + "...";

            ViewModel.ActiveWindowTitle = title;
        }

        private async void UpdateMedia()
        {
            if (_mediaManager == null) return;
            var session = _mediaManager.GetCurrentSession();
            if (session == null)
            {
                ViewModel.MediaText = "Nothing playing";
                ViewModel.MediaIndicatorBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
                return;
            }

            try
            {
                var props = await session.TryGetMediaPropertiesAsync();
                var playback = session.GetPlaybackInfo();
                
                string text = "";
                if (!string.IsNullOrEmpty(props.Title)) text += props.Title;
                if (!string.IsNullOrEmpty(props.Artist)) text += (text.Length > 0 ? " — " : "") + props.Artist;
                
                if (string.IsNullOrEmpty(text)) text = "Nothing playing";

                ViewModel.MediaText = text;
                if (playback.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)
                {
                    ViewModel.MediaIndicatorBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(255, 106, 196, 91));
                }
                else
                {
                    ViewModel.MediaIndicatorBrush = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(60, 255, 255, 255));
                }
            }
            catch { }
        }

        private void UpdateHardware()
        {
            var bat = _hwService.GetBatteryInfo();
            if (bat.HasBattery)
            {
                ViewModel.BatteryText = $"{bat.Percent}%";
                ViewModel.BatteryIcon = bat.Charging ? "\uEA93" : "\uE83F";
            }
            else
            {
                ViewModel.BatteryText = "AC";
                ViewModel.BatteryIcon = "";
            }

            var net = _hwService.GetNetworkInfo();
            if (net.Connected)
            {
                ViewModel.NetworkIcon = net.IsWifi ? "\uE701" : "\uE839";
            }
            else
            {
                ViewModel.NetworkIcon = "\uEB55";
            }
        }

        private void Window_Closed(object sender, WindowEventArgs args)
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Tick -= Timer_Tick;
            }

            NativeMethods.APPBARDATA abd = new NativeMethods.APPBARDATA();
            abd.cbSize = Marshal.SizeOf(abd);
            abd.hWnd = _hwnd;
            NativeMethods.SHAppBarMessage(NativeMethods.ABM_REMOVE, ref abd);
        }
    }
}
