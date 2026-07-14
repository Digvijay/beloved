using System;
using System.Collections.Generic;
using System.Linq;

namespace Beloved.AssemblyEngine;

public class DependencyResolver
{
    public static List<string> Resolve(IEnumerable<string> modules, Dictionary<string, List<string>> dependencyMap)
    {
        var visited = new HashSet<string>();
        var stack = new HashSet<string>();
        var result = new List<string>();

        void Visit(string node)
        {
            if (stack.Contains(node))
                throw new InvalidOperationException("Circular dependency detected inside modules: " + node);

            if (!visited.Contains(node))
            {
                stack.Add(node);
                if (dependencyMap.TryGetValue(node, out var deps))
                {
                    foreach (var dep in deps)
                    {
                        Visit(dep);
                    }
                }
                stack.Remove(node);
                visited.Add(node);
                result.Add(node);
            }
        }

        foreach (var module in modules)
        {
            Visit(module);
        }

        return result;
    }
}
