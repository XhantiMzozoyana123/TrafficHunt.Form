using System;
using System.Collections.Generic;
using System.Text;

namespace TrafficHunt.Application.Dtos
{
    public class MessengerDto
    {
        public string CommentId { get; set; } = string.Empty;

        public string Messege { get; set; } = string.Empty;

        public List<string> Messages { get; set; } = [];

        public bool UseRoundRobin { get; set; }

        public bool AiGenerated { get; set; }
    }
}