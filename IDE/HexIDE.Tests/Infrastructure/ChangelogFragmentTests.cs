using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Every changelog.d/ fragment must be one scripts/changelog-gather.sh can fold into CHANGELOG.md (#1375).
/// </summary>
/// <remarks>
/// The script checks the same rules before it gathers, but it runs once a release; a malformed fragment found
/// then is found by whoever is cutting the release, long after its author has moved on. These run on every
/// build, so it is found by the pull request that adds it.
/// </remarks>
public class ChangelogFragmentTests
{
    private static readonly Regex FragmentName = new(@"^(\d+(-[a-z0-9-]+)?|[a-z0-9-]+)\.md$", RegexOptions.Compiled);
    private static readonly Regex Section = new(@"^### (Added|Changed|Deprecated|Removed|Fixed|Security)$", RegexOptions.Compiled);
    private static readonly Regex AnyHeading = new(@"^#{1,3} ", RegexOptions.Compiled);

    private static string Folder() => Path.Combine(RepoTree.Root(), "changelog.d");

    private static IEnumerable<string> Fragments() =>
        Directory.EnumerateFiles(Folder(), "*.md").Where(f => Path.GetFileName(f) != "README.md");

    [Fact]
    public void The_folder_exists_and_explains_itself()
    {
        // A guard over a folder that has moved checks nothing and passes.
        File.Exists(Path.Combine(Folder(), "README.md")).Should().BeTrue("changelog.d/README.md says how to write a fragment");
    }

    [Fact]
    public void Every_fragment_is_named_for_its_issue()
    {
        foreach (var fragment in Fragments())
            FragmentName.IsMatch(Path.GetFileName(fragment)).Should().BeTrue(
                $"{Path.GetFileName(fragment)} must be named <issue>.md, <issue>-<word>.md or <word>.md, in lower case");
    }

    [Fact]
    public void Every_fragment_starts_with_a_section_and_uses_only_the_changelogs_sections()
    {
        foreach (var fragment in Fragments())
        {
            var name = Path.GetFileName(fragment);
            var lines = File.ReadAllLines(fragment);
            var first = lines.FirstOrDefault(l => !string.IsNullOrWhiteSpace(l));
            first.Should().NotBeNull($"{name} is empty");
            Section.IsMatch(first!).Should().BeTrue(
                $"{name} must start with ### Added, Changed, Deprecated, Removed, Fixed or Security, not '{first}'");
            foreach (var heading in lines.Where(l => AnyHeading.IsMatch(l)))
                Section.IsMatch(heading).Should().BeTrue($"'{heading}' in {name} is not one of the changelog's sections");
            lines.Should().Contain(l => l.StartsWith("- ", StringComparison.Ordinal), $"{name} holds no entry");
        }
    }
}
