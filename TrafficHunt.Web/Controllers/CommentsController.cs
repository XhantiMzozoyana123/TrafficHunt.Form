using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;
using Microsoft.Extensions.DependencyInjection;
using TrafficHunt.Domain.Entities;

namespace TrafficHunt.Web.Controllers
{
    public class CommentsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IYouTubeService _youTubeService;
        private readonly IAIService _aiService;
        private readonly IMessengerService _messengerService;

        public CommentsController(
            ApplicationDbContext db,
            IYouTubeService youTubeService,
            IAIService aiService,
            IMessengerService messengerService)
        {
            _db = db;
            _youTubeService = youTubeService;
            _aiService = aiService;
            _messengerService = messengerService;
        }

        /// <summary>Shows every non-removed comment from traffichuntdb.</summary>
        public async Task<IActionResult> Index()
        {
            var comments = await _db.Comments
                .AsNoTracking()
                .Where(comment => !comment.RemovedByAi)
                .OrderByDescending(comment => comment.CreatedAt)
                .ToListAsync();

            ViewData["DefaultPrompt"] = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Hunt:DefaultPrompt"] ?? string.Empty;
            ViewData["DefaultReplyPrompt"] = HttpContext.RequestServices.GetRequiredService<IConfiguration>()["Hunt:DefaultReplyPrompt"] ?? string.Empty;

            return View(comments);
        }

        /// <summary>
        /// Searches YouTube for videos with the keyword and fetches their comments into
        /// traffichuntdb (upsert on the YouTube comment id, so refetching updates instead
        /// of duplicating and keeps any reply already posted).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Fetch([FromForm] string keyword, [FromForm] int maxVideos, [FromForm] int maxComments, [FromForm] string order)
        {
            try
            {
                keyword = keyword?.Trim() ?? string.Empty;

                // Remember the inputs so the server-rendered form can show them again after the redirect.
                TempData["FetchKeyword"] = keyword;
                TempData["FetchMaxVideos"] = Math.Clamp(maxVideos, 1, 25).ToString();
                TempData["FetchMaxComments"] = Math.Clamp(maxComments, 1, 100).ToString();
                TempData["FetchOrder"] = string.IsNullOrWhiteSpace(order) ? "relevance" : order.Trim();

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    TempData["FetchError"] = "Type a keyword first."; return RedirectToAction(nameof(Index));
                }

                var videoSearch = new SearchDto
                {
                    Keyword = keyword,
                    MaxResult = Math.Clamp(maxVideos, 1, 25)
                };

                var videos = await _youTubeService.SearchVideosAsync(videoSearch);

                if (videos.Count == 0)
                {
                    TempData["FetchError"] = "No videos found."; return RedirectToAction(nameof(Index));
                }

                var added = 0;
                var updated = 0;
                var skipped = 0;
                var fetched = 0;

                foreach (var video in videos)
                {
                    List<YouTubeCommentDto> comments;

                    try
                    {
                        comments = await _youTubeService.GetCommentsAsync(
                            new SearchDto { Keyword = keyword, MaxResult = Math.Clamp(maxComments, 1, 100) },
                            video.VideoId,
                            order);
                    }
                    catch
                    {
                        // Comments are frequently disabled or closed; carry on with the other videos.
                        skipped++;
                        continue;
                    }

                    fetched += comments.Count;

                    var ids = comments.Select(comment => comment.YouTubeCommentId).ToList();
                    var existing = await _db.Comments
                        .Where(comment => ids.Contains(comment.CommentId))
                        .ToDictionaryAsync(comment => comment.CommentId);
                    var now = DateTime.UtcNow;

                    foreach (var comment in comments)
                    {
                        if (existing.TryGetValue(comment.YouTubeCommentId, out var stored))
                        {
                            stored.VideoId = video.VideoId;
                            stored.VideoTitle = video.Title;
                            stored.AuthorName = string.IsNullOrWhiteSpace(comment.AuthorDisplayName)
                                ? "(unknown author)"
                                : comment.AuthorDisplayName;
                            stored.CommentText = comment.Text;
                            stored.Keyword = keyword;
                            stored.PublishedAt = comment.PublishedAt;
                            stored.LikeCount = comment.LikeCount;
                            stored.RemovedByAi = false;
                            stored.UpdatedAt = now;
                            updated++;
                        }
                        else
                        {
                            _db.Comments.Add(new Comment
                            {
                                CommentId = comment.YouTubeCommentId,
                                VideoId = video.VideoId,
                                VideoTitle = video.Title,
                                AuthorName = string.IsNullOrWhiteSpace(comment.AuthorDisplayName)
                                    ? "(unknown author)"
                                    : comment.AuthorDisplayName,
                                CommentText = comment.Text,
                                Keyword = keyword,
                                PublishedAt = comment.PublishedAt,
                                LikeCount = comment.LikeCount,
                                CreatedAt = now,
                                UpdatedAt = now
                            });
                            added++;
                        }
                    }

                    await _db.SaveChangesAsync();
                }

                TempData["Status"] = "Got " + fetched.ToString() + " comments (" + added.ToString() + " new, " + updated.ToString() + " updated, " + skipped.ToString() + " skipped)."; return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["FetchError"] = ex.Message; return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Sends the whole comment list to the AI as one JSON payload and keeps only the
        /// matches: kept rows are flagged KeptByAi, the rest RemovedByAi (kept for history).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AiQualify([FromForm] string prompt)
        {
            try
            {
                prompt = prompt?.Trim() ?? string.Empty;

                // Remember the prompt so the server-rendered form can show it again after the redirect.
                TempData["QualifyPrompt"] = prompt;

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    TempData["QualifyError"] = "Write the AI prompt first."; return RedirectToAction(nameof(Index));
                }

                var comments = await _db.Comments
                    .Where(comment => !comment.RemovedByAi)
                    .ToListAsync();

                if (comments.Count == 0)
                {
                    TempData["QualifyError"] = "Fetch comments first."; return RedirectToAction(nameof(Index));
                }

                var payload = comments
                    .Select(comment => (comment.CommentId, comment.AuthorName, comment.CommentText))
                    .ToList();

                var keptIds = await _aiService.FilterCommentIdsAsync(payload, prompt);
                var kept = new HashSet<string>(keptIds);

                foreach (var comment in comments)
                {
                    if (kept.Contains(comment.CommentId))
                    {
                        comment.KeptByAi = true;
                        comment.RemovedByAi = false;
                    }
                    else
                    {
                        comment.RemovedByAi = true;
                        comment.KeptByAi = false;
                    }

                    comment.UpdatedAt = DateTime.UtcNow;
                }

                await _db.SaveChangesAsync();

                TempData["Status"] = "AI kept " + keptIds.Count.ToString() + "; removed " + (comments.Count - keptIds.Count).ToString() + "."; return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["QualifyError"] = ex.Message; return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Remembers which comment the reply composer should work on. This is what the Reply
        /// button on a table row posts, because the page no longer uses client-side JavaScript.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public IActionResult SelectForReply([FromForm] string commentId)
        {
            commentId = commentId?.Trim() ?? string.Empty;

            TempData["DraftCommentId"] = commentId;

            if (string.IsNullOrWhiteSpace(commentId))
            {
                TempData["ReplyError"] = "Pick a comment first.";
            }

            return RedirectToAction(nameof(Index));
        }

        /// <summary>Drafts one AI reply for a comment, without sending anything.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DraftReply([FromForm] string commentId, [FromForm] string prompt)
        {
            try
            {
                commentId = commentId?.Trim() ?? string.Empty;
                prompt = prompt?.Trim() ?? string.Empty;

                // Remember the selection and the prompt so the server-rendered composer can show them again.
                TempData["DraftCommentId"] = commentId;
                TempData["ReplyPrompt"] = prompt;

                if (string.IsNullOrWhiteSpace(commentId))
                {
                    TempData["ReplyError"] = "Pick a comment first."; return RedirectToAction(nameof(Index));
                }

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    TempData["ReplyError"] = "Write the AI reply prompt first."; return RedirectToAction(nameof(Index));
                }

                var comment = await _db.Comments
                    .FirstOrDefaultAsync(item => item.CommentId == commentId);

                if (comment is null)
                {
                    TempData["ReplyError"] = "Draft failed: comment not found."; return RedirectToAction(nameof(Index));
                }

                var reply = (await _aiService.AIGeneratedReplyAsync(comment.CommentText, prompt ?? string.Empty)).Trim();

                if (string.IsNullOrWhiteSpace(reply))
                {
                    TempData["ReplyError"] = "AI returned an empty reply."; return RedirectToAction(nameof(Index));
                }

                TempData["DraftedReply"] = reply;
                TempData["Status"] = "AI draft ready for comment " + commentId + ".";
                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ReplyError"] = ex.Message; return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>Posts a reply to YouTube and stores it on the Comment row.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendReply([FromForm] string commentId, [FromForm] string replyText, [FromForm] string? prompt)
        {
            try
            {
                replyText = replyText?.Trim() ?? string.Empty;
                commentId = commentId?.Trim() ?? string.Empty;

                // Remember the selection so the server-rendered composer can show it again after the redirect.
                TempData["DraftCommentId"] = commentId;

                if (string.IsNullOrWhiteSpace(commentId))
                {
                    TempData["ReplyError"] = "Pick a comment first."; return RedirectToAction(nameof(Index));
                }

                if (string.IsNullOrWhiteSpace(replyText))
                {
                    TempData["ReplyError"] = "Write a reply first."; return RedirectToAction(nameof(Index));
                }

                var comment = await _db.Comments
                    .FirstOrDefaultAsync(item => item.CommentId == commentId);

                if (comment is null)
                {
                    // Keep what the user typed so the composer shows it again.
                    TempData["DraftedReply"] = replyText;
                    TempData["ReplyError"] = "Send failed: comment not found."; return RedirectToAction(nameof(Index));
                }

                await _messengerService.SendMessageAsync(new MessengerDto
                {
                    CommentId = commentId,
                    Messege = replyText
                });

                comment.ReplyText = replyText;
                comment.RepliedAt = DateTime.UtcNow;
                comment.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                TempData["Status"] = "Reply sent and saved."; return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                // Keep what the user typed so the composer shows it again.
                TempData["DraftedReply"] = replyText;
                TempData["ReplyError"] = ex.Message +
                    " (posting comments requires an OAuth-authorized YouTube client, an API key alone cannot post)";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>
        /// Bulk auto-reply: for every comment without a reply yet, the AI drafts one with the
        /// prompt and it is posted to YouTube. Stops on the first failure (for example the
        /// OAuth restriction) and reports what was already sent.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AiReplyAll([FromForm] string prompt)
        {
            try
            {
                prompt = prompt?.Trim() ?? string.Empty;

                // Remember the prompt so the server-rendered form can show it again after the redirect.
                TempData["BulkReplyPrompt"] = prompt;

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    TempData["ReplyError"] = "Write the AI reply prompt first."; return RedirectToAction(nameof(Index));
                }

                var targets = await _db.Comments
                    .Where(comment => !comment.RemovedByAi && comment.RepliedAt == null)
                    .OrderByDescending(comment => comment.CreatedAt)
                    .ToListAsync();

                if (targets.Count == 0)
                {
                    TempData["ReplyError"] = "Every comment already has a reply (or the table is empty)."; return RedirectToAction(nameof(Index));
                }

                var sent = 0;
                var empty = 0;

                foreach (var comment in targets)
                {
                    var reply = (await _aiService.AIGeneratedReplyAsync(comment.CommentText, prompt)).Trim();

                    if (string.IsNullOrWhiteSpace(reply))
                    {
                        empty++;
                        continue;
                    }

                    await _messengerService.SendMessageAsync(new MessengerDto
                    {
                        CommentId = comment.CommentId,
                        Messege = reply
                    });

                    comment.ReplyText = reply;
                    comment.RepliedAt = DateTime.UtcNow;
                    comment.UpdatedAt = DateTime.UtcNow;

                    await _db.SaveChangesAsync();
                    sent++;
                }

                TempData["Status"] = "AI replies sent: " + sent.ToString() + " of " + targets.Count.ToString() + " (" + empty.ToString() + " empty)."; return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["ReplyError"] = ex.Message +
                    " (posting comments requires an OAuth-authorized YouTube client)";
                return RedirectToAction(nameof(Index));
            }
        }

        /// <summary>Hard-deletes one comment from traffichuntdb.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete([FromForm] string commentId)
        {
            try
            {
                var deleted = await _db.Comments
                    .Where(comment => comment.CommentId == commentId)
                    .ExecuteDeleteAsync();

                TempData["Status"] = deleted > 0 ? "Comment deleted." : "Nothing deleted."; return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Status"] = ex.Message; return RedirectToAction(nameof(Index));
            }
        }
    }
}
