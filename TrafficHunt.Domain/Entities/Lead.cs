using System;
using System.Collections.Generic;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace TrafficHunt.Domain.Entities
{
    public class Lead : BaseEntity
    {
        public string Name { get; set; } = string.Empty;

        public string CommentText { get; set; } = string.Empty;

        public string YourReply { get; set; } = string.Empty;

        public string VideoId { get; set; } = string.Empty;

        public string CommentId { get; set; } = string.Empty;

        public string Keyword { get; set; } = string.Empty;

        public bool Replied { get; set; } = false;
    }
}
