using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

public class InBoxCorpusStateTests
{
    private static string Corpus => Path.Combine(RepoTree.Root(), "corpus", "inbox-controls");

    [Fact]
    public void A_control_folder_cannot_be_both_pending_and_saved()
    {
        foreach (var pending in Directory.EnumerateDirectories(Path.Combine(Corpus, "pending")))
        {
            var saved = Path.Combine(Corpus, "saved", Path.GetFileName(pending));
            Directory.Exists(saved).Should().BeFalse($"{pending} and {saved} must not both exist; move the whole control folder");
        }
    }

    [Fact]
    public void A_pending_file_cannot_duplicate_its_saved_relative_path()
    {
        var pendingRoot = Path.Combine(Corpus, "pending");
        foreach (var pending in Directory.EnumerateFiles(pendingRoot, "*", SearchOption.AllDirectories))
        {
            var saved = Path.Combine(Corpus, "saved", Path.GetRelativePath(pendingRoot, pending));
            if (File.Exists(saved))
                File.ReadAllBytes(pending).SequenceEqual(File.ReadAllBytes(saved)).Should().BeFalse(
                    $"{pending} is byte-identical to {saved}; remove the pending merge leftover");
        }
    }

    [Fact]
    public void Readme_folder_rows_match_their_actual_state()
    {
        var rows = File.ReadAllLines(Path.Combine(Corpus, "README.md"))
            .Where(line => line.StartsWith("| `", StringComparison.Ordinal))
            .SelectMany(line => Regex.Matches(line.Split('|')[1], @"`((?:pending|saved)/[^`]+/)`"))
            .Select(match => match.Groups[1].Value).ToArray();

        foreach (var row in rows)
            Directory.Exists(Path.Combine(Corpus, row.TrimEnd('/'))).Should().BeTrue(
                $"README lists {row}, which must exist in that state");

        foreach (var state in new[] { "pending", "saved" })
        foreach (var folder in Directory.EnumerateDirectories(Path.Combine(Corpus, state)))
        {
            var relative = $"{state}/{Path.GetFileName(folder)}/";
            rows.Should().Contain(row => row.StartsWith(relative, StringComparison.Ordinal),
                $"README must list {relative} under its actual state");
        }
    }
}
