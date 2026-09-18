using System;
using System.Collections.Generic;
using System.Text;
using TrafficHunt.Application.Dtos;

namespace TrafficHunt.Application.Interfaces
{
    public interface IYouTubeService
    {
        Task<List<YouTubeVideoDto>> SearchVideosAsync(SearchDto searchDto);

        Task<List<YouTubeCommentDto>> GetCommentsAsync(SearchDto searchDto, string videoId);
    }
}
