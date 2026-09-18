using System;
using System.Collections.Generic;
using System.Text;

namespace TrafficHunt.Application.Dtos
{
    public class YouTubeVideoDto
    {
        public string VideoId { get; set; } = string.Empty;

        public string ChannelId { get; set; } = string.Empty;

        public string ChannelTitle { get; set; } = string.Empty;

        public string Title { get; set; } = string.Empty;

        public string? Description { get; set; }

        public string? ThumbnailUrl { get; set; }

        public DateTime PublishedAt { get; set; }
    }
}
