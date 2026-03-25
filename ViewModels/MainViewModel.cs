using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MenuBar.ViewModels
{
    public class MainViewModel : ObservableObject
    {
        private string _activeWindowTitle = "Desktop";
        public string ActiveWindowTitle
        {
            get => _activeWindowTitle;
            set => SetProperty(ref _activeWindowTitle, value);
        }

        private string _activeWindowTitleTooltip = "Desktop";
        public string ActiveWindowTitleTooltip
        {
            get => _activeWindowTitleTooltip;
            set => SetProperty(ref _activeWindowTitleTooltip, value);
        }

        private ImageSource _activeWindowIcon;
        public ImageSource ActiveWindowIcon
        {
            get => _activeWindowIcon;
            set => SetProperty(ref _activeWindowIcon, value);
        }

        private Visibility _activeWindowIconVisibility = Visibility.Collapsed;
        public Visibility ActiveWindowIconVisibility
        {
            get => _activeWindowIconVisibility;
            set => SetProperty(ref _activeWindowIconVisibility, value);
        }

        private string _clockText = "";
        public string ClockText
        {
            get => _clockText;
            set => SetProperty(ref _clockText, value);
        }

        private string _clockTooltip = "";
        public string ClockTooltip
        {
            get => _clockTooltip;
            set => SetProperty(ref _clockTooltip, value);
        }

        private string _batteryIcon = "\uE83F";
        public string BatteryIcon
        {
            get => _batteryIcon;
            set => SetProperty(ref _batteryIcon, value);
        }

        private string _batteryText = "100%";
        public string BatteryText
        {
            get => _batteryText;
            set => SetProperty(ref _batteryText, value);
        }

        private string _batteryTooltip = "Battery";
        public string BatteryTooltip
        {
            get => _batteryTooltip;
            set => SetProperty(ref _batteryTooltip, value);
        }

        private string _networkIcon = "\uE701";
        public string NetworkIcon
        {
            get => _networkIcon;
            set => SetProperty(ref _networkIcon, value);
        }

        private string _networkTooltip = "Network";
        public string NetworkTooltip
        {
            get => _networkTooltip;
            set => SetProperty(ref _networkTooltip, value);
        }

        private string _mediaText = "Nothing playing";
        public string MediaText
        {
            get => _mediaText;
            set => SetProperty(ref _mediaText, value);
        }

        private string _mediaTooltip = "";
        public string MediaTooltip
        {
            get => _mediaTooltip;
            set => SetProperty(ref _mediaTooltip, value);
        }

        private string _mediaTitle = "";
        public string MediaTitle
        {
            get => _mediaTitle;
            set => SetProperty(ref _mediaTitle, value);
        }

        private string _mediaArtist = "";
        public string MediaArtist
        {
            get => _mediaArtist;
            set => SetProperty(ref _mediaArtist, value);
        }

        private ImageSource _mediaAlbumCover;
        public ImageSource MediaAlbumCover
        {
            get => _mediaAlbumCover;
            set => SetProperty(ref _mediaAlbumCover, value);
        }

        private Microsoft.UI.Xaml.Controls.Symbol _mediaPlayPauseSymbol = Microsoft.UI.Xaml.Controls.Symbol.Play;
        public Microsoft.UI.Xaml.Controls.Symbol MediaPlayPauseSymbol
        {
            get => _mediaPlayPauseSymbol;
            set => SetProperty(ref _mediaPlayPauseSymbol, value);
        }

        private Brush _mediaIndicatorBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        public Brush MediaIndicatorBrush
        {
            get => _mediaIndicatorBrush;
            set => SetProperty(ref _mediaIndicatorBrush, value);
        }

        private string _logoTooltip = "Power and system menu";
        public string LogoTooltip
        {
            get => _logoTooltip;
            set => SetProperty(ref _logoTooltip, value);
        }

        private Visibility _logoVisibility = Visibility.Visible;
        public Visibility LogoVisibility
        {
            get => _logoVisibility;
            set => SetProperty(ref _logoVisibility, value);
        }

        private Visibility _titleVisibility = Visibility.Visible;
        public Visibility TitleVisibility
        {
            get => _titleVisibility;
            set => SetProperty(ref _titleVisibility, value);
        }

        private Visibility _mediaVisibility = Visibility.Collapsed;
        public Visibility MediaVisibility
        {
            get => _mediaVisibility;
            set => SetProperty(ref _mediaVisibility, value);
        }

        private Visibility _networkVisibility = Visibility.Visible;
        public Visibility NetworkVisibility
        {
            get => _networkVisibility;
            set => SetProperty(ref _networkVisibility, value);
        }

        private Visibility _batteryVisibility = Visibility.Visible;
        public Visibility BatteryVisibility
        {
            get => _batteryVisibility;
            set => SetProperty(ref _batteryVisibility, value);
        }

        private Visibility _clockVisibility = Visibility.Visible;
        public Visibility ClockVisibility
        {
            get => _clockVisibility;
            set => SetProperty(ref _clockVisibility, value);
        }

        private double _iconFontSize = 14;
        public double IconFontSize
        {
            get => _iconFontSize;
            set => SetProperty(ref _iconFontSize, value);
        }


        private double _textFontSize = 11;
        public double TextFontSize
        {
            get => _textFontSize;
            set => SetProperty(ref _textFontSize, value);
        }
    }
}
