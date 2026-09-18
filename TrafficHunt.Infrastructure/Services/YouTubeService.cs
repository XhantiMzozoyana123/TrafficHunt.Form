using Google.Apis.YouTube.v3;
using System;
using System.Collections.Generic;
using System.Text;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using YoutubeExplode;
using YoutubeExplode.Search;

namespace TrafficHunt.Infrastructure.Services
{
    public class YouTubeService : IYouTubeService
    {
        private readonly YoutubeClient _youtube;
        private readonly Google.Apis.YouTube.v3.YouTubeService _youtubeDataApi;

        public YouTubeService(YoutubeClient youtube, Google.Apis.YouTube.v3.YouTubeService youtubeDataApi) 
        {
            _youtube = youtube;
            _youtubeDataApi = youtubeDataApi;
        }

        public async Task<List<YouTubeCommentDto>> GetCommentsAsync(SearchDto searchDto, string videoId)
        {
            var request = _youtubeDataApi.CommentThreads.List("snippet");

            request.VideoId = videoId;
            request.MaxResults = searchDto.MaxResult;

            var response = await request.ExecuteAsync();

            return response.Items.Select(x =>
            {
                var comment = x.Snippet.TopLevelComment;
                var snippet = comment.Snippet;

                return new YouTubeCommentDto
                {
                    YouTubeCommentId = comment.Id,
                    YouTubeVideoId = videoId,
                    AuthorChannelId = snippet.AuthorChannelId?.Value ?? "",
                    AuthorDisplayName = snippet.AuthorDisplayName,
                    Text = snippet.TextOriginal,
                    IsReply = false,
                };
            }).ToList();
        }

        public async Task<List<YouTubeVideoDto>> SearchVideosAsync(SearchDto searchDto)
        {
            var results = new List<YouTubeVideoDto>();
            var limit = searchDto.MaxResult > 0 ? searchDto.MaxResult : int.MaxValue;

            await foreach (var video in _youtube.Search.GetVideosAsync(searchDto.Keyword))
            {
                if (results.Count >= limit)
                {
                    break;
                }

                results.Add(new YouTubeVideoDto
                {
                    VideoId = video.Id,
                    Title = video.Title,
                    ChannelId = video.Author.ChannelId,
                    ChannelTitle = video.Author.ChannelTitle,
                    ThumbnailUrl = video.Thumbnails.FirstOrDefault()?.Url
                });
            }

            return results;
        }
    }
}
