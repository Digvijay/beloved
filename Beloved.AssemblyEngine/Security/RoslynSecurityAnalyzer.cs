using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Beloved.AssemblyEngine.Security
{
    public record SecurityDiagnostic(string RuleId, string Message, string FilePath, int LineNumber);

    /// <summary>
    /// Static AST security analysis engine for Beloved community modules.
    /// Inspects Roslyn syntax trees to enforce safety rules prior to module verification and compilation.
    /// Blocks unsafe memory manipulation, reflection emission, and unauthorized shell execution.
    /// </summary>
    public static class RoslynSecurityAnalyzer
    {
        public static List<SecurityDiagnostic> AnalyzeCode(string codeContent, string filePath = "Source.cs")
        {
            var diagnostics = new List<SecurityDiagnostic>();
            if (string.IsNullOrWhiteSpace(codeContent)) return diagnostics;

            var options = new CSharpParseOptions(LanguageVersion.Latest);
            var tree = CSharpSyntaxTree.ParseText(codeContent, options);
            var root = tree.GetRoot();

            // Rule 1: Check for unsafe keywords or statements
            var hasUnsafeKeyword = root.DescendantTokens().Any(t => t.IsKind(SyntaxKind.UnsafeKeyword)) || codeContent.Contains("unsafe");
            if (hasUnsafeKeyword)
            {
                diagnostics.Add(new SecurityDiagnostic(
                    "SEC001",
                    "Unsafe memory block execution ('unsafe') is strictly prohibited in community modules.",
                    filePath,
                    1
                ));
            }

            // Rule 2: Check for Process.Start calls
            var invocationNodes = root.DescendantNodes().OfType<InvocationExpressionSyntax>();
            foreach (var inv in invocationNodes)
            {
                var text = inv.ToString();
                if (text.Contains("Process.Start") || text.Contains("System.Diagnostics.Process"))
                {
                    var lineSpan = tree.GetLineSpan(inv.Span);
                    diagnostics.Add(new SecurityDiagnostic(
                        "SEC002",
                        "External shell process spawning ('Process.Start') is strictly prohibited.",
                        filePath,
                        lineSpan.StartLinePosition.Line + 1
                    ));
                }

                if (text.Contains("AssemblyBuilder") || text.Contains("DefineDynamicAssembly"))
                {
                    var lineSpan = tree.GetLineSpan(inv.Span);
                    diagnostics.Add(new SecurityDiagnostic(
                        "SEC003",
                        "Dynamic assembly byte emission ('System.Reflection.Emit') is strictly prohibited.",
                        filePath,
                        lineSpan.StartLinePosition.Line + 1
                    ));
                }
            }

            return diagnostics;
        }
    }
}
