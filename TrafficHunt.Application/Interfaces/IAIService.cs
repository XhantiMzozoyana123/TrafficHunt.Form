using System;
using System.Collections.Generic;
using System.Text;

namespace TrafficHunt.Application.Interfaces
{
    /// <summary>Progress snapshot for the AI comment-filter run.</summary>
    /// <param name="Processed">How many comments have been checked so far.</param>
    /// <param name="Total">How many comments will be checked in total.</param>
    /// <param name="Message">Human-readable status, e.g. which batch is running.</param>
    public sealed record AiFilterProgress(int Processed, int Total, string Message);

    public interface IAIService
    {
        public Task<bool> IsCommentRelevantAsync(string comment, string prompt);

        /// <summary>
        /// Sends the whole fetched comment list to the LLM as a JSON array and asks it to
        /// return only the ids of the comments that match the prompt. Batching keeps each
        /// request small enough for the model context.
        /// </summary>
        /// <param name="comments">Id plus text for every fetched comment.</param>
        /// <param name="onBatchCompleted">
        /// Called after every batch with that batch's comments and the ids kept from it, so
        /// the caller can persist each verdict straight away instead of waiting for the
        /// whole run to finish (a crash mid-run then never loses finished batches).
        /// </param>
        /// <returns>The ids the AI decided to keep.</returns>
        public Task<IReadOnlyList<string>> FilterCommentIdsAsync(
            IReadOnlyList<(string Id, string Author, string Text)> comments,
            string prompt,
            Action<string>? progress = null,
            CancellationToken cancellationToken = default,
            Func<IReadOnlyList<(string Id, string Author, string Text)>, IReadOnlyList<string>, Task>? onBatchCompleted = null);

        /// <summary>
        /// Same as <see cref="FilterCommentIdsAsync(IReadOnlyList{ValueTuple{string,string,string}},string,Action{string},CancellationToken,Func{IReadOnlyList{ValueTuple{string,string,string}},IReadOnlyList{string},System.Threading.Tasks.Task}?)"/>
        /// but also reports numeric progress (processed/total) so the UI can drive a progress bar.
        /// </summary>
        public Task<IReadOnlyList<string>> FilterCommentIdsAsync(
            IReadOnlyList<(string Id, string Author, string Text)> comments,
            string prompt,
            IProgress<AiFilterProgress>? progress,
            CancellationToken cancellationToken = default,
            Func<IReadOnlyList<(string Id, string Author, string Text)>, IReadOnlyList<string>, Task>? onBatchCompleted = null);

        public Task<string> AIGeneratedReplyAsync(string comment, string prompt); // This method generates a reply to a comment based on the provided prompt
    }
}
