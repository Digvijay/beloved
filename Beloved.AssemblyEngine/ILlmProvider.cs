using System.Collections.Generic;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

/// <summary>
/// Abstraction over any LLM backend for mapping natural language intent to a Blueprint.
/// Implementations: Ollama, OpenAI, Gemini, Claude.
/// </summary>
public interface ILlmProvider
{
    Task<Blueprint?> MapIntentAsync(string userPrompt, IEnumerable<string> availableModules);
    Task<Blueprint?> RefineBlueprintAsync(Blueprint currentBlueprint, string refinePrompt, IEnumerable<string> availableModules);
    
    /// <summary>
    /// Executes a structured, deterministic AI-assisted merge of module context into a source file.
    /// </summary>
    Task<string> StitchFileAsync(string originalContent, string mergeInstruction, string contextHint = "");
}
