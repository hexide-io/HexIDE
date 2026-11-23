namespace HexIDE.Tests.Infrastructure;

public class ResolvedTempTests
{
    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact]
    public void CreateDirectory_creates_distinct_directories_under_the_resolved_root()
    {
        const string prefix = "hexide-resolved-temp-";
        var first = ResolvedTemp.CreateDirectory(prefix);
        try
        {
            var second = ResolvedTemp.CreateDirectory(prefix);
            try
            {
                first.Should().NotBe(second);
                foreach (var directory in new[] { first, second })
                {
                    Directory.Exists(directory).Should().BeTrue();
                    Path.GetFileName(directory).Should().StartWith(prefix);
                    Path.GetDirectoryName(directory).Should().Be(Path.TrimEndingDirectorySeparator(ResolvedTemp.Root));
                }
            }
            finally { Directory.Delete(second); }
        }
        finally { Directory.Delete(first); }
    }

    [Fact(Skip = "Windows preserves the supplied path spelling.", SkipUnless = nameof(OnWindows))]
    public void Resolve_returns_the_original_path_on_Windows()
    {
        var path = Path.GetTempPath();
        ResolvedTemp.Resolve(path).Should().Be(path);
    }

    [Fact(Skip = "Windows symbolic links may require privileges.", SkipWhen = nameof(OnWindows))]
    public void Resolve_follows_links_inside_a_link_target()
    {
        var root = ResolvedTemp.CreateDirectory("hexide-resolved-temp-");
        try
        {
            var target = Directory.CreateDirectory(Path.Combine(root, "target", "child")).FullName;
            var ancestor = Path.Combine(root, "ancestor");
            Directory.CreateSymbolicLink(ancestor, "target");
            var link = Path.Combine(root, "link");
            Directory.CreateSymbolicLink(link, Path.Combine("ancestor", "child"));

            ResolvedTemp.Resolve(link).Should().Be(target);
        }
        finally { Directory.Delete(root, recursive: true); }
    }
}
