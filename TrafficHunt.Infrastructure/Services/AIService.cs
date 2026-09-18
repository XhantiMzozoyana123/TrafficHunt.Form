using TrafficHunt.Application.Interfaces;

namespace TrafficHunt.Infrastructure.Services
{
    public class AIService : IAIService
    {
        private readonly ILLMService _llmService;

        public AIService(ILLMService llmService)
        {
            _llmService = llmService;
        }

        public async Task<bool> IsCommentRelevantAsync(
            string comment,
            string prompt)
        {
            var fullPrompt = $"""
                {prompt}

                Comment:
                {comment}

                Respond with only:
                true
                or
                false
                """;

            var response = await _llmService.GenerateTextAsync(fullPrompt);

            return response.Trim().Equals(
                "true",
                StringComparison.OrdinalIgnoreCase);
        }

        public async Task<string> AIGeneratedReplyAsync(
            string comment,
            string prompt)
        {
            var fullPrompt = $"""
                {prompt}

                Comment:
                {comment}

                Generate a natural reply to this comment.
                Respond with only the reply text.
                """;

            return await _llmService.GenerateTextAsync(fullPrompt);
        }
    }
}