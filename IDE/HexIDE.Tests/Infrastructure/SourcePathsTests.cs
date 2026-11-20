using HexIDE.Debugging;

namespace HexIDE.Tests.Infrastructure;

public class SourcePathsTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Linked_parents_match_but_different_files_do_not(bool useUris)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-paths-" + Guid.NewGuid());
        var real = Path.Combine(directory, "real");
        var link = Path.Combine(directory, "link");
        Directory.CreateDirectory(Path.Combine(real, "nested"));
        try
        {
            Directory.CreateSymbolicLink(link, real);
            var uri = new Uri(Path.Combine(real, "nested", "Module.bas"));
            var linked = Path.Combine(link, "nested", "Module.bas");
            var other = Path.Combine(link, "nested", "Other.bas");
            if (useUris)
            {
                SourcePaths.SameSourcePath(uri, new Uri(linked)).Should().BeTrue();
                SourcePaths.SameSourcePath(uri, new Uri(other)).Should().BeFalse();
            }
            else
            {
                SourcePaths.SameSourcePath(uri.AbsoluteUri, linked).Should().BeTrue();
                SourcePaths.SameSourcePath(uri.AbsoluteUri, other).Should().BeFalse();
            }
        }
        finally
        {
            if (Directory.Exists(link)) Directory.Delete(link);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Non_file_uris_match_only_exactly()
    {
        SourcePaths.SameSourcePath(new Uri("untitled:Module1"), new Uri("untitled:Module1")).Should().BeTrue();
        SourcePaths.SameSourcePath(new Uri("untitled:Module1"), new Uri("untitled:module1")).Should().Be(OperatingSystem.IsWindows());
        SourcePaths.SameSourcePath(new Uri("untitled:Module1"), new Uri("untitled:Module2")).Should().BeFalse();
        SourcePaths.SameSourcePath(new Uri("archive-entry:One"), new Uri("archive-entry:Two")).Should().BeFalse();
        SourcePaths.SameSourcePath(new Uri("untitled:Module%31"), new Uri("untitled:Module1")).Should().BeFalse();
    }

    [Fact]
    public void Missing_files_keep_the_exact_match()
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString(), "Module.bas");
        var uri = new Uri(path);
        SourcePaths.SameSourcePath(uri.AbsoluteUri, path).Should().BeTrue();
        SourcePaths.SameSourcePath(uri, new Uri(path)).Should().BeTrue();
        SourcePaths.SameSourcePath(uri.AbsoluteUri, path + ".other").Should().BeFalse();
        SourcePaths.SameSourcePath(uri, new Uri(path + ".other")).Should().BeFalse();
        SourcePaths.SameSourcePath(uri.AbsoluteUri, null).Should().BeFalse();
    }
}
