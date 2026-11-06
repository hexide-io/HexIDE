using System;
using System.IO;

namespace HexIDE.Integration.Tests;

public class RepoTreeTests
{
    [Fact]
    public void Root_ignores_a_nearer_corpus_and_requires_both_project_trees()
    {
        var root = Path.Join(Path.GetTempPath(), "hexide-root-" + Guid.NewGuid());
        var decoy = Path.Join(root, "IDE", "Tests");
        var start = Path.Join(decoy, "bin", "Debug");
        Directory.CreateDirectory(Path.Join(root, "LspServer"));
        Directory.CreateDirectory(Path.Join(decoy, "corpus", "inbox-controls", "look"));
        Directory.CreateDirectory(Path.Join(decoy, "IDE"));
        Directory.CreateDirectory(start);
        try
        {
            RepoTree.Root(start).Should().Be(root);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}
