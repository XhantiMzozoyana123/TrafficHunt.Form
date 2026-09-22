namespace TrafficHunt.Maui
{
    public partial class AppShell : Shell
    {
        public AppShell(CommentsPage commentsPage)
        {
            InitializeComponent();

            // Resolved from the DI container so the page can take its services through
            // its constructor. The app currently shows the comment finder only.
            CommentsContent.Content = commentsPage;
        }
    }
}
