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
            var url = _configuration["LLM:Url"]!;
            var model = _configuration["LLM:Model"]!;

            var request = new
            {
                model = model,
                prompt = prompt,
                stream = false
            };

            var response = await _httpClient.PostAsJsonAsync(
                $"{url}/api/generate",
                request);

            response.EnsureSuccessStatusCode();

            var result = await response.Content
                .ReadFromJsonAsync<OllamaResponseDto>();

            return result?.Response ?? string.Empty;
        }
    }
}
