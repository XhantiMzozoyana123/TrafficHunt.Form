using Google.Apis.YouTube.v3.Data;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;

namespace TrafficHunt.Infrastructure.Services
{
    public class MessengerService : IMessengerService
    {
        private readonly ApplicationDbContext _context;
        private readonly Google.Apis.YouTube.v3.YouTubeService _youtubeDataApi;
        private readonly IAIService _aiService;

        public MessengerService(
            Google.Apis.YouTube.v3.YouTubeService youtubeDataApi,
            ApplicationDbContext context,
            IAIService aiService)
        {
            _youtubeDataApi = youtubeDataApi;
            _context = context;
            _aiService = aiService;
        }

        public async Task SendAllMessagesAsync(MessengerDto messengerDto)
        {
            var leads = await _context.Leads
                .Where(x => x.Replied == false)
                .ToListAsync();

            if (messengerDto.Messages.Count == 0 && !messengerDto.AiGenerated)
                return;

            var messageIndex = 0;

            foreach (var lead in leads)
            {
                string message;

                if (messengerDto.AiGenerated)
                {
                    message = await _aiService.AIGeneratedReplyAsync(
                        lead.CommentText,
                        messengerDto.Messege);
                }
                else if (messengerDto.UseRoundRobin)
                {
                    message = messengerDto.Messages[messageIndex];

                    messageIndex++;

                    if (messageIndex >= messengerDto.Messages.Count)
                        messageIndex = 0;

                    message = ApplySpinText(message);
                }
                else
                {
                    message = ApplySpinText(messengerDto.Messege);
                }

                await SendMessageAsync(new MessengerDto
                {
                    CommentId = lead.CommentId,
                    Messege = message
                });

                lead.Replied = true;
            }

            await _context.SaveChangesAsync();
        }

        public async Task SendMessageAsync(MessengerDto messengerDto)
        {
            var comment = new Comment
            {
                Snippet = new CommentSnippet
                {
                    ParentId = messengerDto.CommentId,
                    TextOriginal = messengerDto.Messege
                }
            };

            await _youtubeDataApi.Comments
                .Insert(comment, "snippet")
                .ExecuteAsync();
        }

        private static string ApplySpinText(string message)
        {
            return Regex.Replace(
                message,
                @"\{([^{}]+)\}",
                match =>
                {
                    var options = match.Groups[1]
                        .Value
                        .Split('|');

                    return options[Random.Shared.Next(options.Length)];
                });
        }
    }
}