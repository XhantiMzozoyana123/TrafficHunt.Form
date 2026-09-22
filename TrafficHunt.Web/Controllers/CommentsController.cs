using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;
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

            return View(comments);
        }

        /// <summary>
        /// Searches YouTube for videos with the keyword and fetches their comments into
        /// traffichuntdb (upsert on the YouTube comment id, so refetching updates instead
        /// of duplicating and keeps any reply already posted).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Fetch(string keyword, int maxVideos, int maxComments, string order)
        {
            try
            {
                keyword = keyword?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(keyword))
                {
                    return Json(new { ok = false, error = "Type a keyword first." });
                }

                var videoSearch = new SearchDto
                {
                    Keyword = keyword,
                    MaxResult = Math.Clamp(maxVideos, 1, 25)
                };

                var videos = await _youTubeService.SearchVideosAsync(videoSearch);

                if (videos.Count == 0)
                {
                    return Json(new { ok = false, error = $"No videos found for \"{keyword}\"." });
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

                return Json(new { ok = true, added, updated, fetched, skipped });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Sends the whole comment list to the AI as one JSON payload and keeps only the
        /// matches: kept rows are flagged KeptByAi, the rest RemovedByAi (kept for history).
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Qualify(string prompt)
        {
            try
            {
                prompt = prompt?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return Json(new { ok = false, error = "Write the AI prompt first." });
                }

                var comments = await _db.Comments
                    .Where(comment => !comment.RemovedByAi)
                    .ToListAsync();

                if (comments.Count == 0)
                {
                    return Json(new { ok = false, error = "Fetch comments first." });
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

                return Json(new { ok = true, kept = keptIds.Count, removed = comments.Count - keptIds.Count });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }

        /// <summary>Drafts one AI reply for a comment, without sending anything.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DraftReply(string commentId, string prompt)
        {
            try
            {
                var comment = await _db.Comments
                    .FirstOrDefaultAsync(item => item.CommentId == commentId);

                if (comment is null)
                {
                    return Json(new { ok = false, error = "Comment not found." });
                }

                var reply = (await _aiService.AIGeneratedReplyAsync(comment.CommentText, prompt ?? string.Empty)).Trim();

                return string.IsNullOrWhiteSpace(reply)
                    ? Json(new { ok = false, error = "The AI returned an empty reply. Check the LLM settings." })
                    : Json(new { ok = true, reply });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }

        /// <summary>Posts a reply to YouTube and stores it on the Comment row.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendReply(string commentId, string text)
        {
            try
            {
                text = text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Json(new { ok = false, error = "Write a reply first." });
                }

                var comment = await _db.Comments
                    .FirstOrDefaultAsync(item => item.CommentId == commentId);

                if (comment is null)
                {
                    return Json(new { ok = false, error = "Comment not found." });
                }

                await _messengerService.SendMessageAsync(new MessengerDto
                {
                    CommentId = commentId,
                    Messege = text
                });

                comment.ReplyText = text;
                comment.RepliedAt = DateTime.UtcNow;
                comment.UpdatedAt = DateTime.UtcNow;

                await _db.SaveChangesAsync();

                return Json(new { ok = true });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    error = ex.Message +
                        " (posting comments requires an OAuth-authorized YouTube client, an API key alone cannot post)"
                });
            }
        }

        /// <summary>
        /// Bulk auto-reply: for every comment without a reply yet, the AI drafts one with the
        /// prompt and it is posted to YouTube. Stops on the first failure (for example the
        /// OAuth restriction) and reports what was already sent.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AiReplyAll(string prompt)
        {
            try
            {
                prompt = prompt?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(prompt))
                {
                    return Json(new { ok = false, error = "Write the AI reply prompt first." });
                }

                var targets = await _db.Comments
                    .Where(comment => !comment.RemovedByAi && comment.RepliedAt == null)
                    .OrderByDescending(comment => comment.CreatedAt)
                    .ToListAsync();

                if (targets.Count == 0)
                {
                    return Json(new { ok = false, error = "Every comment already has a reply (or the table is empty)." });
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

                return Json(new { ok = true, sent, empty, total = targets.Count });
            }
            catch (Exception ex)
            {
                return Json(new
                {
                    ok = false,
                    error = ex.Message +
                        " (posting comments requires an OAuth-authorized YouTube client)"
                });
            }
        }

        /// <summary>Hard-deletes one comment from traffichuntdb.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(string commentId)
        {
            try
            {
                var deleted = await _db.Comments
                    .Where(comment => comment.CommentId == commentId)
                    .ExecuteDeleteAsync();

                return Json(new { ok = deleted > 0 });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }
    }
}
