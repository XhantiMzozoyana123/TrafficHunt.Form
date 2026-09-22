using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;

namespace TrafficHunt.Maui
{
    public partial class MainPage : ContentPage
    {
        private readonly IYouTubeService _youTubeService;
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IConfiguration _configuration;
        private readonly ObservableCollection<YouTubeVideoDto> _videos = new();

        public MainPage(
            IYouTubeService youTubeService,
            IServiceScopeFactory scopeFactory,
            IConfiguration configuration)
        {
            InitializeComponent();

            _youTubeService = youTubeService;
            _scopeFactory = scopeFactory;
            _configuration = configuration;

            VideoList.ItemsSource = _videos;

            PromptEditor.Text = _configuration["Hunt:DefaultPrompt"] ?? string.Empty;
            MaxResultsEntry.Text = _configuration["Hunt:DefaultMaxResults"] ?? "10";
        }

        private async void OnSearchClicked(object? sender, EventArgs e)
        {
            if (!TryCreateSearch(out var searchDto))
            {
                return;
            }

            await RunBusyAsync("Searching YouTube...", async () =>
            {
                var videos = await _youTubeService.SearchVideosAsync(searchDto);

                _videos.Clear();
                foreach (var video in videos)
                {
                    _videos.Add(video);
                }

                SetStatus($"Found {_videos.Count} video(s) for \"{searchDto.Keyword}\".");
                VideoCountLabel.Text = $"{_videos.Count} video(s)";
            });
        }

        private async void OnHuntClicked(object? sender, EventArgs e)
        {
            if (!TryCreateSearch(out var searchDto))
            {
                return;
            }

            if (string.IsNullOrWhiteSpace(_configuration["YouTube:ApiKey"]))
            {
                SetStatus("YouTube:ApiKey is not configured. Add it on the Settings tab before running a hunt.");
                return;
            }

            await RunBusyAsync("Hunting leads... this can take a while.", async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var extractService = scope.ServiceProvider.GetRequiredService<IExtractService>();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var leadsBefore = await context.Leads.CountAsync();
                await extractService.ExtractVideoAsync(searchDto);
                var leadsAfter = await context.Leads.CountAsync();

                SetStatus($"Hunt complete. New leads: {leadsAfter - leadsBefore}. Total leads: {leadsAfter}.");
            });
        }

        private void OnClearClicked(object? sender, EventArgs e)
        {
            _videos.Clear();
            VideoCountLabel.Text = "none yet";
            SetStatus(string.Empty);
        }

        private bool TryCreateSearch(out SearchDto searchDto)
        {
            searchDto = new SearchDto();

            var keyword = KeywordEntry.Text?.Trim();
            if (string.IsNullOrWhiteSpace(keyword))
            {
                SetStatus("Enter a keyword first.");
                return false;
            }

            searchDto.Keyword = keyword;
            searchDto.Prompt = PromptEditor.Text?.Trim() ?? string.Empty;

            if (int.TryParse(MaxResultsEntry.Text, out var maxResults) && maxResults > 0)
            {
                searchDto.MaxResult = maxResults;
            }

            return true;
        }

        private async Task RunBusyAsync(string status, Func<Task> action)
        {
            SetBusy(true);
            SetStatus(status);

            try
            {
                await action();
            }
            catch (Exception ex)
            {
                SetStatus($"Error: {ex.Message}");
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
            SearchButton.IsEnabled = !isBusy;
            HuntButton.IsEnabled = !isBusy;
            ClearButton.IsEnabled = !isBusy;
        }

        private void SetStatus(string message) => StatusLabel.Text = message;
    }
}
