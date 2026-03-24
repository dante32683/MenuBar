using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;

namespace MenuBar.ViewModels
{
    public class MainViewModel : INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;

        private void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private string _activeWindowTitle = "Desktop";
        public string ActiveWindowTitle
        {
            get => _activeWindowTitle;
            set { _activeWindowTitle = value; OnPropertyChanged(); }
        }

        private string _clockText = "";
        public string ClockText
        {
            get => _clockText;
            set { _clockText = value; OnPropertyChanged(); }
        }

        private string _batteryIcon = "\uE83F"; // default battery
        public string BatteryIcon
        {
            get => _batteryIcon;
            set { _batteryIcon = value; OnPropertyChanged(); }
        }

        private string _batteryText = "100%";
        public string BatteryText
        {
            get => _batteryText;
            set { _batteryText = value; OnPropertyChanged(); }
        }

        private string _networkIcon = "\uE701"; // default wifi
        public string NetworkIcon
        {
            get => _networkIcon;
            set { _networkIcon = value; OnPropertyChanged(); }
        }

        private string _mediaText = "Nothing playing";
        public string MediaText
        {
            get => _mediaText;
            set { _mediaText = value; OnPropertyChanged(); }
        }

        private Brush _mediaIndicatorBrush = new SolidColorBrush(Microsoft.UI.Colors.Transparent);
        public Brush MediaIndicatorBrush
        {
            get => _mediaIndicatorBrush;
            set { _mediaIndicatorBrush = value; OnPropertyChanged(); }
        }

        // Visibility toggles based on settings
        public Visibility ShowLogo => Visibility.Visible;
        public Visibility ShowTitle => Visibility.Visible;
        public Visibility ShowMedia => Visibility.Visible;
        public Visibility ShowNetwork => Visibility.Visible;
        public Visibility ShowBattery => Visibility.Visible;
        public Visibility ShowClock => Visibility.Visible;
    }
}