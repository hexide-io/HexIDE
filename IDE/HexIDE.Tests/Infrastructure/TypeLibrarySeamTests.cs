using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// The type-library reader belongs to the IDE (#4059, step 1 of the run side of #3411's split). The Object Browser,
/// the References dialog and the workspace names read it, and the interpreter never did, so it stays out of
/// <c>HexIDE.Runtime</c>, which leaves with the optional provider.
/// </summary>
public class TypeLibrarySeamTests
{
    private static readonly Regex TypeLibraryNamespace =
        new(@"^\s*namespace\s+HexIDE\.Runtime\.TypeLibrary\b", RegexOptions.Multiline);

    [Fact]
    public void The_runtime_declares_no_type_library_namespace()
    {
        var runtime = Path.Combine(RepoTree.Root(), "IDE", "HexIDE.Runtime");
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";

        var files = Directory.EnumerateFiles(runtime, "*.cs", SearchOption.AllDirectories)
            .Where(file => !file.Contains(bin, StringComparison.Ordinal) && !file.Contains(obj, StringComparison.Ordinal))
            .ToList();

        files.Should().NotBeEmpty("the search must reach the runtime's source, or it passes by finding nothing");
        files.Where(file => TypeLibraryNamespace.IsMatch(File.ReadAllText(file)))
            .Select(file => Path.GetRelativePath(runtime, file))
            .Should().BeEmpty("the type-library reader is the IDE's, in HexIDE.TypeLibrary");
    }
}
