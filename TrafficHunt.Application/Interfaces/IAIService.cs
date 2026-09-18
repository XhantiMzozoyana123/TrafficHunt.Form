using System;
using System.Collections.Generic;
using System.Text;

namespace TrafficHunt.Application.Interfaces
{
    public interface IAIService
    {
        public Task<bool> IsCommentRelevantAsync(string comment, string prompt);

        public Task<string> AIGeneratedReplyAsync(string comment, string prompt); // This method generates a reply to a comment based on the provided prompt
    }
}
