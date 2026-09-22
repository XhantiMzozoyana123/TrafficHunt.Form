using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;
using TrafficHunt.Domain.Entities;

namespace TrafficHunt.Maui
{
    /// <summary>
    /// Step 1 fetches every comment into the traffichuntdb Comments table. Step 2
    /// (the "AI qualify" button) loads those persisted rows, sends them to Ollama as
    /// one JSON payload and keeps only what the AI kept.
    /// </summary>
    public partial class CommentsPage : ContentPage
    {
        /// <summary>Every persisted comment for the current run; the table shows a filtered view of this list.</summary>
        private readonly List<CommentRow> _allRows = new();

        /// <summary>The rows currently shown in the table.</summary>
        private readonly ObservableCollection<CommentRow> _rows = new();

        private readonly IYouTubeService _youTubeService;
        private readonly IAIService _aiService;
        private readonly IConfiguration _configuration;
        private readonly IServiceScopeFactory _scopeFactory;

        private CommentRow? _selectedRow;
        private bool _stopRequested;
        private bool _isBusy;

        private CancellationTokenSource? _aiRun;

        /// <summary>Cancel source for the bulk AI auto-reply run (Step 3).</summary>
        private CancellationTokenSource? _aiReplyRun;

        public CommentsPage(
            IYouTubeService youTubeService,
            IAIService aiService,
            IConfiguration configuration,
            IServiceScopeFactory scopeFactory)
        {
            InitializeComponent();

            _youTubeService = youTubeService;
            _aiService = aiService;
            _configuration = configuration;
            _scopeFactory = scopeFactory;

            CommentList.ItemsSource = _rows;
            OrderPicker.SelectedIndex = 0;
            SortPicker.SelectedIndex = 0;

            AiPromptEditor.Text = _configuration["Hunt:DefaultPrompt"] ?? string.Empty;
            AiReplyPromptEditor.Text = _configuration["Hunt:DefaultReplyPrompt"] ?? string.Empty;
            UpdateAiUi();
        }

        protected override async void OnAppearing()
        {
            base.OnAppearing();

            // The comments live in traffichuntdb, so a restart shows the same table again.
            await LoadPersistedCommentsAsync();
        }

        /// <summary>Loads every non-removed comment from traffichuntdb into the table.</summary>
        private async Task LoadPersistedCommentsAsync()
        {
            List<Comment> persisted;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                persisted = await db.Comments
                    .AsNoTracking()
                    .Where(comment => !comment.RemovedByAi)
                    .OrderByDescending(comment => comment.CreatedAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                SetSummary($"Could not load saved comments from traffichuntdb: {ex.Message}");
                return;
            }

            _allRows.Clear();

            foreach (var comment in persisted)
            {
                _allRows.Add(ToRow(comment));
            }

            ClearSelection();
            ApplyFilter();
            RenumberRows();
            UpdateAiUi();

            if (persisted.Count > 0)
            {
                SetSummary($"{persisted.Count} saved comment(s) loaded from traffichuntdb.");
            }
        }

        private static CommentRow ToRow(Comment comment)
        {
            return new CommentRow
            {
                CommentId = comment.CommentId,
                AuthorName = comment.AuthorName,
                CommentText = comment.CommentText,
                VideoId = comment.VideoId,
                VideoTitle = comment.VideoTitle,
                PublishedAt = comment.PublishedAt,
                LikeCount = comment.LikeCount,
                ReplyText = comment.ReplyText,
                RepliedAt = comment.RepliedAt
            };
        }

        private async void OnFetchClicked(object? sender, EventArgs e)
        {
            if (_aiReplyRun is not null)
            {
                SetSummary("The AI auto-reply is running - stop it before fetching again.");
                return;
            }

            var keyword = KeywordEntry.Text?.Trim();

            if (string.IsNullOrWhiteSpace(keyword))
            {
                SetSummary("Type a keyword first, for example \"traffic leads for contractors\".");
                return;
            }

            if (string.IsNullOrWhiteSpace(_configuration["YouTube:ApiKey"]))
            {
                SetSummary("No YouTube API key configured. Add YouTube:ApiKey to appsettings.json next to the app, then fetch again.");
                return;
            }

            var prompt = AiPromptEditor.Text?.Trim() ?? string.Empty;

            // Remember a non-empty prompt so it is still there the next time the app starts.
            if (!string.IsNullOrWhiteSpace(prompt))
            {
                await SavePromptAsync(prompt);
            }

            _stopRequested = false;
            _allRows.Clear();
            _rows.Clear();
            ClearSelection();

            await RunBusyAsync(async () =>
            {
                var maxVideos = ParsePositive(MaxVideosEntry.Text, 3);
                var maxComments = ParsePositive(MaxCommentsEntry.Text, 20);
                var order = OrderPicker.SelectedIndex == 1 ? "time" : "relevance";

                SetSummary($"Searching YouTube for \"{keyword}\"...");

                var videos = await _youTubeService.SearchVideosAsync(new SearchDto
                {
                    Keyword = keyword,
                    MaxResult = maxVideos
                });

                if (videos.Count == 0)
                {
                    SetSummary($"No videos found for \"{keyword}\". Try a different keyword.");
                    return;
                }

                var commentSearch = new SearchDto
                {
                    Keyword = keyword,
                    MaxResult = maxComments,
                    Prompt = prompt
                };

                var fetched = 0;
                var skipped = 0;

                for (var videoIndex = 0; videoIndex < videos.Count; videoIndex++)
                {
                    if (_stopRequested)
                    {
                        break;
                    }

                    var video = videos[videoIndex];
                    SetSummary($"Video {videoIndex + 1}/{videos.Count}: reading comments from {Shorten(video.Title, 60)}...");

                    List<YouTubeCommentDto> comments;
                    try
                    {
                        comments = await _youTubeService.GetCommentsAsync(commentSearch, video.VideoId, order);
                    }
                    catch (Exception ex)
                    {
                        // Comments are frequently disabled or closed; carry on with the other videos.
                        skipped++;
                        SetSummary($"Skipped \"{Shorten(video.Title, 40)}\": {ex.Message}");
                        continue;
                    }

                    foreach (var comment in comments)
                    {
                        if (_stopRequested)
                        {
                            break;
                        }

                        fetched++;
                        AddRow(comment, video);
                    }

                    // Keep traffichuntdb in step with the table, one video at a time.
                    await PersistCommentsAsync(comments, video.VideoId, video.Title, keyword);

                    // Refetching must not wipe replies that were already posted, so pull the
                    // stored reply state back onto the freshly created rows.
                    await ApplyStoredRepliesAsync(comments);

                    // Redraw once per video so the table fills up while the fetch is running.
                    ApplyFilter();
                    UpdateAiUi();
                }

                ApplyFilter();
                UpdateAiUi();
                SetSummary(BuildFetchSummary(videos.Count - skipped, fetched));
            });
        }

        /// <summary>
        /// Step 2: sends the fetched comments to Ollama as one JSON list and replaces the
        /// table with only the comments the AI kept. The removed comments are gone from the
        /// table until the next fetch.
        /// </summary>
        private async void OnAiQualifyClicked(object? sender, EventArgs e)
        {
            // Button doubles as cancel while the AI run is in flight.
            if (_aiRun is not null)
            {
                _aiRun.Cancel();
                SetSummary("Stopping the AI as soon as the current request finishes...");
                return;
            }

            if (_allRows.Count == 0)
            {
                SetSummary("Fetch comments first, then the AI can qualify them.");
                return;
            }

            var prompt = AiPromptEditor.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                SetSummary("Write the AI prompt first - it decides which comments are kept.");
                return;
            }

            await SavePromptAsync(prompt);

            _aiRun = new CancellationTokenSource();
            SetAiRunning(true);
            AiProgressDetailLabel.Text = "Sending list to AI...";

            try
            {
                var payload = _allRows
                    .Select(row => (row.CommentId, row.AuthorName, row.CommentText))
                    .ToList();

                SetSummary($"Sending {payload.Count} comment(s) to the AI as one list...");

                // IProgress marshals back onto the UI thread, so the bar + text stay live.
                // Both SummaryLabel (top) and AiProgressDetailLabel (inside panel) show
                // the same message so progress is visible wherever the user looks.
                var progress = new Progress<AiFilterProgress>(snapshot =>
                {
                    SetSummary(snapshot.Message);
                    AiProgressDetailLabel.Text = snapshot.Message;

                    var fraction = snapshot.Total <= 0
                        ? 0
                        : Math.Clamp((double)snapshot.Processed / snapshot.Total, 0, 1);

                    AiProgressPanel.IsVisible = true;
                    AiProgressBar.Progress = fraction;
                    AiProgressPercentLabel.Text = $"{(int)Math.Round(fraction * 100)}% ({snapshot.Processed}/{snapshot.Total})";
                });

                var keptIds = await _aiService.FilterCommentIdsAsync(
                    payload,
                    prompt,
                    progress,
                    _aiRun.Token,
                    SaveBatchVerdictsAsync);

                var kept = new HashSet<string>(keptIds);
                var before = _allRows.Count;

                AiProgressBar.Progress = 1;
                AiProgressPercentLabel.Text = $"100% ({before}/{before})";
                AiProgressDetailLabel.Text = $"AI finished - kept {keptIds.Count} of {before}.";

                var removedIds = _allRows
                    .Where(row => !kept.Contains(row.CommentId))
                    .Select(row => row.CommentId)
                    .ToList();

                await MarkRemovedByAiAsync(removedIds);
                await MarkKeptByAiAsync(keptIds);

                _allRows.RemoveAll(row => !kept.Contains(row.CommentId));
                ClearSelection();
                ApplyFilter();
                RenumberRows();

                SetSummary(
                    $"AI kept {keptIds.Count} of {before} comment(s). " +
                    $"{before - _allRows.Count} removed by the prompt. " +
                    "Fetch again to get the full list back.");
            }
            catch (OperationCanceledException)
            {
                SetSummary("AI qualify stopped. The table still shows every fetched comment.");
            }
            catch (Exception ex)
            {
                SetSummary($"AI qualify failed part-way, but every batch already checked was saved: {ex.Message}");
            }
            finally
            {
                var sawCompletion = AiProgressBar.Progress >= 1;
                if (sawCompletion)
                {
                    // Panel is still visible here - let the user see 100% before it hides.
                    await Task.Delay(900);
                }
                _aiRun.Dispose();
                _aiRun = null;
                SetAiRunning(false);
            }
        }

        /// <summary>Renumber the # column after rows were removed.</summary>
        private void RenumberRows()
        {
            var number = 1;

            foreach (var row in _allRows)
            {
                row.Number = number++;
            }

            ApplyFilter();
        }

        private void AddRow(YouTubeCommentDto comment, YouTubeVideoDto video)
        {
            _allRows.Add(new CommentRow
            {
                Number = _allRows.Count + 1,
                CommentId = comment.YouTubeCommentId,
                AuthorName = string.IsNullOrWhiteSpace(comment.AuthorDisplayName)
                    ? "(unknown author)"
                    : comment.AuthorDisplayName,
                CommentText = comment.Text,
                VideoId = video.VideoId,
                VideoTitle = video.Title,
                PublishedAt = comment.PublishedAt,
                LikeCount = comment.LikeCount
            });
        }

        /// <summary>
        /// Inserts new comments or refreshes rows that were already stored (same YouTube
        /// comment id), so the table and the database stay in step.
        /// </summary>
        private async Task PersistCommentsAsync(IEnumerable<YouTubeCommentDto> comments, string videoId, string videoTitle, string keyword)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ids = comments.Select(comment => comment.YouTubeCommentId).ToList();
            var existing = await db.Comments
                .Where(comment => ids.Contains(comment.CommentId))
                .ToDictionaryAsync(comment => comment.CommentId);
            var now = DateTime.UtcNow;

            foreach (var comment in comments)
            {
                if (existing.TryGetValue(comment.YouTubeCommentId, out var stored))
                {
                    stored.VideoId = videoId;
                    stored.VideoTitle = videoTitle;
                    stored.AuthorName = string.IsNullOrWhiteSpace(comment.AuthorDisplayName)
                        ? "(unknown author)"
                        : comment.AuthorDisplayName;
                    stored.CommentText = comment.Text;
                    stored.Keyword = keyword;
                    stored.PublishedAt = comment.PublishedAt;
                    stored.LikeCount = comment.LikeCount;
                    stored.RemovedByAi = false;
                    stored.UpdatedAt = now;
                }
                else
                {
                    db.Comments.Add(new Comment
                    {
                        CommentId = comment.YouTubeCommentId,
                        VideoId = videoId,
                        VideoTitle = videoTitle,
                        AuthorName = string.IsNullOrWhiteSpace(comment.AuthorDisplayName)
                            ? "(unknown author)"
                            : comment.AuthorDisplayName,
                        CommentText = comment.Text,
                        Keyword = keyword,
                        PublishedAt = comment.PublishedAt,
                        LikeCount = comment.LikeCount,
                        CreatedAt = now,
                        UpdatedAt = now
                    });
                }
            }

            await db.SaveChangesAsync();
        }

        /// <summary>
        /// Saves one AI batch's verdicts the moment it finishes, so a crash later in the
        /// run never loses the work already done. Kept ids get KeptByAi, everything else
        /// in the batch gets RemovedByAi - exactly what the end-of-run pass would write.
        /// </summary>
        private async Task SaveBatchVerdictsAsync(
            IReadOnlyList<(string Id, string Author, string Text)> batch,
            IReadOnlyList<string> batchKeptIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

            var ids = batch.Select(item => item.Id).ToList();
            var now = DateTime.UtcNow;
            var kept = new HashSet<string>(batchKeptIds);

            var rows = await db.Comments
                .Where(comment => ids.Contains(comment.CommentId))
                .ToListAsync();

            foreach (var row in rows)
            {
                row.KeptByAi = kept.Contains(row.CommentId);
                row.RemovedByAi = !kept.Contains(row.CommentId);
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync();
        }

        /// <summary>Marks the AI-rejected comments as removed so they stay gone after a restart.</summary>
        private async Task MarkRemovedByAiAsync(IEnumerable<string> commentIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ids = commentIds.ToList();

            if (ids.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var rows = await db.Comments
                .Where(comment => ids.Contains(comment.CommentId))
                .ToListAsync();

            foreach (var row in rows)
            {
                row.RemovedByAi = true;
                row.KeptByAi = false;
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync();
        }

        /// <summary>Marks the AI-kept comments so a later run can see what survived.</summary>
        private async Task MarkKeptByAiAsync(IEnumerable<string> commentIds)
        {
            using var scope = _scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var ids = commentIds.ToList();

            if (ids.Count == 0)
            {
                return;
            }

            var now = DateTime.UtcNow;
            var rows = await db.Comments
                .Where(comment => ids.Contains(comment.CommentId))
                .ToListAsync();

            foreach (var row in rows)
            {
                row.KeptByAi = true;
                row.RemovedByAi = false;
                row.UpdatedAt = now;
            }

            await db.SaveChangesAsync();
        }

        private void OnStopClicked(object? sender, EventArgs e)
        {
            _stopRequested = true;
            SetSummary("Stopping as soon as the current request finishes...");
        }

        /// <summary>Restricts the CSV pickers to .csv files (Windows plus a fallback for others).</summary>
        private static readonly FilePickerFileType CsvFileType = new(
            new Dictionary<DevicePlatform, IEnumerable<string>>
            {
                [DevicePlatform.WinUI] = ["csv"],
                [DevicePlatform.Android] = ["text/csv", "text/comma-separated-values", "text/plain"],
                [DevicePlatform.iOS] = ["public.comma-separated-values-text"],
                [DevicePlatform.MacCatalyst] = ["public.comma-separated-values-text"]
            });

        private async void OnCsvClicked(object? sender, EventArgs e)
        {
            if (_isBusy || _aiRun is not null)
            {
                SetSummary("Wait for the current fetch / AI run to finish (or stop it) before using CSV.");
                return;
            }

            const string export = "Export comments to CSV";
            const string import = "Import comments from CSV";

            var choice = await DisplayActionSheetAsync(
                "CSV",
                "Cancel",
                null,
                export,
                import);

            if (choice == export)
            {
                await RunBusyAsync(ExportCommentsAsync);
            }
            else if (choice == import)
            {
                await RunBusyAsync(ImportCommentsAsync);
            }
        }

        /// <summary>
        /// Writes every non-removed saved comment to a CSV file chosen by the user.
        /// The file can be edited in Excel and brought back with "Import comments from CSV".
        /// </summary>
        private async Task ExportCommentsAsync()
        {
            List<Comment> persisted;

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                persisted = await db.Comments
                    .AsNoTracking()
                    .Where(comment => !comment.RemovedByAi)
                    .OrderByDescending(comment => comment.CreatedAt)
                    .ToListAsync();
            }
            catch (Exception ex)
            {
                SetSummary($"Could not read saved comments from traffichuntdb: {ex.Message}");
                return;
            }

            if (persisted.Count == 0)
            {
                SetSummary("There are no saved comments to export. Fetch or import comments first.");
                return;
            }

            var (path, pickerError) = await PickExportPathAsync();

            if (path is null)
            {
                if (pickerError is null)
                {
                    SetSummary("Export cancelled.");
                    return;
                }

                // The save dialog could not open - show why and offer the Documents
                // folder with a timestamped name instead so the export still works.
                var fallback = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
                    $"comments-{DateTime.Now:yyyy-MM-dd-HHmm}.csv");

                var useFallback = await DisplayAlertAsync(
                    "Save dialog failed",
                    $"The save dialog could not open: {pickerError}\n\nSave the CSV to:\n{fallback}",
                    "Save there",
                    "Cancel");

                if (!useFallback)
                {
                    SetSummary("Export cancelled.");
                    return;
                }

                path = fallback;
            }

            var rows = new List<IReadOnlyList<string?>> { ExpectedHeader };

            foreach (var comment in persisted)
            {
                rows.Add(ToCsvRow(comment));
            }

            await CsvFile.WriteAsync(path, rows);

            SetSummary($"Exported {persisted.Count} comment(s) to {path}.");
        }

        /// <summary>
        /// Shows the Windows save dialog for a .csv file. Returns the chosen path (null when
        /// the user cancelled) plus the dialog's error message when it could not be shown.
        /// </summary>
        private static async Task<(string? path, string? error)> PickExportPathAsync()
        {
            try
            {
                var window = Microsoft.Maui.Controls.Application.Current?.Windows.FirstOrDefault();
                if (window is null)
                {
                    return (null, "the app window could not be found");
                }

                var savePicker = new Windows.Storage.Pickers.FileSavePicker();
                WinRT.Interop.InitializeWithWindow.Initialize(
                    savePicker,
                    WinRT.Interop.WindowNative.GetWindowHandle(window));

                savePicker.SuggestedStartLocation = Windows.Storage.Pickers.PickerLocationId.DocumentsLibrary;
                savePicker.FileTypeChoices.Add("CSV file", new List<string> { ".csv" });
                savePicker.SuggestedFileName = $"comments-{DateTime.Now:yyyy-MM-dd-HHmm}";

                var file = await savePicker.PickSaveFileAsync();

                return (file?.Path, null);
            }
            catch (Exception ex)
            {
                return (null, ex.Message);
            }
        }

        /// <summary>
        /// Reads a CSV file (the same format the export writes, or a hand-made sheet with
        /// matching headers) and upserts every row into traffichuntdb keyed on CommentId,
        /// so re-importing the same file updates rather than duplicates.
        /// </summary>
        private async Task ImportCommentsAsync()
        {
            var file = await FilePicker.PickAsync(new PickOptions
            {
                PickerTitle = "Import comments",
                FileTypes = CsvFileType
            });

            if (file is null)
            {
                return;
            }

            List<string[]> rows;

            try
            {
                rows = await CsvFile.ReadAsync(file.FullPath);
            }
            catch (Exception ex)
            {
                SetSummary($"Could not read the CSV file: {ex.Message}");
                return;
            }

            if (rows.Count == 0)
            {
                SetSummary("The CSV file is empty, nothing to import.");
                return;
            }

            var map = BuildHeaderMap(rows[0]);
            if (map is null)
            {
                SetSummary(
                    "The CSV header row does not match. Expected columns: " +
                    $"{string.Join(", ", ExpectedHeader)}. Use \"Export comments to CSV\" to get the right format.");
                return;
            }

            var imported = new List<Comment>();
            var skipped = 0;

            foreach (var row in rows.Skip(1))
            {
                var comment = ParseCsvRow(row, map);
                if (comment is null)
                {
                    skipped++;
                    continue;
                }

                imported.Add(comment);
            }

            if (imported.Count == 0)
            {
                SetSummary(
                    "No importable rows found (CommentId, AuthorName, CommentText and VideoId are required)." +
                    (skipped > 0 ? $" {skipped} row(s) skipped." : string.Empty));
                return;
            }

            var saved = await SaveImportedCommentsAsync(imported);
            if (saved is null)
            {
                return;
            }

            SetSummary(
                $"Imported {imported.Count} comment(s): {saved.Value.added} added, {saved.Value.updated} updated." +
                (skipped > 0 ? $" {skipped} row(s) skipped." : string.Empty));

            await LoadPersistedCommentsAsync();
        }

        /// <summary>Upserts imported rows by CommentId. Returns (added, updated), or null on failure.</summary>
        private async Task<(int added, int updated)?> SaveImportedCommentsAsync(List<Comment> imported)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var ids = imported.Select(comment => comment.CommentId).ToList();
                var existing = await db.Comments
                    .Where(comment => ids.Contains(comment.CommentId))
                    .ToDictionaryAsync(comment => comment.CommentId);

                var now = DateTime.UtcNow;
                var added = 0;
                var updated = 0;

                foreach (var comment in imported)
                {
                    if (existing.TryGetValue(comment.CommentId, out var stored))
                    {
                        stored.VideoId = comment.VideoId;
                        stored.VideoTitle = comment.VideoTitle;
                        stored.AuthorName = comment.AuthorName;
                        stored.CommentText = comment.CommentText;
                        stored.Keyword = comment.Keyword;
                        stored.PublishedAt = comment.PublishedAt;
                        stored.LikeCount = comment.LikeCount;
                        stored.RemovedByAi = false;
                        stored.UpdatedAt = now;
                        updated++;
                    }
                    else
                    {
                        comment.CreatedAt = now;
                        comment.UpdatedAt = now;
                        db.Comments.Add(comment);
                        added++;
                    }
                }

                await db.SaveChangesAsync();
                return (added, updated);
            }
            catch (Exception ex)
            {
                SetSummary($"Could not save the imported comments: {ex.Message}");
                return null;
            }
        }

        private static readonly string[] ExpectedHeader =
        [
            "CommentId", "AuthorName", "CommentText", "VideoId", "VideoTitle",
            "Keyword", "PublishedAt", "LikeCount"
        ];

        /// <summary>
        /// Maps each header cell to its column index. Returns null unless every expected
        /// column is present, so hand-made sheets cannot silently lose data.
        /// </summary>
        private static Dictionary<string, int>? BuildHeaderMap(IReadOnlyList<string> header)
        {
            var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            for (var i = 0; i < header.Count; i++)
            {
                var name = header[i].Trim();
                if (name.Length > 0 && !map.ContainsKey(name))
                {
                    map[name] = i;
                }
            }

            foreach (var expected in ExpectedHeader)
            {
                if (!map.ContainsKey(expected))
                {
                    return null;
                }
            }

            return map;
        }

        private static IReadOnlyList<string?> ToCsvRow(Comment comment)
        {
            return
            [
                comment.CommentId,
                comment.AuthorName,
                comment.CommentText,
                comment.VideoId,
                comment.VideoTitle,
                comment.Keyword,
                comment.PublishedAt?.ToString("O") ?? string.Empty,
                comment.LikeCount.ToString(CultureInfo.InvariantCulture)
            ];
        }

        /// <summary>Turns one CSV row into a comment, or null when the row has no usable key.</summary>
        private static Comment? ParseCsvRow(IReadOnlyList<string> row, Dictionary<string, int> map)
        {
            string? Cell(string column)
            {
                return map.TryGetValue(column, out var index) && index < row.Count
                    ? row[index].Trim()
                    : null;
            }

            var commentId = Cell("CommentId");
            var authorName = Cell("AuthorName");
            var commentText = Cell("CommentText");
            var videoId = Cell("VideoId");

            if (string.IsNullOrEmpty(commentId) ||
                string.IsNullOrEmpty(authorName) ||
                string.IsNullOrEmpty(commentText) ||
                string.IsNullOrEmpty(videoId))
            {
                return null;
            }

            DateTimeOffset? publishedAt = null;

            if (DateTimeOffset.TryParse(Cell("PublishedAt"), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var published))
            {
                publishedAt = published;
            }

            _ = long.TryParse(Cell("LikeCount"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var likeCount);

            return new Comment
            {
                CommentId = commentId,
                AuthorName = authorName,
                CommentText = commentText,
                VideoId = videoId,
                VideoTitle = Cell("VideoTitle") ?? string.Empty,
                Keyword = Cell("Keyword") ?? string.Empty,
                PublishedAt = publishedAt,
                LikeCount = likeCount
            };
        }

        private async void OnClearClicked(object? sender, EventArgs e)
        {
            if (_isBusy || _aiRun is not null || _aiReplyRun is not null)
            {
                SetSummary("Wait for the current fetch / AI run to finish (or stop it) before clearing.");
                return;
            }

            const string clearView = "Clear view only";
            const string deleteShown = "Delete shown rows (view filter)";
            const string deleteAll = "Delete ALL saved comments";

            var choice = await DisplayActionSheetAsync(
                "Clear options",
                "Cancel",
                null,
                [clearView, deleteShown, deleteAll]);

            if (string.IsNullOrWhiteSpace(choice) || choice == "Cancel")
            {
                return;
            }

            if (choice == clearView)
            {
                ClearTableOnly("Table cleared. Saved comments stay in traffichuntdb and load again next time the page opens.");
                return;
            }

            if (choice == deleteShown)
            {
                if (_rows.Count == 0)
                {
                    SetSummary("Nothing shown to delete - the table filter is empty.");
                    return;
                }

                var ok = await DisplayAlertAsync(
                    "Delete shown rows?",
                    $"Hard-delete the {_rows.Count} shown comment(s) from traffichuntdb? This cannot be undone.",
                    "Delete",
                    "Cancel");

                if (!ok)
                {
                    return;
                }

                var ids = _rows.Select(row => row.CommentId).ToList();
                var removed = await DeleteCommentsFromDbAsync(ids);
                ClearTableOnly($"Deleted {removed} comment(s) from traffichuntdb. Table cleared.");
                return;
            }

            // Delete ALL saved comments (including AI-removed history).
            var count = await CountCommentsInDbAsync();
            var confirm = await DisplayAlertAsync(
                "Delete ALL comments?",
                count == 0
                    ? "traffichuntdb Comments is already empty. Clear the table view instead?"
                    : $"Hard-delete ALL {count} saved comment(s) from traffichuntdb, including AI-removed history? This cannot be undone.",
                count == 0 ? "Clear view" : "Delete all",
                "Cancel");

            if (!confirm)
            {
                return;
            }

            if (count == 0)
            {
                ClearTableOnly("Table cleared. traffichuntdb Comments was already empty.");
                return;
            }

            var deleted = await DeleteAllCommentsFromDbAsync();
            ClearTableOnly($"Deleted all {deleted} saved comment(s) from traffichuntdb.");
        }

        /// <summary>Clears the on-screen table without touching traffichuntdb.</summary>
        private void ClearTableOnly(string message)
        {
            _stopRequested = true;
            _aiRun?.Cancel();
            _aiReplyRun?.Cancel();
            _allRows.Clear();
            _rows.Clear();
            ClearSelection();
            ApplyFilter();
            UpdateAiUi();
            SetSummary(message);
        }

        private async Task<int> CountCommentsInDbAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await db.Comments.CountAsync();
            }
            catch (Exception ex)
            {
                SetSummary($"Could not count saved comments: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Hard-deletes the given YouTube comment ids from traffichuntdb.</summary>
        /// <returns>How many rows were deleted.</returns>
        private async Task<int> DeleteCommentsFromDbAsync(IReadOnlyList<string> commentIds)
        {
            if (commentIds.Count == 0)
            {
                return 0;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                // Batch the IN-clause so very large selections do not blow the query up.
                const int chunk = 500;
                var deleted = 0;

                for (var start = 0; start < commentIds.Count; start += chunk)
                {
                    var ids = commentIds.Skip(start).Take(chunk).ToList();
                    deleted += await db.Comments
                        .Where(comment => ids.Contains(comment.CommentId))
                        .ExecuteDeleteAsync();
                }

                return deleted;
            }
            catch (Exception ex)
            {
                SetSummary($"Delete failed, nothing was removed: {ex.Message}");
                return 0;
            }
        }

        /// <summary>Hard-deletes every row in traffichuntdb Comments, including AI-removed history.</summary>
        private async Task<int> DeleteAllCommentsFromDbAsync()
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                return await db.Comments.ExecuteDeleteAsync();
            }
            catch (Exception ex)
            {
                SetSummary($"Delete-all failed, nothing was removed: {ex.Message}");
                return 0;
            }
        }

        private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

        private void OnSortChanged(object? sender, EventArgs e) => ApplyFilter();

        private void OnToolbarToggleClicked(object? sender, EventArgs e)
        {
            ToolbarContent.IsVisible = !ToolbarContent.IsVisible;
            ToolbarToggleButton.Text = ToolbarContent.IsVisible ? "Hide" : "Show";
        }

        private void OnAiSectionToggleClicked(object? sender, EventArgs e)
        {
            AiSectionContent.IsVisible = !AiSectionContent.IsVisible;
            AiSectionToggleButton.Text = AiSectionContent.IsVisible ? "Hide" : "Show";
        }

        /// <summary>
        /// Step 2 only makes sense once comments are fetched, so the button state and the
        /// explanation underneath follow the table instead of a switch.
        /// </summary>
        private void UpdateAiUi()
        {
            var hasRows = _allRows.Count > 0;
            var aiRunning = _aiRun is not null;
            var replyRunning = _aiReplyRun is not null;

            // Keep the button ENABLED while running so it doubles as "Stop AI".
            AiQualifyButton.IsEnabled = (hasRows && !_isBusy && !replyRunning) || aiRunning;
            AiPromptEditor.IsEnabled = !aiRunning && !_isBusy && !replyRunning;

            // Progress only lives while the AI is working.
            AiProgressPanel.IsVisible = aiRunning;

            if (!aiRunning)
            {
                AiProgressBar.Progress = 0;
                AiProgressPercentLabel.Text = "0%";
                AiProgressDetailLabel.Text = "Waiting...";
            }

            AiStateLabel.Text = aiRunning
                ? "AI is reading the list now. Press \"Stop AI\" to cancel after the current batch."
                : hasRows
                    ? "List is ready. Press \"AI qualify\" to send it to Ollama and keep only the matches."
                    : "Fetch comments first - the AI qualify button lights up once the table has rows.";

            AiPromptHintLabel.Text =
                "The AI prompt is applied after the fetch, never during it. " +
                "Removed comments stay gone until the next fetch. The prompt is remembered for next time.";

            // The Step 3 auto-reply section follows the same table state.
            UpdateAiReplyUi();
        }

        /// <summary>Locks the UI while the AI list run is in flight.</summary>
        private void SetAiRunning(bool running)
        {
            AiQualifyButton.Text = running ? "Stop AI" : "AI qualify";
            FetchButton.IsEnabled = !running;
            ClearButton.IsEnabled = !running;
            BusyIndicator.IsRunning = running || _isBusy;
            BusyIndicator.IsVisible = running || _isBusy;

            if (running)
            {
                AiProgressBar.Progress = 0;
                AiProgressPercentLabel.Text = "0%";
                AiProgressDetailLabel.Text = "Starting...";
            }

            // UpdateAiUi() is the single place that shows/hides the panel:
            // visible only while _aiRun != null, hidden + reset otherwise.
            UpdateAiUi();
        }

        /// <summary>
        /// Stores the qualify prompt in appsettings.json (Hunt:DefaultPrompt) so it is still
        /// there the next time the app starts. Failing to save must never break a fetch.
        /// </summary>
        private static Task SavePromptAsync(string prompt) => SaveHuntPromptAsync("DefaultPrompt", prompt);

        /// <summary>Writes one value into the Hunt section of appsettings.json.</summary>
        private static async Task SaveHuntPromptAsync(string key, string value)
        {
            try
            {
                var path = Path.Combine(AppContext.BaseDirectory, "appsettings.json");

                var root = File.Exists(path)
                    ? JsonNode.Parse(await File.ReadAllTextAsync(path)) as JsonObject ?? new JsonObject()
                    : new JsonObject();

                if (root["Hunt"] is not JsonObject hunt)
                {
                    hunt = new JsonObject();
                    root["Hunt"] = hunt;
                }

                hunt[key] = value;

                await File.WriteAllTextAsync(
                    path,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));
            }
            catch
            {
                // Saving the prompt is a convenience only.
            }
        }

        private void OnCommentSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is CommentRow row)
            {
                ShowRow(row);
            }
            else
            {
                ClearSelection();
            }
        }

        private async void OnOpenClicked(object? sender, EventArgs e)
        {
            if (_selectedRow is null)
            {
                return;
            }

            await Launcher.Default.OpenAsync(_selectedRow.WatchUrl);
        }

        private async void OnCopyClicked(object? sender, EventArgs e)
        {
            if (_selectedRow is null)
            {
                return;
            }

            await Clipboard.Default.SetTextAsync(_selectedRow.CommentText);
            SetSummary("Comment copied to the clipboard.");
        }

        private async void OnCopyAuthorClicked(object? sender, EventArgs e)
        {
            if (_selectedRow is null)
            {
                return;
            }

            await Clipboard.Default.SetTextAsync(
                $"{_selectedRow.AuthorName}: {_selectedRow.CommentText}{Environment.NewLine}{_selectedRow.WatchUrl}");

            SetSummary("Author, comment and link copied to the clipboard.");
        }

        /// <summary>Rebuilds the visible table from the filter, sort and lead switches.</summary>
        private void ApplyFilter()
        {
            var filter = FilterEntry.Text?.Trim();
            IEnumerable<CommentRow> matches = _allRows;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                matches = matches.Where(row =>
                    row.AuthorName.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    row.CommentText.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    row.VideoTitle.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            matches = SortPicker.SelectedIndex switch
            {
                1 => matches.OrderByDescending(row => row.PublishedAt),
                2 => matches.OrderByDescending(row => row.LikeCount),
                3 => matches.OrderBy(row => row.AuthorName, StringComparer.OrdinalIgnoreCase),
                _ => matches
            };

            var selectedCommentId = _selectedRow?.CommentId;

            _rows.Clear();

            var number = 1;
            foreach (var row in matches)
            {
                row.Number = number++;
                _rows.Add(row);
            }

            CountLabel.Text = $"{_rows.Count} of {_allRows.Count} shown";

            var selection = selectedCommentId is null
                ? null
                : _rows.FirstOrDefault(row => row.CommentId == selectedCommentId);

            if (selection != null)
            {
                CommentList.SelectedItem = selection;
                ShowRow(selection);
            }
            else
            {
                ClearSelection();
            }
        }

        private void ShowRow(CommentRow row)
        {
            _selectedRow = row;

            EmptyDetailLabel.IsVisible = false;
            DetailPanel.IsVisible = true;
            DetailHeaderLabel.Text = row.AuthorName;
            DetailMetaLabel.Text = $"{row.PostedText}  |  {row.LikeCountText} like(s)  |  {row.VideoTitle}";
            DetailCommentLabel.Text = row.CommentText;
            ReplyEditor.Text = row.ReplyText;
            UpdateReplyStatus(row);
        }

        /// <summary>Step 3a: drafts one AI reply for the selected comment into the editor.</summary>
        private async void OnDraftReplyClicked(object? sender, EventArgs e)
        {
            if (_selectedRow is null)
            {
                return;
            }

            if (_isBusy || _aiRun is not null || _aiReplyRun is not null)
            {
                SetSummary("Wait for the current fetch / AI run to finish (or stop it) first.");
                return;
            }

            var prompt = AiReplyPromptEditor.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                SetSummary("Write the AI reply prompt first (Step 3 - AI auto-reply above).");
                return;
            }

            await SaveHuntPromptAsync("DefaultReplyPrompt", prompt);

            SetSummary($"Drafting an AI reply for {_selectedRow.AuthorName}...");

            try
            {
                var reply = (await _aiService.AIGeneratedReplyAsync(_selectedRow.CommentText, prompt)).Trim();

                ReplyEditor.Text = reply;

                SetSummary(string.IsNullOrWhiteSpace(reply)
                    ? "The AI returned an empty reply - check the LLM URL and model on the Settings page."
                    : "AI reply drafted. Edit it if you like, then press \"Send reply\".");
            }
            catch (Exception ex)
            {
                SetSummary($"AI draft failed: {ex.Message}");
            }
        }

        /// <summary>Step 3b: sends the typed reply for the selected comment to YouTube.</summary>
        private async void OnSendReplyClicked(object? sender, EventArgs e)
        {
            if (_selectedRow is null)
            {
                return;
            }

            if (_isBusy || _aiRun is not null || _aiReplyRun is not null)
            {
                SetSummary("Wait for the current fetch / AI run to finish (or stop it) first.");
                return;
            }

            var reply = ReplyEditor.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(reply))
            {
                SetSummary("Write a reply first (or draft one with the AI).");
                return;
            }

            SetBusy(true);

            try
            {
                var author = _selectedRow.AuthorName;

                await PostReplyAsync(_selectedRow, reply);

                SetSummary($"Reply sent to {author}. It is saved in traffichuntdb and shown in the Reply column.");
            }
            catch (Exception ex)
            {
                SetSummary(
                    $"Reply failed: {ex.Message} " +
                    "(posting comments requires an OAuth-authorized YouTube client, an API key alone cannot post).");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private async void OnCopyReplyClicked(object? sender, EventArgs e)
        {
            if (_selectedRow is null)
            {
                return;
            }

            await Clipboard.Default.SetTextAsync(_selectedRow.ReplyText);
            SetSummary("Reply copied to the clipboard.");
        }

        /// <summary>
        /// Step 3c: bulk auto-reply. For every comment shown in the table that has no reply
        /// yet, the AI drafts one with the reply prompt and it is posted to YouTube. Each
        /// reply is saved to traffichuntdb the moment it is sent, so a crash mid-run never
        /// loses the replies already posted.
        /// </summary>
        private async void OnAiReplyClicked(object? sender, EventArgs e)
        {
            // Button doubles as cancel while the auto-reply run is in flight.
            if (_aiReplyRun is not null)
            {
                _aiReplyRun.Cancel();
                SetSummary("Stopping the AI auto-reply as soon as the current comment finishes...");
                return;
            }

            if (_rows.Count == 0)
            {
                SetSummary("Nothing shown to reply to - fetch comments first or clear the filter.");
                return;
            }

            if (_isBusy || _aiRun is not null)
            {
                SetSummary("Wait for the current fetch / AI run to finish (or stop it) first.");
                return;
            }

            var prompt = AiReplyPromptEditor.Text?.Trim() ?? string.Empty;

            if (string.IsNullOrWhiteSpace(prompt))
            {
                SetSummary("Write the AI reply prompt first - it decides how the AI responds and what it says.");
                return;
            }

            await SaveHuntPromptAsync("DefaultReplyPrompt", prompt);

            var targets = _rows.Where(row => !row.HasReply).ToList();
            var alreadyReplied = _rows.Count - targets.Count;

            if (targets.Count == 0)
            {
                SetSummary($"All {_rows.Count} shown comment(s) already have a reply. Nothing to do.");
                return;
            }

            _aiReplyRun = new CancellationTokenSource();
            SetAiReplyRunning(true);

            try
            {
                var sent = 0;
                var empty = 0;

                for (var index = 0; index < targets.Count; index++)
                {
                    _aiReplyRun.Token.ThrowIfCancellationRequested();

                    var row = targets[index];
                    var message = $"AI reply {index + 1}/{targets.Count}: drafting for {row.AuthorName}...";

                    SetSummary(message);
                    AiReplyStatusLabel.Text = message;

                    var reply = (await _aiService.AIGeneratedReplyAsync(row.CommentText, prompt)).Trim();

                    if (string.IsNullOrWhiteSpace(reply))
                    {
                        empty++;
                        continue;
                    }

                    message = $"AI reply {index + 1}/{targets.Count}: sending to {row.AuthorName}...";

                    SetSummary(message);
                    AiReplyStatusLabel.Text = message;

                    await PostReplyAsync(row, reply);
                    sent++;
                }

                var result = $"AI auto-reply finished: {sent} sent" +
                    (empty > 0 ? $", {empty} empty draft(s) skipped" : string.Empty) +
                    (alreadyReplied > 0 ? $", {alreadyReplied} already replied" : string.Empty) + ".";

                AiReplyStatusLabel.Text = result;
                SetSummary(result);
            }
            catch (OperationCanceledException)
            {
                AiReplyStatusLabel.Text = "AI auto-reply stopped.";
                SetSummary("AI auto-reply stopped. Replies already sent stay saved.");
            }
            catch (Exception ex)
            {
                AiReplyStatusLabel.Text =
                    $"AI auto-reply failed: {ex.Message} " +
                    "(posting comments requires an OAuth-authorized YouTube client).";
                SetSummary($"AI auto-reply stopped after a failure: {ex.Message}");
            }
            finally
            {
                _aiReplyRun.Dispose();
                _aiReplyRun = null;
                SetAiReplyRunning(false);
            }
        }

        private void OnAiReplyStopClicked(object? sender, EventArgs e)
        {
            if (_aiReplyRun is null)
            {
                return;
            }

            _aiReplyRun.Cancel();
            SetSummary("Stopping the AI auto-reply as soon as the current comment finishes...");
        }

        /// <summary>
        /// Posts one reply to YouTube (comments.insert with the comment as the parent), then
        /// stores it on the Comment row in traffichuntdb and updates the table row.
        /// </summary>
        private async Task PostReplyAsync(CommentRow row, string reply)
        {
            using var scope = _scopeFactory.CreateScope();
            var messenger = scope.ServiceProvider.GetRequiredService<IMessengerService>();

            await messenger.SendMessageAsync(new MessengerDto
            {
                CommentId = row.CommentId,
                Messege = reply
            });

            var repliedAt = DateTime.UtcNow;

            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var stored = await db.Comments
                .FirstOrDefaultAsync(comment => comment.CommentId == row.CommentId);

            if (stored is not null)
            {
                stored.ReplyText = reply;
                stored.RepliedAt = repliedAt;
                stored.UpdatedAt = repliedAt;
                await db.SaveChangesAsync();
            }

            row.ReplyText = reply;
            row.RepliedAt = repliedAt;

            if (_selectedRow == row)
            {
                UpdateReplyStatus(row);
            }
        }

        /// <summary>Restores stored reply state onto freshly fetched rows.</summary>
        private async Task ApplyStoredRepliesAsync(IEnumerable<YouTubeCommentDto> comments)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                var ids = comments.Select(comment => comment.YouTubeCommentId).ToList();

                var stored = await db.Comments
                    .AsNoTracking()
                    .Where(comment => ids.Contains(comment.CommentId) && comment.RepliedAt != null)
                    .Select(comment => new { comment.CommentId, comment.ReplyText, comment.RepliedAt })
                    .ToDictionaryAsync(comment => comment.CommentId);

                foreach (var row in _allRows)
                {
                    if (stored.TryGetValue(row.CommentId, out var reply))
                    {
                        row.ReplyText = reply.ReplyText;
                        row.RepliedAt = reply.RepliedAt;
                    }
                }
            }
            catch
            {
                // Restoring replies after a refetch is a convenience; never break a fetch.
            }
        }

        private void UpdateReplyStatus(CommentRow row)
        {
            ReplyStatusLabel.Text = row.RepliedAt is null
                ? "Not replied yet. Write a reply, draft one with the AI, then send it."
                : $"Replied {row.RepliedAt.Value.ToLocalTime():yyyy-MM-dd HH:mm} - saved in traffichuntdb and shown in the Reply column.";
        }

        /// <summary>Locks the UI while the bulk AI auto-reply run is in flight.</summary>
        private void SetAiReplyRunning(bool running)
        {
            AiReplyButton.Text = running ? "Stop AI reply" : "AI reply to shown comments";
            FetchButton.IsEnabled = !running;
            ClearButton.IsEnabled = !running;
            CsvButton.IsEnabled = !running;
            DraftReplyButton.IsEnabled = !running;
            SendReplyButton.IsEnabled = !running;
            AiReplyBusyIndicator.IsVisible = running;
            AiReplyBusyIndicator.IsRunning = running;
            BusyIndicator.IsRunning = running || _isBusy;
            BusyIndicator.IsVisible = running || _isBusy;

            UpdateAiUi();
        }

        /// <summary>
        /// Step 3 only makes sense once comments are fetched, so the button state follows
        /// the table like the AI qualify section does.
        /// </summary>
        private void UpdateAiReplyUi()
        {
            var hasRows = _allRows.Count > 0;
            var replyRunning = _aiReplyRun is not null;
            var qualifyRunning = _aiRun is not null;

            // Keep the button ENABLED while running so it doubles as "Stop AI reply".
            AiReplyButton.IsEnabled = (hasRows && !_isBusy && !qualifyRunning) || replyRunning;
            AiReplyStopButton.IsEnabled = replyRunning;
            AiReplyPromptEditor.IsEnabled = !replyRunning && !_isBusy && !qualifyRunning;

            if (!replyRunning)
            {
                AiReplyStatusLabel.Text = hasRows
                    ? "Set the reply prompt, then press \"AI reply to shown comments\"."
                    : "Fetch comments first - the AI reply button lights up once the table has rows.";
            }
        }

        private void ClearSelection()
        {
            _selectedRow = null;
            CommentList.SelectedItem = null;
            DetailPanel.IsVisible = false;
            EmptyDetailLabel.IsVisible = true;
        }

        private string BuildFetchSummary(int videoCount, int fetched)
        {
            var text = $"{fetched} comment(s) from {videoCount} video(s). " +
                "Now press \"AI qualify\" to send the list to the AI, or keep browsing the full list.";

            if (_stopRequested)
            {
                text += " Stopped early.";
            }

            return text;
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
                SetSummary($"Something went wrong: {ex.Message}");
            }
            finally
            {
                SetBusy(false);
            }
        }

        private void SetBusy(bool isBusy)
        {
            _isBusy = isBusy;

            BusyIndicator.IsRunning = isBusy;
            BusyIndicator.IsVisible = isBusy;
            FetchButton.IsEnabled = !isBusy;
            ClearButton.IsEnabled = !isBusy;
            CsvButton.IsEnabled = !isBusy;
            StopButton.IsEnabled = isBusy;
            MaxVideosEntry.IsEnabled = !isBusy;
            MaxCommentsEntry.IsEnabled = !isBusy;
            OrderPicker.IsEnabled = !isBusy;
            DraftReplyButton.IsEnabled = !isBusy;
            SendReplyButton.IsEnabled = !isBusy;

            UpdateAiUi();
        }

        private void SetSummary(string message) => SummaryLabel.Text = message;

        private static int ParsePositive(string? text, int fallback)
        {
            return int.TryParse(text, out var value) && value > 0 ? value : fallback;
        }

        private static string Shorten(string text, int maxLength)
        {
            return text.Length <= maxLength ? text : $"{text[..maxLength]}...";
        }
    }
}
