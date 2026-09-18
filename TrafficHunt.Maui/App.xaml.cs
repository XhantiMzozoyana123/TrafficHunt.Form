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

            return new Microsoft.Maui.Controls.Window(appShell)
            {
                Title = "TrafficHunt",
                Width = 1280,
                Height = 800,
                MinimumWidth = 900,
                MinimumHeight = 600
            };
        }
    }
}