namespace TrafficHunt.Domain.Entities
{
    /// <summary>
    /// A YouTube comment fetched by the comment finder. Stored in traffichuntdb so the
    /// table survives restarts; AI qualify only ever filters these persisted rows.
    /// </summary>
    public class Comment : BaseEntity
    {
        /// <summary>YouTube comment id. Unique: refetching the same comment updates it.</summary>
        public string CommentId { get; set; } = string.Empty;

        public string VideoId { get; set; } = string.Empty;

        public string VideoTitle { get; set; } = string.Empty;

        public string AuthorName { get; set; } = string.Empty;

        public string CommentText { get; set; } = string.Empty;

        /// <summary>Keyword that produced this comment.</summary>
        public string Keyword { get; set; } = string.Empty;

        /// <summary>When the comment was posted on YouTube (null when not reported).</summary>
        public DateTimeOffset? PublishedAt { get; set; }

        public long LikeCount { get; set; } = 0;

        /// <summary>True when the row survived an AI qualify pass.</summary>
        public bool KeptByAi { get; set; } = false;

        /// <summary>True when the row was removed by an AI qualify pass (kept for history).</summary>
        public bool RemovedByAi { get; set; } = false;

        /// <summary>The reply that was posted to this comment (empty when not replied yet).</summary>
        public string ReplyText { get; set; } = string.Empty;

        /// <summary>When the reply was posted on YouTube (null until a reply was sent).</summary>
        public DateTime? RepliedAt { get; set; }
    }
}
