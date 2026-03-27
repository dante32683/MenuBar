using System;
using Microsoft.UI.Xaml;

namespace MenuBar
{
    public partial class App : Application
    {
        private Window m_window;

        public App()
        {
            this.InitializeComponent();
        }

        protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
        {
            if (!IsWindowsVersionSupported(17763))
            {
                Exit();
                return;
            }

            m_window = new MainWindow();
            m_window.Activate();
        }

        private static bool IsWindowsVersionSupported(int minBuild)
        {
            var version = Environment.OSVersion.Version;
            return version.Major > 10 || (version.Major == 10 && version.Build >= minBuild);
        }
    }
}