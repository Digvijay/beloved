using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

/// <summary>
/// LLM provider targeting the official OpenAI REST API (api.openai.com).
/// Uses JSON mode (response_format: json_object) for deterministic Blueprint output.
/// </summary>
public sealed class OpenAiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _modelName;

    private static readonly string SystemPromptTemplate =
        """
        You are the Beloved Assembly Engine Intent Mapper.
        Your job is to translate a user's natural language request into a strict JSON Blueprint.
        Available modules to choose from: [{0}]

        You MUST output ONLY valid JSON matching this schema exactly:
        {{
          "appName": "StringWithoutSpaces",
          "modules": ["Auth", "Items"],
          "database": "SQLite",
          "authStrategy": "None",
          "target": "WebAndApi"
        }}

        Rules:
        1. Do not include markdown code blocks or any text outside the JSON.
        2. Select only modules from the available list that are relevant to the user's request.
        3. If the user asks for authentication or login, include 'Auth'.
        4. If they ask for products, items, inventory, or a dashboard, include 'Items'.
        5. For 'database', choose from: 'SQLite' (default), 'PostgreSQL' (if they ask for postgres/postgresql), or 'SQLServer' (if they ask for sqlserver/mssql/sql server).
        6. For 'authStrategy', choose from: 'None' (default) or 'JWT' (if they ask for jwt/tokens/auth/login).
        7. For 'target', choose from: 'WebAndApi' (default for fullstack/web + api apps), 'ApiOnly' (if they ask for backend only, headless, api only, or no frontend), or 'Mobile' (if they ask for mobile app).
        """;

    private static readonly string RefinePromptTemplate =
        """
        You are the Beloved Assembly Engine Intent Mapper.
        Your job is to update an existing JSON Blueprint based on the user's refinement instructions.

        Available modules: [{0}]
        Current Blueprint: {1}

        You MUST output ONLY valid JSON matching this schema exactly:
        {{
          "appName": "StringWithoutSpaces",
          "modules": ["Auth", "Items"],
          "database": "SQLite",
          "authStrategy": "None",
          "target": "WebAndApi"
        }}

        Rules:
        1. Do not include markdown code blocks or any text outside the JSON.
        2. Adjust modules, database, authStrategy, or target according to the User Refinement Instruction.
        3. Keep other properties unmodified unless requested.
        4. For 'database', choose from: 'SQLite' (default), 'PostgreSQL' (if they ask for postgres/postgresql), or 'SQLServer' (if they ask for sqlserver/mssql/sql server).
        5. For 'authStrategy', choose from: 'None' (default) or 'JWT' (if they ask for jwt/tokens/auth/login).
        6. For 'target', choose from: 'WebAndApi' (default for fullstack/web + api apps), 'ApiOnly' (if they ask for backend only, headless, api only, or no frontend), or 'Mobile' (if they ask for mobile app).
        """;

    public OpenAiLlmProvider(HttpClient httpClient, string apiKey, string modelName = "gpt-4o-mini")
    {
        _httpClient = httpClient;
        _modelName = modelName;
        _httpClient.BaseAddress = new Uri("https://api.openai.com");
        _httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
    }

    public async Task<Blueprint?> MapIntentAsync(string userPrompt, IEnumerable<string> availableModules)
    {
        var modulesList = string.Join(", ", availableModules);
        var systemPrompt = string.Format(SystemPromptTemplate, modulesList);

        return await SendLlmRequestAsync(systemPrompt, userPrompt);
    }

    public async Task<Blueprint?> RefineBlueprintAsync(Blueprint currentBlueprint, string refinePrompt, IEnumerable<string> availableModules)
    {
        var modulesList = string.Join(", ", availableModules);
        var currentJson = JsonSerializer.Serialize(currentBlueprint, AssemblyJsonContext.Default.Blueprint);
        var systemPrompt = string.Format(RefinePromptTemplate, modulesList, currentJson);

        return await SendLlmRequestAsync(systemPrompt, refinePrompt);
    }

    private async Task<Blueprint?> SendLlmRequestAsync(string systemPrompt, string userPrompt)
    {
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

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8,
            "application/json");

        var response = await _httpClient.PostAsync("/v1/chat/completions", jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"OpenAI API call failed: {response.StatusCode} — {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);
        var aiContent = doc.RootElement
            .GetProperty("choices")[0]
            .GetProperty("message")
            .GetProperty("content")
            .GetString();

        if (string.IsNullOrWhiteSpace(aiContent)) return null;

        return JsonSerializer.Deserialize(aiContent, AssemblyJsonContext.Default.Blueprint);
    }
}
