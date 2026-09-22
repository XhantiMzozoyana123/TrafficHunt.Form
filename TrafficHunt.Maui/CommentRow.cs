using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TrafficHunt.Maui
{
    /// <summary>
    /// A single row of the comments table. Numbers and the AI verdict change after the row
    /// has been created, so this raises change notifications for the bound cells.
    /// </summary>
    public class CommentRow : INotifyPropertyChanged
    {
        private int _number;

        /// <summary>Row position in the current filtered/sorted view, starting at 1.</summary>
        public int Number
        {
            get => _number;
            set => Set(ref _number, value);
        }

        public string CommentId { get; init; } = string.Empty;

        public string AuthorName { get; init; } = string.Empty;

        public string CommentText { get; init; } = string.Empty;

        public string VideoId { get; init; } = string.Empty;

        public string VideoTitle { get; init; } = string.Empty;

        public DateTimeOffset? PublishedAt { get; init; }

        public long LikeCount { get; init; }

        public string PostedText => PublishedAt is null
            ? "-"
            : PublishedAt.Value.ToLocalTime().ToString("yyyy-MM-dd");

        public string LikeCountText => LikeCount.ToString("N0");

        /// <summary>Whether this comment already has a reply.</summary>
        public bool HasReply => !string.IsNullOrWhiteSpace(ReplyText);

        /// <summary>The reply for this comment - editable in the detail panel, persisted on send.</summary>
        private string _replyText = string.Empty;
        public string ReplyText
        {
            get => _replyText;
            set
            {
                if (EqualityComparer<string>.Default.Equals(_replyText, value))
                {
                    return;
                }

                _replyText = value;
                OnPropertyChanged(nameof(ReplyText));
                OnPropertyChanged(nameof(HasReply));
                OnPropertyChanged(nameof(ReplyDisplayText));
            }
        }

        /// <summary>When the reply was posted (null until a reply was sent).</summary>
        private DateTime? _repliedAt;
        public DateTime? RepliedAt
        {
            get => _repliedAt;
            set => Set(ref _repliedAt, value);
        }

        /// <summary>Short text for the table column: the reply, or a dash when there is none.</summary>
        public string ReplyDisplayText => HasReply ? ReplyText.Replace("\r", " ").Replace("\n", " ").Trim() : "-";

        /// <summary>Deep link that opens the video with this comment highlighted.</summary>
        public string WatchUrl =>
            $"https://www.youtube.com/watch?v={VideoId}&lc={CommentId}";

        public event PropertyChangedEventHandler? PropertyChanged;

        private void Set<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
        {
            if (EqualityComparer<T>.Default.Equals(field, value))
            {
                return;
            }

            field = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        private void OnPropertyChanged(string propertyName) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
