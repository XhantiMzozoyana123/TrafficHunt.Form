using Google.Apis.Services;
using Microsoft.EntityFrameworkCore;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;
using TrafficHunt.Infrastructure.Services;
using YoutubeExplode;

namespace TrafficHunt.Web
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            builder.Configuration.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true);

            builder.Services.AddControllersWithViews();

            // Database (SQLite) - single file database, no external server needed.
            var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
                ?? "Data Source=traffichunt.db";

            builder.Services.AddDbContext<ApplicationDbContext>(options =>
                options.UseSqlite(connectionString));

            // Shared HTTP client used by the LLM (Ollama) service.
            builder.Services.AddSingleton(new HttpClient { Timeout = TimeSpan.FromMinutes(5) });

            // YouTube: search/browse client plus the Google Data API client used for comments.
            builder.Services.AddSingleton<YoutubeClient>();
            builder.Services.AddSingleton<Google.Apis.YouTube.v3.YouTubeService>(_ =>
                new Google.Apis.YouTube.v3.YouTubeService(new BaseClientService.Initializer
                {
                    ApiKey = builder.Configuration["YouTube:ApiKey"] ?? string.Empty,
                    ApplicationName = builder.Configuration["YouTube:ApplicationName"] ?? "TrafficHunt"
                }));

            builder.Services.AddSingleton<IYouTubeService, YouTubeService>();
            builder.Services.AddSingleton<ILLMService, LLMService>();
            builder.Services.AddSingleton<IAIService, AIService>();
            builder.Services.AddScoped<IMessengerService, MessengerService>();

            var app = builder.Build();

            // Apply any pending EF Core migrations so the app is usable on first run.
            using (var scope = app.Services.CreateScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                db.Database.Migrate();
            }

            app.UseStaticFiles();
            app.UseRouting();

            app.MapControllerRoute(
                name: "default",
                pattern: "{controller=Comments}/{action=Index}/{id?}");

            app.Run();
        }
    }
}
