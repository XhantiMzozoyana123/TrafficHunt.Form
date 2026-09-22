using TrafficHunt.Application.Interfaces;

namespace TrafficHunt.Infrastructure.Services
{
    public class AIService : IAIService
    {
        private readonly ILLMService _llmService;

        private static readonly char[] Separators =
        {
            ' ', '\t', '\r', '\n', '.', ',', ';', ':', '!', '?', '"', '\'', '*', '`', '(', ')', '[', ']'
        };

        public AIService(ILLMService llmService)
        {
            _llmService = llmService;
        }

        public async Task<bool> IsCommentRelevantAsync(
            string comment,
            string prompt)
        {
            var fullPrompt = $"""
                Deciding rule:
                {prompt}

                Comment:
                {comment}

                Does the comment match the deciding rule above? Treat it as a match only when it
                clearly fits, not when it is merely related.
                Answer with only the single word true or false.
                """;

            var response = await _llmService.GenerateTextAsync(fullPrompt);

            return IsAffirmative(response);
        }

        /// <summary>
        /// One Ollama request per batch: the comments go in as a JSON array and the model
        /// answers with a JSON array of the ids it wants to keep. Only ids that were sent
        /// in are accepted, so a hallucinated id can never sneak into the table.
        /// </summary>
        public Task<IReadOnlyList<string>> FilterCommentIdsAsync(
            IReadOnlyList<(string Id, string Author, string Text)> comments,
            string prompt,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default,
            Func<IReadOnlyList<(string Id, string Author, string Text)>, IReadOnlyList<string>, Task>? onBatchCompleted = null)
        {
            IProgress<AiFilterProgress>? typed = progress is null
                ? null
                : new Progress<AiFilterProgress>(snapshot => progress(snapshot.Message));

            return FilterCommentIdsAsync(comments, prompt, typed, cancellationToken, onBatchCompleted);
        }

        /// <inheritdoc />
        public async Task<IReadOnlyList<string>> FilterCommentIdsAsync(
            IReadOnlyList<(string Id, string Author, string Text)> comments,
            string prompt,
            IProgress<AiFilterProgress>? progress,
            CancellationToken cancellationToken = default,
            Func<IReadOnlyList<(string Id, string Author, string Text)>, IReadOnlyList<string>, Task>? onBatchCompleted = null)
        {
            var kept = new List<string>();

            // Keep each request small: ids plus comment text stay well inside the model context.
            const int batchSize = 20;

            for (var start = 0; start < comments.Count; start += batchSize)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var batch = comments.Skip(start).Take(batchSize).ToList();

                progress?.Report(new AiFilterProgress(
                    start,
                    comments.Count,
                    $"AI reading comments {start + 1}-{start + batch.Count} of {comments.Count}..."));

                var payload = System.Text.Json.JsonSerializer.Serialize(
                    batch.Select(item => new { id = item.Id, author = item.Author, text = item.Text }));

                var fullPrompt = $"""
                    Deciding rule:
                    {prompt}

                    Here is a JSON array of YouTube comments, each with an "id", an "author" and a "text":
                    {payload}

                    Return a JSON array of the matching comment ids as plain strings, for
                    example ["c1", "c3"]. Do not wrap them in objects. If none match,
                    return [].

                    Respond with only the JSON array, no explanation.
                    """;

                var response = await _llmService.GenerateTextAsync(fullPrompt);

                cancellationToken.ThrowIfCancellationRequested();

                foreach (var id in ParseKeptIds(response))
                {
                    // Accept only ids that were actually sent in this batch.
                    if (batch.Any(item => item.Id == id) && !kept.Contains(id))
                    {
                        kept.Add(id);
                    }
                }

                // This batch is fully checked only after its reply has been parsed.
                progress?.Report(new AiFilterProgress(
                    start + batch.Count,
                    comments.Count,
                    $"AI checked {start + batch.Count} of {comments.Count} comment(s)..."));

                // Hand this batch's verdicts to the caller immediately so they can be
                // persisted in real time; a crash later in the run never loses them.
                if (onBatchCompleted is not null)
                {
                    var batchKept = batch
                        .Where(item => kept.Contains(item.Id))
                        .Select(item => item.Id)
                        .ToList();

                    await onBatchCompleted(batch, batchKept);
                }
            }

            return kept;
        }

        /// <summary>
        /// Reads the model's answer as a JSON array of id strings. Falls back to scanning the
        /// raw text for known-looking ids so a slightly chatty answer still works.
        /// </summary>
        private static IReadOnlyList<string> ParseKeptIds(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return Array.Empty<string>();
            }

            try
            {
                using var document = System.Text.Json.JsonDocument.Parse(response.Trim());

                if (document.RootElement.ValueKind == System.Text.Json.JsonValueKind.Array)
                {
                    var ids = new List<string>();

                    foreach (var element in document.RootElement.EnumerateArray())
                    {
                        // Expected shape is a plain string, but also accept {"id": "c1"}
                        // in case the model wraps the ids in objects anyway.
                        if (element.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            ids.Add(element.GetString() ?? string.Empty);
                        }
                        else if (element.ValueKind == System.Text.Json.JsonValueKind.Object
                            && element.TryGetProperty("id", out var idProperty)
                            && idProperty.ValueKind == System.Text.Json.JsonValueKind.String)
                        {
                            ids.Add(idProperty.GetString() ?? string.Empty);
                        }
                    }

                    return ids.Where(id => !string.IsNullOrWhiteSpace(id)).ToList();
                }
            }
            catch (System.Text.Json.JsonException)
            {
                // Fall through to the tolerant scan below.
            }

            // Tolerant fallback: pull anything that looks like a quoted token out of the text.
            var matches = System.Text.RegularExpressions.Regex.Matches(response, "\"([^\"]+)\"");

            return matches
                .Select(match => match.Groups[1].Value.Trim())
                .Where(id => !string.IsNullOrWhiteSpace(id))
                .Distinct()
                .ToList();
        }

        /// <summary>
        /// The Ollama model does not always answer with a bare "true"/"false"; it can return
        /// "True", "true.", "**True**" or an explanatory sentence. The reply is therefore
        /// scanned for the first decisive token instead of requiring an exact match, so a
        /// formatted answer does not silently disqualify a valid lead.
        /// </summary>
        private static bool IsAffirmative(string response)
        {
            if (string.IsNullOrWhiteSpace(response))
            {
                return false;
            }

            var tokens = response
                .ToLowerInvariant()
                .Split(Separators, StringSplitOptions.RemoveEmptyEntries);

            foreach (var token in tokens)
            {
                switch (token)
                {
                    case "true":
                    case "yes":
                        return true;

                    case "false":
                    case "no":
                        return false;
                }
            }

            return false;
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