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
            // Simple runtime check for compatibility
            if (!IsWindowsVersionSupported(17763))
            {
                // In a real app, we might show a message box here, 
                // but for a portable utility, we'll just exit gracefully.
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