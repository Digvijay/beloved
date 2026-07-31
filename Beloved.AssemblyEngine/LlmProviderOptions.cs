namespace Beloved.AssemblyEngine;

/// <summary>
/// Strongly-typed configuration for the pluggable LLM backend.
/// Bind from appsettings.json section "Llm".
/// </summary>
public sealed class LlmProviderOptions
{
    public const string SectionName = "Llm";

    /// <summary>
    /// Which provider to use: "Ollama" | "OpenAI" | "Gemini" | "Claude"
    /// </summary>
    public string Provider { get; set; } = "Ollama";

    /// <summary>
    /// API key for cloud providers. Not used for Ollama (local).
    /// </summary>
    public string ApiKey { get; set; } = string.Empty;

    /// <summary>
    /// The model name to use. Defaults are set per-provider.
    /// Examples: "gpt-4o-mini", "gemini-2.0-flash", "claude-3-haiku-20240307", "qwen2.5:32b"
    /// </summary>
    public string Model { get; set; } = string.Empty;

    /// <summary>
    /// Override the base URL. Used for Ollama (default: http://localhost:11434)
    /// or any OpenAI-compatible endpoint.
    /// </summary>
    public string? BaseUrl { get; set; }
}
