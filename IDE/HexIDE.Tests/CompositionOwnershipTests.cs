using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace HexIDE.Tests;

public class CompositionOwnershipTests
{
    [Fact]
    public void CompositionsAreCreatedOnlyByThePerTestOwner()
    {
        var root = SourceDirectory();
        var unowned = Directory.EnumerateFiles(root, "*.cs", SearchOption.AllDirectories)
            .Where(path => !Path.GetRelativePath(root, path).Split(Path.DirectorySeparatorChar)
                .Any(part => part is "obj" or "bin"))
            .Where(path => Path.GetFileName(path) != "CompositionTestBase.cs")
            .Where(path => Regex.IsMatch(File.ReadAllText(path), @"new\s+DISetup\s*\("))
            .Select(path => Path.GetRelativePath(root, path));

        unowned.Should().BeEmpty("composition tests must use CompositionTestBase so their language servers are disposed (#2588)");
    }

    private static string SourceDirectory([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;
}
