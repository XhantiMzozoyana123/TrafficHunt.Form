using Microsoft.AspNetCore.Mvc;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace TrafficHunt.Web.Controllers
{
    public class SettingsController : Controller
    {
        private readonly IConfiguration _configuration;
        private readonly string _settingsPath;

        public SettingsController(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _settingsPath = Path.Combine(environment.ContentRootPath, "appsettings.json");
        }

        public IActionResult Index()
        {
            return View();
        }

        /// <summary>Saves the edited values back into appsettings.json.</summary>
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Save(
            string connectionString,
            string apiKey,
            string applicationName,
            string llmUrl,
            string llmModel,
            string defaultPrompt,
            string defaultReplyPrompt,
            string defaultMaxResults)
        {
            try
            {
                var root = System.IO.File.Exists(_settingsPath)
                    ? JsonNode.Parse(await System.IO.File.ReadAllTextAsync(_settingsPath)) as JsonObject ?? new JsonObject()
                    : new JsonObject();

                SetValue(root, "ConnectionStrings", "DefaultConnection", connectionString);
                SetValue(root, "YouTube", "ApiKey", apiKey);
                SetValue(root, "YouTube", "ApplicationName", applicationName);
                SetValue(root, "LLM", "Url", llmUrl);
                SetValue(root, "LLM", "Model", llmModel);
                SetValue(root, "Hunt", "DefaultPrompt", defaultPrompt);
                SetValue(root, "Hunt", "DefaultReplyPrompt", defaultReplyPrompt);
                SetValue(root, "Hunt", "DefaultMaxResults", defaultMaxResults);

                await System.IO.File.WriteAllTextAsync(
                    _settingsPath,
                    root.ToJsonString(new JsonSerializerOptions { WriteIndented = true }));

                TempData["Saved"] = true;

                return RedirectToAction(nameof(Index));
            }
            catch (Exception ex)
            {
                TempData["Error"] = ex.Message;
                return RedirectToAction(nameof(Index));
            }
        }

        private static void SetValue(JsonObject root, string section, string key, string? value)
        {
            if (root[section] is not JsonObject sectionObject)
            {
                sectionObject = new JsonObject();
                root[section] = sectionObject;
            }

            sectionObject[key] = value ?? string.Empty;
        }
    }
}

