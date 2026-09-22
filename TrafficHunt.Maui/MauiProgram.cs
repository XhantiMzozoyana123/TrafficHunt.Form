using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using System.Diagnostics;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;
using TrafficHunt.Infrastructure.Services;
using YoutubeExplode;

namespace TrafficHunt.Maui
{
    public static class MauiProgram
    {
        public static MauiApp CreateMauiApp()
        {
            AppDomain.CurrentDomain.UnhandledException += (_, args) =>
                StartupDiagnostics.Log(
                    args.ExceptionObject as Exception ?? new Exception("Unknown app domain failure"),
                    "AppDomain");

            TaskScheduler.UnobservedTaskException += (_, args) =>
            {
                StartupDiagnostics.Log(args.Exception, "TaskScheduler");
                args.SetObserved();
            };

            var builder = MauiApp.CreateBuilder();
            builder.UseMauiApp<App>();

            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            // Database (MySQL / MariaDB)
            builder.Services.AddMySqlDbContext(builder.Configuration);

            // Shared HTTP client used by the LLM (Ollama) service.
            builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

            // YouTube: search/browse client plus the Google Data API client used for comments.
            builder.Services.AddSingleton<YoutubeClient>();
            builder.Services.AddSingleton<Google.Apis.YouTube.v3.YouTubeService>(provider =>
            {
                var configuration = provider.GetRequiredService<IConfiguration>();

                return new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
                {
                    ApiKey = configuration["YouTube:ApiKey"] ?? string.Empty,
                    ApplicationName = configuration["YouTube:ApplicationName"] ?? "TrafficHunt"
                });
            });

            builder.Services.AddSingleton<IYouTubeService, YouTubeService>();
            builder.Services.AddSingleton<ILLMService, LLMService>();
            builder.Services.AddSingleton<IAIService, AIService>();

            // These two depend on the scoped ApplicationDbContext, so they are scoped as well.
            builder.Services.AddScoped<IMessengerService, MessengerService>();
            builder.Services.AddScoped<IExtractService, ExtractService>();

            // UI
            builder.Services.AddSingleton<AppShell>();
            builder.Services.AddTransient<CommentsPage>();
            builder.Services.AddTransient<MainPage>();
            builder.Services.AddTransient<LeadsPage>();
            builder.Services.AddTransient<SettingsPage>();

            var app = builder.Build();

            ApplyMigrations(app.Services);

            return app;
        }

        /// <summary>
        /// Applies any pending EF Core migrations so the app is usable on first run.
        /// Failures are logged instead of crashing the app; the Settings page can retry
        /// and surface the error to the user.
        /// </summary>
        private static void ApplyMigrations(IServiceProvider services)
        {
            try
            {
                using var scope = services.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                context.Database.Migrate();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[TrafficHunt] Database migration failed: {ex.Message}");
            }
        }
    }
}