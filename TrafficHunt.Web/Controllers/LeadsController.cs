using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;
using TrafficHunt.Domain;

namespace TrafficHunt.Web.Controllers
{
    public class LeadsController : Controller
    {
        private readonly ApplicationDbContext _db;
        private readonly IMessengerService _messengerService;
        private readonly IAIService _aiService;

        public LeadsController(
            ApplicationDbContext db,
            IMessengerService messengerService,
            IAIService aiService)
        {
            _db = db;
            _messengerService = messengerService;
            _aiService = aiService;
        }

        /// <summary>Shows every lead, newest first.</summary>
        public async Task<IActionResult> Index()
        {
            var leads = await _db.Leads
                .OrderByDescending(lead => lead.CreatedAt)
                .ToListAsync();

            return View(leads);
        }

        /// <summary>Sends one reply for a lead and marks it replied.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SendReply(int leadId, string text)
        {
            try
            {
                text = text?.Trim() ?? string.Empty;

                if (string.IsNullOrWhiteSpace(text))
                {
                    return Json(new { ok = false, error = "Write a reply first." });
                }

                var lead = await _db.Leads.FirstOrDefaultAsync(item => item.Id == leadId);

                if (lead is null)
                {
                    return Json(new { ok = false, error = "Lead not found." });
                }

                await _messengerService.SendMessageAsync(new MessengerDto
                {
                    CommentId = lead.CommentId,
                    Messege = text
                });

                lead.YourReply = text;
                lead.Replied = true;

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

        /// <summary>Drafts one AI reply for a lead, without sending anything.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DraftReply(int leadId, string prompt)
        {
            try
            {
                var lead = await _db.Leads.FirstOrDefaultAsync(item => item.Id == leadId);

                if (lead is null)
                {
                    return Json(new { ok = false, error = "Lead not found." });
                }

                var reply = (await _aiService.AIGeneratedReplyAsync(lead.CommentText, prompt ?? string.Empty)).Trim();

                return string.IsNullOrWhiteSpace(reply)
                    ? Json(new { ok = false, error = "The AI returned an empty reply. Check the LLM settings." })
                    : Json(new { ok = true, reply });
            }
            catch (Exception ex)
            {
                return Json(new { ok = false, error = ex.Message });
            }
        }

        /// <summary>
        /// Bulk reply to every pending lead. Mode "ai" drafts one AI reply per lead with the
        /// prompt, "single" sends the same (spintax-supported) message everywhere.
        /// </summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkSend(string mode, string prompt, string message)
        {
            try
            {
                var dto = new MessengerDto();

                if (string.Equals(mode, "ai", StringComparison.OrdinalIgnoreCase))
                {
                    dto.Messege = prompt?.Trim() ?? string.Empty;
                    dto.AiGenerated = true;

                    if (string.IsNullOrWhiteSpace(dto.Messege))
                    {
                        return Json(new { ok = false, error = "Describe the reply the AI should write." });
                    }
                }
                else
                {
                    dto.Messege = message?.Trim() ?? string.Empty;

                    if (string.IsNullOrWhiteSpace(dto.Messege))
                    {
                        return Json(new { ok = false, error = "Enter a message to send." });
                    }
                }

                await _messengerService.SendAllMessagesAsync(dto);

                var remaining = await _db.Leads.CountAsync(lead => !lead.Replied);

                return Json(new { ok = true, remaining });
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

        /// <summary>Deletes one lead.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int leadId)
        {
            try
            {
                var deleted = await _db.Leads
                    .Where(lead => lead.Id == leadId)
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
