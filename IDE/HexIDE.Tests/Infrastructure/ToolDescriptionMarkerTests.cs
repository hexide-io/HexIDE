using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace HexIDE.Tests.Infrastructure;

[ToolDescription]
public class ToolDescriptionMarkerTests
{
    [Fact]
    public void Every_class_reading_tool_descriptions_has_the_shared_marker()
    {
        var folder = Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Tests");
        var files = Directory.EnumerateFiles(folder, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(folder, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "bin" or "obj"));
        files.SelectMany(file => UnmarkedReaders(File.ReadAllText(file))
                .Select(name => $"{Path.GetRelativePath(folder, file)}: {name}"))
            .Should().BeEmpty("classes reading tool descriptions must use [ToolDescription] so the trait filter includes them");
    }

    // Check syntax, not text: examples and comments must not count as readers. AllText combines the tool's
    // description with its parameters'. Requiring ToolSource in the class excludes unrelated model descriptions.
    private static IEnumerable<string> UnmarkedReaders(string source)
    {
        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        return root.DescendantNodes().OfType<ClassDeclarationSyntax>()
            .Where(c => c.DescendantNodes().OfType<IdentifierNameSyntax>().Any(n => n.Identifier.ValueText == "ToolSource"))
            .Where(c => c.DescendantNodes().OfType<MemberAccessExpressionSyntax>()
                .Any(m => m.Name.Identifier.ValueText is "Description" or "AllText"))
            .Where(c => !c.AttributeLists.SelectMany(a => a.Attributes)
                .Any(a => a.Name.ToString().Split('.').Last() is "ToolDescription" or "ToolDescriptionAttribute"))
            .Select(c => c.Identifier.ValueText);
    }

    [Theory]
    [InlineData("class Pin { object T = ToolSource.Tools.First().Description; }", true)]
    [InlineData("class Pin { object T = ToolSource.Tools.First().Parameters.First().Description; }", true)]
    [InlineData("class Pin { object T = HexIDE.Tests.Infrastructure.ToolSource.Tools.First().AllText; }", true)]
    [InlineData("[ToolDescription] class Pin { object T = ToolSource.Tools.First().Description; }", false)]
    [InlineData("[HexIDE.Tests.Infrastructure.ToolDescriptionAttribute] class Pin { object T = ToolSource.Tools.First().Description; }", false)]
    [InlineData("class Pin { object T = model.Description; }", false)]
    [InlineData("class Pin { /* ToolSource.Tools.First().Description */ string T = \"ToolSource.Tools.First().AllText\"; }", false)]
    public void Guard_recognizes_description_readers(string source, bool unmarked)
        => UnmarkedReaders(source).Should().HaveCount(unmarked ? 1 : 0);
}
