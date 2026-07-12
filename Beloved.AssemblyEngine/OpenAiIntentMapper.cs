using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

public class OpenAiIntentMapper : IIntentMapper
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
    private readonly string _modelName;

    public OpenAiIntentMapper(HttpClient httpClient, string apiKey, string modelName = "gpt-4o")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _modelName = modelName;
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _apiKey);
    }

    public async Task<Blueprint?> MapIntentAsync(string userPrompt, IEnumerable<string> availableModules)
    {
        var modulesList = string.Join(", ", availableModules);
        var systemPrompt = $@"You are the Beloved Assembly Engine Intent Mapper. 
Your job is to translate a user's natural language request into a strict JSON Blueprint.
Available modules to choose from: [{modulesList}]

You MUST output ONLY valid JSON matching this schema exactly:
{{
  ""appName"": ""StringWithoutSpaces"",
  ""modules"": [""Auth"", ""Items""]
}}

Rules:
1. Do not include markdown code blocks.
2. Select only modules from the available list that are relevant to the user's request.
3. If the user asks for authentication or login, include 'Auth'.
4. If they ask for products, items, inventory, or a dashboard, include 'Items'.";

        var requestBody = new
        {
            model = _modelName,
            response_format = new { type = "json_object" },
            messages = new[]
            {
                new { role = "system", content = systemPrompt },
                new { role = "user", content = userPrompt }
            },
            temperature = 0.0
        };

        var jsonOptions = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };
        var jsonContent = JsonSerializer.Serialize(requestBody, jsonOptions);
        var content = new StringContent(jsonContent, Encoding.UTF8, "application/json");

        var response = await _httpClient.PostAsync("/v1/chat/completions", content);
        
        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync();
            throw new Exception($"OpenAI API call failed: {response.StatusCode} - {errorBody}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var responseDoc = JsonDocument.Parse(responseString);
        var aiContent = responseDoc.RootElement.GetProperty("choices")[0].GetProperty("message").GetProperty("content").GetString();

        if (string.IsNullOrWhiteSpace(aiContent)) return null;

        return JsonSerializer.Deserialize(aiContent, AssemblyJsonContext.Default.Blueprint);
    }
}
