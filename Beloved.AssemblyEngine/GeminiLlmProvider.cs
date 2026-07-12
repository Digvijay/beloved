using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

/// <summary>
/// LLM provider targeting the Google Gemini REST API.
/// Endpoint: generativelanguage.googleapis.com/v1beta/models/{model}:generateContent
/// Uses responseMimeType "application/json" for deterministic Blueprint output.
/// </summary>
public sealed class GeminiLlmProvider : ILlmProvider
{
    private readonly HttpClient _httpClient;
    private readonly string _apiKey;
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

    public GeminiLlmProvider(HttpClient httpClient, string apiKey, string modelName = "gemini-2.0-flash")
    {
        _httpClient = httpClient;
        _apiKey = apiKey;
        _modelName = modelName;
        _httpClient.BaseAddress = new Uri("https://generativelanguage.googleapis.com");
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
            system_instruction = new
            {
                parts = new[] { new { text = systemPrompt } }
            },
            contents = new[]
            {
                new
                {
                    role = "user",
                    parts = new[] { new { text = userPrompt } }
                }
            },
            generationConfig = new
            {
                responseMimeType = "application/json",
                temperature = 0.0
            }
        };

        var jsonContent = new StringContent(
            JsonSerializer.Serialize(requestBody, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
            Encoding.UTF8,
            "application/json");

        var url = $"/v1beta/models/{_modelName}:generateContent?key={_apiKey}";
        var response = await _httpClient.PostAsync(url, jsonContent);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Gemini API call failed: {response.StatusCode} — {error}");
        }

        var responseString = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(responseString);

        var aiContent = doc.RootElement
            .GetProperty("candidates")[0]
            .GetProperty("content")
            .GetProperty("parts")[0]
            .GetProperty("text")
            .GetString();

        if (string.IsNullOrWhiteSpace(aiContent)) return null;

        return JsonSerializer.Deserialize(aiContent, AssemblyJsonContext.Default.Blueprint);
    }
}
