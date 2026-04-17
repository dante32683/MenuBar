using System;
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

        private string _mediaSourceApp = "";
        public string MediaSourceApp
        {
            get => _mediaSourceApp;
            set => SetProperty(ref _mediaSourceApp, value);
        }

        private ImageSource _mediaSourceAppIcon;
        public ImageSource MediaSourceAppIcon
        {
            get => _mediaSourceAppIcon;
            set => SetProperty(ref _mediaSourceAppIcon, value);
        }

        private Visibility _mediaSourceAppIconVisibility = Visibility.Collapsed;
        public Visibility MediaSourceAppIconVisibility
        {
            get => _mediaSourceAppIconVisibility;
            set => SetProperty(ref _mediaSourceAppIconVisibility, value);
        }

        private double _mediaShuffleOpacity = 0.5;
        public double MediaShuffleOpacity
        {
            get => _mediaShuffleOpacity;
            set => SetProperty(ref _mediaShuffleOpacity, value);
        }

        private Visibility _mediaShuffleVisibility = Visibility.Visible;
        public Visibility MediaShuffleVisibility
        {
            get => _mediaShuffleVisibility;
            set => SetProperty(ref _mediaShuffleVisibility, value);
        }

        private string _mediaRepeatIcon = "\uE8EE";
        public string MediaRepeatIcon
        {
            get => _mediaRepeatIcon;
            set => SetProperty(ref _mediaRepeatIcon, value);
        }

        private double _mediaRepeatOpacity = 0.5;
        public double MediaRepeatOpacity
        {
            get => _mediaRepeatOpacity;
            set => SetProperty(ref _mediaRepeatOpacity, value);
        }

        private Visibility _mediaRepeatVisibility = Visibility.Visible;
        public Visibility MediaRepeatVisibility
        {
            get => _mediaRepeatVisibility;
            set => SetProperty(ref _mediaRepeatVisibility, value);
        }

        private bool _isMediaInlineLayout;
        public bool IsMediaInlineLayout
        {
            get => _isMediaInlineLayout;
            set
            {
                if (SetProperty(ref _isMediaInlineLayout, value))
                {
                    OnPropertyChanged(nameof(MediaStandardTransportVisibility));
                    OnPropertyChanged(nameof(MediaInlineTransportVisibility));
                    OnPropertyChanged(nameof(MediaInlineSourceVisibility));
                    OnPropertyChanged(nameof(MediaStandardSourceVisibility));
                    OnPropertyChanged(nameof(MediaAlbumArtSize));
                }
            }
        }

        public Visibility MediaStandardTransportVisibility =>
            IsMediaInlineLayout ? Visibility.Collapsed : Visibility.Visible;

        public Visibility MediaInlineTransportVisibility =>
            IsMediaInlineLayout ? Visibility.Visible : Visibility.Collapsed;

        public Visibility MediaInlineSourceVisibility =>
            IsMediaInlineLayout ? Visibility.Visible : Visibility.Collapsed;

        public Visibility MediaStandardSourceVisibility =>
            IsMediaInlineLayout ? Visibility.Collapsed : Visibility.Visible;

        public double MediaAlbumArtSize => IsMediaInlineLayout ? 68 : 56;

        private double _mediaPositionSeconds = 0;
        public double MediaPositionSeconds
        {
            get => _mediaPositionSeconds;
            set => SetProperty(ref _mediaPositionSeconds, value);
        }

        private double _mediaDurationSeconds = 1;
        public double MediaDurationSeconds
        {
            get => _mediaDurationSeconds;
            set => SetProperty(ref _mediaDurationSeconds, value);
        }

        private string _mediaPositionText = "0:00";
        public string MediaPositionText
        {
            get => _mediaPositionText;
            set => SetProperty(ref _mediaPositionText, value);
        }

        private string _mediaDurationText = "0:00";
        public string MediaDurationText
        {
            get => _mediaDurationText;
            set => SetProperty(ref _mediaDurationText, value);
        }

        private Visibility _mediaFlyoutProgressVisibility = Visibility.Visible;
        public Visibility MediaFlyoutProgressVisibility
        {
            get => _mediaFlyoutProgressVisibility;
            set => SetProperty(ref _mediaFlyoutProgressVisibility, value);
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

        private Visibility _phoneVisibility = Visibility.Collapsed;
        public Visibility PhoneVisibility
        {
            get => _phoneVisibility;
            set => SetProperty(ref _phoneVisibility, value);
        }

        private double _iconFontSize = 14;
        public double IconFontSize
        {
            get => _iconFontSize;
            set
            {
                if (SetProperty(ref _iconFontSize, value))
                {
                    OnPropertyChanged(nameof(FlyoutIconFontSize));
                    OnPropertyChanged(nameof(BatteryTextMargin));
                }
            }
        }

        // Negative top margin shifts the battery % text upward to compensate for Segoe Fluent Icons'
        // glyph sitting in the upper portion of its em square vs Segoe UI Variable's baseline.
        // Must be negative top (not positive bottom): Segoe Fluent Icons and Segoe UI Variable have
        // nearly identical layout heights at the TextFontSize/IconFontSize ratio used here, so a
        // positive bottom margin would inflate the text's measured height past the icon's, making it
        // the StackPanel height reference and pushing the text to the top of its slot.
        // Negative top margin shifts without inflating measured height; factor 0.115 scales with
        // bar_height via IconFontSize (all in DIPs, so DPI is already handled by the framework).
        public Thickness BatteryTextMargin =>
            new Thickness(0, -Math.Round(_iconFontSize * 0.115), 0, 0);

        private double _textFontSize = 11;
        public double TextFontSize
        {
            get => _textFontSize;
            set
            {
                if (SetProperty(ref _textFontSize, value))
                {
                    OnPropertyChanged(nameof(FlyoutTitleFontSize));
                    OnPropertyChanged(nameof(FlyoutBodyFontSize));
                    OnPropertyChanged(nameof(FlyoutCaptionFontSize));
                }
            }
        }

        // Responsive Flyout Font Sizes
        // Derived from TextFontSize (base 11px -> 14px body, 20px title, 12px caption)
        public double FlyoutTitleFontSize => Math.Round(TextFontSize * 1.82);   // 11 * 1.82 ≈ 20
        public double FlyoutBodyFontSize => Math.Round(TextFontSize * 1.27);    // 11 * 1.27 ≈ 14
        public double FlyoutCaptionFontSize => Math.Round(TextFontSize * 1.09); // 11 * 1.09 ≈ 12
        public double FlyoutIconFontSize => Math.Round(IconFontSize * 1.71);    // 14 * 1.71 ≈ 24

        private CornerRadius _hostCornerRadius = new CornerRadius(6);
        public CornerRadius HostCornerRadius
        {
            get => _hostCornerRadius;
            set => SetProperty(ref _hostCornerRadius, value);
        }

        private Thickness _hostPadding = new Thickness(8, 0, 8, 0);
        public Thickness HostPadding
        {
            get => _hostPadding;
            set => SetProperty(ref _hostPadding, value);
        }

        private double _batteryIconWidth = 22;
        public double BatteryIconWidth
        {
            get => _batteryIconWidth;
            set => SetProperty(ref _batteryIconWidth, value);
        }

        // Battery Flyout Properties
        private string _batteryFlyoutPercent = "100%";
        public string BatteryFlyoutPercent
        {
            get => _batteryFlyoutPercent;
            set => SetProperty(ref _batteryFlyoutPercent, value);
        }

        private string _batteryFlyoutStatus = "On battery";
        public string BatteryFlyoutStatus
        {
            get => _batteryFlyoutStatus;
            set => SetProperty(ref _batteryFlyoutStatus, value);
        }

        private string _batteryFlyoutTime = "";
        public string BatteryFlyoutTime
        {
            get => _batteryFlyoutTime;
            set => SetProperty(ref _batteryFlyoutTime, value);
        }

        private string _batteryFlyoutWattage = "";
        public string BatteryFlyoutWattage
        {
            get => _batteryFlyoutWattage;
            set => SetProperty(ref _batteryFlyoutWattage, value);
        }

        private string _batteryFlyoutWattageIcon = "";
        public string BatteryFlyoutWattageIcon
        {
            get => _batteryFlyoutWattageIcon;
            set => SetProperty(ref _batteryFlyoutWattageIcon, value);
        }

        private Brush _batteryFlyoutWattageBrush;
        public Brush BatteryFlyoutWattageBrush
        {
            get => _batteryFlyoutWattageBrush;
            set => SetProperty(ref _batteryFlyoutWattageBrush, value);
        }

        private double _batteryFlyoutProgress;
        public double BatteryFlyoutProgress
        {
            get => _batteryFlyoutProgress;
            set => SetProperty(ref _batteryFlyoutProgress, value);
        }

        private Visibility _batteryFlyoutProgressVisibility = Visibility.Visible;
        public Visibility BatteryFlyoutProgressVisibility
        {
            get => _batteryFlyoutProgressVisibility;
            set => SetProperty(ref _batteryFlyoutProgressVisibility, value);
        }

        private Visibility _batteryFlyoutWattageVisibility = Visibility.Collapsed;
        public Visibility BatteryFlyoutWattageVisibility
        {
            get => _batteryFlyoutWattageVisibility;
            set => SetProperty(ref _batteryFlyoutWattageVisibility, value);
        }


        private string _batteryFlyoutProjected = "";
        public string BatteryFlyoutProjected
        {
            get => _batteryFlyoutProjected;
            set => SetProperty(ref _batteryFlyoutProjected, value);
        }

        private Visibility _batteryFlyoutProjectedVisibility = Visibility.Collapsed;
        public Visibility BatteryFlyoutProjectedVisibility
        {
            get => _batteryFlyoutProjectedVisibility;
            set => SetProperty(ref _batteryFlyoutProjectedVisibility, value);
        }

        private Visibility _batteryFlyoutTimeVisibility = Visibility.Collapsed;
        public Visibility BatteryFlyoutTimeVisibility
        {
            get => _batteryFlyoutTimeVisibility;
            set => SetProperty(ref _batteryFlyoutTimeVisibility, value);
        }

        private string _virtualDesktopText = "Desktop 1";
        public string VirtualDesktopText
        {
            get => _virtualDesktopText;
            set => SetProperty(ref _virtualDesktopText, value);
        }

        private Visibility _virtualDesktopVisibility = Visibility.Collapsed;
        public Visibility VirtualDesktopVisibility
        {
            get => _virtualDesktopVisibility;
            set => SetProperty(ref _virtualDesktopVisibility, value);
        }

        private string _batteryFlyoutUsageTime = "";
        public string BatteryFlyoutUsageTime
        {
            get => _batteryFlyoutUsageTime;
            set => SetProperty(ref _batteryFlyoutUsageTime, value);
        }

        private Visibility _batteryFlyoutUsageTimeVisibility = Visibility.Collapsed;
        public Visibility BatteryFlyoutUsageTimeVisibility
        {
            get => _batteryFlyoutUsageTimeVisibility;
            set => SetProperty(ref _batteryFlyoutUsageTimeVisibility, value);
        }

        private Visibility _batteryFlyoutDetailsVisibility = Visibility.Collapsed;
        public Visibility BatteryFlyoutDetailsVisibility
        {
            get => _batteryFlyoutDetailsVisibility;
            set => SetProperty(ref _batteryFlyoutDetailsVisibility, value);
        }
    }
}
