namespace TrafficHunt.Maui
{
    public partial class AppShell : Shell
    {
        public AppShell(MainPage mainPage, LeadsPage leadsPage, SettingsPage settingsPage)
        {
            InitializeComponent();

            // Pages are resolved from the DI container so they can take their services
            // through their constructors.
            SearchContent.Content = mainPage;
            LeadsContent.Content = leadsPage;
            SettingsContent.Content = settingsPage;
        }
    }
}
