using System;
using System.Collections.Generic;
using System.Text;
using TrafficHunt.Application.Dtos;

namespace TrafficHunt.Application.Interfaces
{
    public interface IYouTubeService
    {
        Task<List<YouTubeVideoDto>> SearchVideosAsync(SearchDto searchDto);

        /// <param name="order">
        /// "time" for newest first, anything else (or null) for YouTube's relevance order.
        /// </param>
        Task<List<YouTubeCommentDto>> GetCommentsAsync(SearchDto searchDto, string videoId, string? order = null);

        /// <summary>
        /// Performs a minimal YouTube Data API request to confirm the configured API key is
        /// accepted. Throws when the key is missing, invalid, disabled or restricted.
        /// </summary>
        Task ValidateApiKeyAsync();
    }
}
