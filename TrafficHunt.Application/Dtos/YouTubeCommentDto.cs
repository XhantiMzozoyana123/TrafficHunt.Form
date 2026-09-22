using System;
using System.Collections.Generic;
using System.Text;

namespace TrafficHunt.Application.Dtos
{
    public class YouTubeCommentDto
    {
        public string YouTubeCommentId { get; set; } = string.Empty;

        public string YouTubeVideoId { get; set; } = string.Empty;

        public string? ParentCommentId { get; set; }

        public string AuthorChannelId { get; set; } = string.Empty;

        public string AuthorDisplayName { get; set; } = string.Empty;

        public string Text { get; set; } = string.Empty;

        /// <summary>When the comment was posted (null when YouTube does not report it).</summary>
        public DateTimeOffset? PublishedAt { get; set; }

        /// <summary>How many likes the comment has, as reported by the API.</summary>
        public long LikeCount { get; set; }

        public bool IsReply { get; set; }

        public bool IsReplied { get; set; }

        public string? ReplyText { get; set; }

        public DateTime? RepliedAt { get; set; }
    }
}
