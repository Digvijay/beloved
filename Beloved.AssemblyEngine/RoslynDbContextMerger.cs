using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using System.Linq;

namespace Beloved.AssemblyEngine;

public static class RoslynDbContextMerger
{
    public static string MergeDbSets(string originalCode, string[] dbSetDeclarations)
    {
        var tree = CSharpSyntaxTree.ParseText(originalCode);
        var root = tree.GetRoot();

        // Locate AppDbContext class
        var classDeclaration = root.DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .FirstOrDefault(c => c.Identifier.Text == "AppDbContext");

        if (classDeclaration == null) return originalCode;

        // Parse new dbset members to inject
        var newMembers = dbSetDeclarations.Select(decl =>
        {
            var parsed = SyntaxFactory.ParseMemberDeclaration(decl);
            if (parsed == null) return null;
            return parsed.WithLeadingTrivia(SyntaxFactory.CarriageReturnLineFeed, SyntaxFactory.Whitespace("    "));
        }).Where(m => m != null).Cast<MemberDeclarationSyntax>().ToArray();

        if (newMembers.Length == 0) return originalCode;

        var updatedClass = classDeclaration.AddMembers(newMembers);
        var newRoot = root.ReplaceNode(classDeclaration, updatedClass);

        return newRoot.ToFullString();
    }
}
