using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;
using TrafficHunt.Domain.Entities;

namespace TrafficHunt.Infrastructure.Services
{
    public class ExtractService : IExtractService
    {
        private readonly ApplicationDbContext _context;
        private readonly IYouTubeService _youTubeService;
        private readonly IAIService _aiService;

        public ExtractService(
            ApplicationDbContext context,
            IYouTubeService youTubeService,
            IAIService aiService)
        {
            _context = context;
            _youTubeService = youTubeService;
            _aiService = aiService;
        }

        public async Task ExtractVideoAsync(SearchDto searchDto)
        {
            var videos = await _youTubeService.SearchVideosAsync(searchDto);

            foreach (var video in videos)
            {
                var comments = await _youTubeService.GetCommentsAsync(
                    searchDto,
                    video.VideoId);

                foreach (var comment in comments)
                {
                    var relevant = await _aiService.IsCommentRelevantAsync(
                        comment.Text,
                        searchDto.Prompt);

                    if (relevant)
                    {
                        // Handle relevant comment
                        var lead = new Lead()
                        {
                            Name = comment.AuthorDisplayName,
                            CommentText = comment.Text,
                            YourReply = "", // You can set this later
                            VideoId = video.VideoId,
                            CommentId = comment.YouTubeCommentId,
                            Keyword = searchDto.Keyword
                        };

                        await _context.Leads.AddAsync(lead);
                        await _context.SaveChangesAsync();
                    }
                }
            }
        }
    }
}