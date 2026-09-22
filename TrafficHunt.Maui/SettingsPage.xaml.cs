using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;

namespace TrafficHunt.Maui
{
    public partial class SettingsPage : ContentPage
    {
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly string _settingsPath = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

        public SettingsPage(IConfiguration configuration, IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();

            _configuration = configuration;
            _scopeFactory = scopeFactory;

            ConfigPathLabel.Text = $"Configuration file: {_settingsPath}";
            LoadValues();
        }

        private void LoadValues()
        {
            ConnectionStringEntry.Text = _configuration.GetConnectionString("DefaultConnection") ?? string.Empty;
            ApiKeyEntry.Text = _configuration["YouTube:ApiKey"] ?? string.Empty;
            AppNameEntry.Text = _configuration["YouTube:ApplicationName"] ?? "TrafficHunt";
            LlmUrlEntry.Text = _configuration["LLM:Url"] ?? "http://46.202.170.203:11434/api/generate";
            LlmModelEntry.Text = _configuration["LLM:Model"] ?? string.Empty;
            DefaultPromptEditor.Text = _configuration["Hunt:DefaultPrompt"] ?? string.Empty;
            DefaultReplyPromptEditor.Text = _configuration["Hunt:DefaultReplyPrompt"] ?? string.Empty;
            DefaultMaxEntry.Text = _configuration["Hunt:DefaultMaxResults"] ?? "10";
        }

        private async void OnSaveClicked(object? sender, EventArgs e)
        {
            await RunBusyAsync(async () =>
            {
                var root = await ReadSettingsAsync();

                SetValue(root, "ConnectionStrings", "DefaultConnection", ConnectionStringEntry.Text?.Trim());
                SetValue(root, "YouTube", "ApiKey", ApiKeyEntry.Text?.Trim());
                SetValue(root, "YouTube", "ApplicationName", AppNameEntry.Text?.Trim());
                SetValue(root, "LLM", "Url", LlmUrlEntry.Text?.Trim());
                SetValue(root, "LLM", "Model", LlmModelEntry.Text?.Trim());
                SetValue(root, "Hunt", "DefaultPrompt", DefaultPromptEditor.Text?.Trim());
                SetValue(root, "Hunt", "DefaultReplyPrompt", DefaultReplyPromptEditor.Text?.Trim());
                SetValue(root, "Hunt", "DefaultMaxResults", DefaultMaxEntry.Text?.Trim());

                var options = new JsonSerializerOptions { WriteIndented = true };
                await File.WriteAllTextAsync(_settingsPath, root.ToJsonString(options));

                StatusLabel.Text = "Settings saved. Restart the app to apply connection and API key changes.";
            });
        }

        private async Task<JsonObject> ReadSettingsAsync()
        {
            if (!File.Exists(_settingsPath))
            {
                return new JsonObject();
            }

            var json = await File.ReadAllTextAsync(_settingsPath);
            return JsonNode.Parse(json) as JsonObject ?? new JsonObject();
        }

        private static void SetValue(JsonObject root, string section, string key, string? value)
        {
            if (root[section] is not JsonObject sectionObject)
            {
                sectionObject = new JsonObject();
                root[section] = sectionObject;
            }

            sectionObject[key] = value ?? string.Empty;
        }

        private async void OnTestClicked(object? sender, EventArgs e)
        {
            await RunBusyAsync(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var canConnect = await context.Database.CanConnectAsync();
                StatusLabel.Text = canConnect
                    ? "Database connection succeeded."
                    : "Could not connect to the database. Check the connection string.";
            });
        }

        /// <summary>
        /// Sends a small prompt to the configured Ollama endpoint
        /// (http://46.202.170.203:11434/api/generate) so the LLM URL/model can be verified
        /// from the app, the same way the endpoint would be checked in Postman.
        /// </summary>
        private async void OnTestLlmClicked(object? sender, EventArgs e)
        {
            await RunBusyAsync(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var llmService = scope.ServiceProvider.GetRequiredService<ILLMService>();

                var reply = (await llmService.GenerateTextAsync("Reply with only the word: ok")).Trim();

                if (reply.Length > 300)
                {
                    reply = $"{reply[..300]}...";
                }

                StatusLabel.Text = string.IsNullOrWhiteSpace(reply)
                    ? "The LLM responded with an empty reply. Check the model name on the server."
                    : $"LLM replied: {reply}";
            });
        }

        /// <summary>
        /// Calls the YouTube Data API with the configured API key so an invalid, disabled or
        /// restricted key is reported here rather than in the middle of a hunt.
        /// </summary>
        private async void OnTestYouTubeClicked(object? sender, EventArgs e)
        {
            await RunBusyAsync(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var youTubeService = scope.ServiceProvider.GetRequiredService<IYouTubeService>();

                await youTubeService.ValidateApiKeyAsync();

                StatusLabel.Text = "YouTube Data API key is valid.";
            });
        }

        private async void OnMigrateClicked(object? sender, EventArgs e)
        {
            await RunBusyAsync(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var pending = (await context.Database.GetPendingMigrationsAsync()).ToList();
                await context.Database.MigrateAsync();

                StatusLabel.Text = pending.Count == 0
                    ? "Database is already up to date."
                    : $"Applied {pending.Count} migration(s): {string.Join(", ", pending)}";
            });
        }

        private async Task RunBusyAsync(Func<Task> action)
        {
            SetBusy(true);

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Error: {ex.Message}";
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool isBusy)
        {
            BusyIndicator.IsRunning = isBusy;
            BusyIndicator.IsVisible = isBusy;
            SaveButton.IsEnabled = !isBusy;
            TestButton.IsEnabled = !isBusy;
            TestLlmButton.IsEnabled = !isBusy;
            TestYouTubeButton.IsEnabled = !isBusy;
            MigrateButton.IsEnabled = !isBusy;
        }
    }
}