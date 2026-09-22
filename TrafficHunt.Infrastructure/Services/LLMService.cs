using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http.Json;
using System.Text;
using TrafficHunt.Application.Dtos;
using TrafficHunt.Application.Interfaces;

namespace TrafficHunt.Infrastructure.Services
{
    public class LLMService : ILLMService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _configuration;

        public LLMService(
            HttpClient httpClient,
            IConfiguration configuration)
        {
            _httpClient = httpClient;
            _configuration = configuration;
        }

        public async Task<string> GenerateTextAsync(string prompt)
        {
            var url = _configuration["LLM:Url"];

            if (string.IsNullOrWhiteSpace(url))
            {
                throw new InvalidOperationException(
                    "LLM:Url is not configured. Set the Ollama URL on the Settings page (for example http://46.202.170.203:11434/api/generate).");
            }

            var model = _configuration["LLM:Model"];

            if (string.IsNullOrWhiteSpace(model))
            {
                throw new InvalidOperationException(
                    "LLM:Model is not configured. Set the Ollama model on the Settings page (for example llama3:latest).");
            }

            var request = new
            {
                model = model,
                prompt = prompt,
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(
                BuildGenerateEndpoint(url),
                request);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<OllamaResponseDto>();

            return result?.Response ?? string.Empty;
        }

        /// <summary>
        /// Builds the Ollama text-generation endpoint. The configured URL may either be the
        /// server root (http://46.202.170.203:11434) or the full endpoint
        /// (http://46.202.170.203:11434/api/generate); trailing slashes are ignored.
        /// </summary>
        private static string BuildGenerateEndpoint(string url)
        {
            var baseUrl = url.Trim().TrimEnd('/');

            return baseUrl.EndsWith("/api/generate", StringComparison.OrdinalIgnoreCase)
                ? baseUrl
                : $"{baseUrl}/api/generate";
        }
    }
}
