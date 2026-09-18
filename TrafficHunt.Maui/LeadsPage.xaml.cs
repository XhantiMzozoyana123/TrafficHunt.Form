using System.Collections.ObjectModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;
using TrafficHunt.Domain.Entities;

namespace TrafficHunt.Maui
{
    public partial class LeadsPage : ContentPage
    {
        private readonly IServiceScopeFactory _scopeFactory;
        private readonly IAIService _aiService;
        private readonly ObservableCollection<Lead> _leads = new();

        private List<Lead> _allLeads = new();
        private Lead? _selectedLead;

        public LeadsPage(IServiceScopeFactory scopeFactory, IAIService aiService)
        {
            InitializeComponent();

            _scopeFactory = scopeFactory;
            _aiService = aiService;

            LeadList.ItemsSource = _leads;
            BulkModePicker.SelectedIndex = 0;
        }

        protected override void OnAppearing()
        {
            base.OnAppearing();
            _ = LoadLeadsAsync();
        }

        private async void OnRefreshClicked(object? sender, EventArgs e) => await LoadLeadsAsync();

        private void OnFilterChanged(object? sender, TextChangedEventArgs e) => ApplyFilter();

        private void OnLeadSelected(object? sender, SelectionChangedEventArgs e)
        {
            if (e.CurrentSelection.FirstOrDefault() is Lead lead)
            {
                ShowLead(lead);
            }
            else
            {
                ClearSelection();
            }
        }

        private async Task LoadLeadsAsync(long? selectedLeadId = null)
        {
            await RunBusyAsync(async () =>
            {
                using var scope = _scopeFactory.CreateScope();
                var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                _allLeads = await context.Leads
                    .OrderByDescending(lead => lead.CreatedAt)
                    .ToListAsync();

                ApplyFilter(selectedLeadId);
            });
        }

        private void ApplyFilter(long? selectedLeadId = null)
        {
            var filter = FilterEntry.Text?.Trim();

            IEnumerable<Lead> matches = _allLeads;

            if (!string.IsNullOrWhiteSpace(filter))
            {
                matches = matches.Where(lead =>
                    lead.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    lead.Keyword.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                    lead.CommentText.Contains(filter, StringComparison.OrdinalIgnoreCase));
            }

            _leads.Clear();
            foreach (var lead in matches)
            {
                _leads.Add(lead);
            }

            var selection = selectedLeadId.HasValue
                ? _leads.FirstOrDefault(lead => lead.Id == selectedLeadId.Value)
                : null;

            if (selection != null)
            {
                LeadList.SelectedItem = selection;
                ShowLead(selection);
            }
            else
            {
                ClearSelection();
            }
        }

        private void ShowLead(Lead lead)
        {
            _selectedLead = lead;

            DetailPanel.IsVisible = true;
            EmptyDetailLabel.IsVisible = false;

            DetailNameLabel.Text = lead.Name;
            DetailMetaLabel.Text = $"video: {lead.VideoId}{Environment.NewLine}comment: {lead.CommentId}";
            DetailCommentLabel.Text = lead.CommentText;
            ReplyEditor.Text = lead.YourReply;

            StatusLabel.Text = string.Empty;
        }

        private void ClearSelection()
        {
            _selectedLead = null;
            LeadList.SelectedItem = null;
            DetailPanel.IsVisible = false;
            EmptyDetailLabel.IsVisible = true;
        }

        private async void OnGenerateClicked(object? sender, EventArgs e)
        {
            var lead = _selectedLead;
            if (lead == null)
            {
                return;
            }

            await RunBusyAsync(async () =>
            {
                var prompt = $"""
                    You are replying to a YouTube comment on behalf of a business.
                    The comment author is "{lead.Name}" and this lead was found with the keyword "{lead.Keyword}".
                    Write a short, friendly and professional reply of maximum 3 sentences.
                    Do not use hashtags or emojis.
                    """;

                ReplyEditor.Text = (await _aiService.AIGeneratedReplyAsync(lead.CommentText, prompt)).Trim();
                StatusLabel.Text = "AI reply drafted. Review it before sending.";
            });
        }

        private async void OnSendClicked(object? sender, EventArgs e)
        {
            var lead = _selectedLead;
            if (lead == null)
            {
                return;
            }

            var reply = ReplyEditor.Text?.Trim();
            if (string.IsNullOrWhiteSpace(reply))
            {
                StatusLabel.Text = "Write or generate a reply first.";
                return;
            }

            await RunBusyAsync(async () =>
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var messengerService = scope.ServiceProvider.GetRequiredService<IMessengerService>();
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    try
                    {
                        await messengerService.SendMessageAsync(new MessengerDto
                        {
                            CommentId = lead.CommentId,
                            Messege = reply
                        });
                    }
                    catch (Exception ex)
                    {
                        StatusLabel.Text =
                            $"Could not post the reply: {ex.Message} " +
                            "(posting comments requires an OAuth-authorized YouTube client).";
                        return;
                    }

                    var stored = await context.Leads.FirstOrDefaultAsync(item => item.Id == lead.Id);

                    if (stored != null)
                    {
                        stored.YourReply = reply;
                        stored.Replied = true;
                        stored.UpdatedAt = DateTime.UtcNow;
                        await context.SaveChangesAsync();
                    }
                }

                await LoadLeadsAsync(lead.Id);
                StatusLabel.Text = "Reply sent and saved.";
            });
        }

        private async void OnOpenClicked(object? sender, EventArgs e)
        {
            var lead = _selectedLead;
            if (lead == null)
            {
                return;
            }

            try
            {
                await Launcher.Default.OpenAsync($"https://www.youtube.com/watch?v={lead.VideoId}&lc={lead.CommentId}");
            }
            catch (Exception ex)
            {
                StatusLabel.Text = $"Could not open the link: {ex.Message}";
            }
        }

        private async void OnDeleteClicked(object? sender, EventArgs e)
        {
            var lead = _selectedLead;
            if (lead == null)
            {
                return;
            }

            var confirmed = await DisplayAlertAsync(
                "Delete lead",
                $"Delete the lead from {lead.Name}?",
                "Delete",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            await RunBusyAsync(async () =>
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var stored = await context.Leads.FirstOrDefaultAsync(item => item.Id == lead.Id);

                    if (stored != null)
                    {
                        context.Leads.Remove(stored);
                        await context.SaveChangesAsync();
                    }
                }

                await LoadLeadsAsync();
            });
        }

        private void OnBulkModeChanged(object? sender, EventArgs e)
        {
            var roundRobin = BulkModePicker.SelectedIndex == 1;
            var aiGenerated = BulkModePicker.SelectedIndex == 2;

            BulkMessagesLabel.IsVisible = roundRobin;
            BulkMessagesEditor.IsVisible = roundRobin;
            BulkMessageLabel.Text = aiGenerated ? "AI prompt" : "Message";
            BulkMessageEditor.Placeholder = aiGenerated
                ? "Describe the reply the AI should write for each lead..."
                : "Hi {there|friend}, thanks for reaching out...";
        }

        private async void OnBulkSendClicked(object? sender, EventArgs e)
        {
            if (!TryCreateBulkDto(out var messengerDto))
            {
                return;
            }

            var confirmed = await DisplayAlertAsync(
                "Bulk reply",
                "Send a reply to every lead that has not been replied to yet?",
                "Send",
                "Cancel");

            if (!confirmed)
            {
                return;
            }

            await RunBusyAsync(async () =>
            {
                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();

                    if (!await context.Leads.AnyAsync(lead => !lead.Replied))
                    {
                        StatusLabel.Text = "There are no pending leads to reply to.";
                        return;
                    }
                }

                try
                {
                    using var scope = _scopeFactory.CreateScope();
                    var messengerService = scope.ServiceProvider.GetRequiredService<IMessengerService>();
                    await messengerService.SendAllMessagesAsync(messengerDto);
                }
                catch (Exception ex)
                {
                    StatusLabel.Text =
                        $"Bulk reply stopped: {ex.Message} " +
                        "(posting comments requires an OAuth-authorized YouTube client).";
                }

                await LoadLeadsAsync();

                using (var scope = _scopeFactory.CreateScope())
                {
                    var context = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
                    var remaining = await context.Leads.CountAsync(lead => !lead.Replied);
                    StatusLabel.Text = $"Bulk reply finished. Pending leads remaining: {remaining}.";
                }
            });
        }

        private bool TryCreateBulkDto(out MessengerDto messengerDto)
        {
            messengerDto = new MessengerDto();

            switch (BulkModePicker.SelectedIndex)
            {
                case 1:
                    var variants = SplitLines(BulkMessagesEditor.Text);
                    if (variants.Count == 0)
                    {
                        StatusLabel.Text = "Add at least one message variant.";
                        return false;
                    }

                    messengerDto.Messages = variants;
                    messengerDto.UseRoundRobin = true;
                    break;

                case 2:
                    var prompt = BulkMessageEditor.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(prompt))
                    {
                        StatusLabel.Text = "Describe the reply the AI should write.";
                        return false;
                    }

                    messengerDto.Messege = prompt;
                    messengerDto.AiGenerated = true;
                    break;

                default:
                    var message = BulkMessageEditor.Text?.Trim();
                    if (string.IsNullOrWhiteSpace(message))
                    {
                        StatusLabel.Text = "Enter a message to send.";
                        return false;
                    }

                    messengerDto.Messege = message;
                    break;
            }

            return true;
        }

        private static List<string> SplitLines(string? text)
        {
            return (text ?? string.Empty)
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(line => line.Trim())
                .Where(line => line.Length > 0)
                .ToList();
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
            GenerateButton.IsEnabled = !isBusy;
            SendButton.IsEnabled = !isBusy;
            BulkSendButton.IsEnabled = !isBusy;
            BulkModePicker.IsEnabled = !isBusy;
        }
    }
}