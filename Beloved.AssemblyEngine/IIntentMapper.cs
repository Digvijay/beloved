using System.Collections.Generic;
using System.Threading.Tasks;

namespace Beloved.AssemblyEngine;

public interface IIntentMapper
{
    Task<Blueprint?> MapIntentAsync(string userPrompt, IEnumerable<string> availableModules);
}
