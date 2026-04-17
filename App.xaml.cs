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

            // Prevent unhandled UI-thread exceptions from silently crashing the process.
            this.UnhandledException += (_, e) => e.Handled = true;

            // Prevent unobserved task exceptions from crashing in edge cases.
            System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (_, e) => e.SetObserved();
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