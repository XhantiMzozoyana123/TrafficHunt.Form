using Microsoft.Extensions.DependencyInjection;

namespace TrafficHunt.Maui
{
    public partial class App : Microsoft.Maui.Controls.Application
    {
        private readonly IServiceProvider _services;

        public App(IServiceProvider services)
        {
            InitializeComponent();
            _services = services;
        }

        protected override Microsoft.Maui.Controls.Window CreateWindow(IActivationState? activationState)
        {
            // Resolved here (not through constructor injection) so that App.xaml resources,
            // such as the shared styles, are loaded before any page is created.
            var appShell = _services.GetRequiredService<AppShell>();

            var (width, height, x, y) = GetInitialWindowBounds();

            var window = new Microsoft.Maui.Controls.Window(appShell)
            {
                Title = "TrafficHunt",
                Width = width,
                Height = height,
                X = x,
                Y = y,
                MinimumWidth = Math.Min(820, width),
                MinimumHeight = Math.Min(500, height)
            };

            window.Created += (_, _) => MaximizeWindow(window);

            return window;
        }

        /// <summary>
        /// Opens the window maximised on Windows: the comments table needs the width, and a
        /// maximised window always fits the display whatever the user's scaling settings are.
        /// When the window is restored down it falls back to the size set in CreateWindow.
        /// </summary>
        private static void MaximizeWindow(Microsoft.Maui.Controls.Window window)
        {
#if WINDOWS
            try
            {
                if (window.Handler?.PlatformView is Microsoft.UI.Xaml.Window nativeWindow &&
                    nativeWindow.AppWindow?.Presenter is Microsoft.UI.Windowing.OverlappedPresenter presenter)
                {
                    presenter.Maximize();
                }
            }
            catch
            {
                // Maximising is a convenience only; the window already has a sensible size.
            }
#endif
        }

        /// <summary>
        /// Keeps the first window inside the current display and centres it. Without this the
        /// window keeps its preferred size and the operating system cascades it down and to the
        /// right, which pushes the right-hand table columns and the bottom panel off the screen.
        /// </summary>
        private static (double Width, double Height, double X, double Y) GetInitialWindowBounds()
        {
            const double preferredWidth = 1160;
            const double preferredHeight = 760;

            var display = DeviceDisplay.Current.MainDisplayInfo;

            if (display.Width <= 0 || display.Height <= 0 || display.Density <= 0)
            {
                return (preferredWidth, preferredHeight, 0, 0);
            }

            // DisplayInfo reports physical pixels, window sizes are in device independent units.
            var screenWidth = display.Width / display.Density;
            var screenHeight = display.Height / display.Density;

            // Leave room for the window border, title bar and taskbar.
            var width = Math.Max(820, Math.Min(preferredWidth, screenWidth - 40));
            var height = Math.Max(500, Math.Min(preferredHeight, screenHeight - 140));

            var x = Math.Max(0, (screenWidth - width) / 2);
            var y = Math.Max(0, ((screenHeight - height) / 2) - 20);

            return (width, height, x, y);
        }
    }
}