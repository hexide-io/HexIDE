using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Recounts the language coverage document from its Markdown rows. This guards internal consistency,
/// not runtime fidelity: changing a rating still requires the evidence described in the document.
/// </summary>
public class MissingLanguageDocTests
{
    private sealed record Table(string Heading, string[] Columns, List<string[]> Rows);
    private static string Clean(string text)
    {
        text = text.Trim();
        if (text.StartsWith("**", StringComparison.Ordinal) && text.EndsWith("**", StringComparison.Ordinal))
            text = text[2..^2];
        return text.Trim('`');
    }

    private static List<Table> Tables(string text)
    {
        var tables = new List<Table>();
        var heading = "";
        Table? table = null;
        // Normalize before splitting: the guard also runs in Windows CRLF checkouts.
        foreach (var line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            if (line.StartsWith('#')) heading = line.TrimStart('#', ' ');
            if (!line.StartsWith('|')) { table = null; continue; }
            var cells = Regex.Split(line.Trim('|'), @"(?<!\\)\|").Select(Clean).ToArray();
            if (table is null)
            {
                table = new Table(heading, cells, []);
                tables.Add(table);
            }
            else if (!cells.All(c => Regex.IsMatch(c, @"^:?-+:?$"))) table.Rows.Add(cells);
        }
        return tables;
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void CountsRatingsAndStatusSectionsAgreeWithTheInventory(string lineEnding)
    {
        var text = File.ReadAllText(Path.Combine(RepoTree.Root(), "docs", "MISSING_LANGUAGE.md")).ReplaceLineEndings(lineEnding);
        var tables = Tables(text);
        var legend = tables.Single(t => t.Columns[0] == "Status").Rows.Select(r => r[0]).ToArray();
        legend.Should().HaveCount(6, "How to read this defines the six ratings");
        var summary = tables.Single(t => t.Columns[0] == "Category");
        var families = tables.Single(t => t.Columns[0] == "Family (VB6 enum)");
        var rated = tables.Where(t => t.Columns.Length > 1 && t.Columns[1] == "Status").ToArray();
        rated.Should().NotBeEmpty("the inventory tables must be parsed");
        var inventory = rated.Where(t => summary.Rows.Any(r => t.Heading.StartsWith(r[0] + " —", StringComparison.Ordinal))
                                     || t.Heading.StartsWith("Constants that do not resolve", StringComparison.Ordinal)).ToArray();
        inventory.Select(t => t.Heading).Distinct().Should().HaveCount(8, "all inventory categories must be read");
        var errors = new List<string>();
        foreach (var table in rated)
        {
            foreach (var row in table.Rows)
                if (!legend.Contains(row[1])) errors.Add($"{table.Heading}: {row[0]} has unknown status '{row[1]}'");
            foreach (var duplicate in table.Rows.GroupBy(r => r[0], StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
                errors.Add($"{table.Heading}: duplicate name '{duplicate.Key}'");
        }

        foreach (var group in rated.GroupBy(t => t.Heading))
        {
            var match = Regex.Match(group.Key, @"— (\d+)(?: names)?");
            if (!match.Success) continue;
            var count = group.Sum(t => t.Rows.Count);
            Check(group.Key, int.Parse(match.Groups[1].Value), count);
            // The parenthetical breakdown uses 'absent' for Dies + Won't load.
            foreach (Match part in Regex.Matches(group.Key, @"(\d+) (silently wrong|absent|partial|no-op|supported)"))
            {
                var label = part.Groups[2].Value;
                var actual = group.SelectMany(t => t.Rows).Count(r => label == "absent"
                    ? r[1] is "Dies" or "Won't load" : r[1].Equals(label, StringComparison.OrdinalIgnoreCase));
                Check($"{group.Key}: {label}", int.Parse(part.Groups[1].Value), actual);
            }
        }

        var totals = new int[legend.Length];
        foreach (var category in summary.Rows.Where(r => r[0] != "Total"))
        {
            var counts = legend.Select(status => inventory.Where(t => t.Heading.StartsWith(category[0] + " —", StringComparison.Ordinal))
                .SelectMany(t => t.Rows).Count(r => r[1] == status)).ToArray();
            if (category[0] == "Intrinsic constants")
            {
                counts = legend.Select(status => families.Rows.Sum(r => status switch
                {
                    "Supported" => int.Parse(r[1]), "Partial" => int.Parse(r[2]),
                    "Dies" => int.Parse(r[3]), _ => 0
                })).ToArray();
            }
            for (var i = 0; i < legend.Length; i++)
            {
                Check($"At a glance: {category[0]} / {legend[i]}", int.Parse(category[i + 1]), counts[i]);
                totals[i] += counts[i];
            }
            Check($"At a glance: {category[0]} / Total", int.Parse(category[^1]), counts.Sum());
        }
        summary.Rows.Count(r => r[0] == "Total").Should().Be(1, "At a glance needs exactly one Total row");
        var total = summary.Rows.Single(r => r[0] == "Total");
        for (var i = 0; i < legend.Length; i++) Check($"At a glance: Total / {legend[i]}", int.Parse(total[i + 1]), totals[i]);
        Check("At a glance: Total / Total", int.Parse(total[^1]), totals.Sum());
        var intro = Regex.Matches(text.ReplaceLineEndings("\n").Split("\n\n")[1], @"\*\*(\d+) names\*\*");
        intro.Should().ContainSingle("the opening blockquote declares the inventory size in bold");
        Check("Intro names (At a glance Total)", int.Parse(intro[0].Groups[1].Value), int.Parse(total[^1]));

        var wrongProse = Regex.Match(text, @"The (\w+) Silently wrong rows below");
        wrongProse.Success.Should().BeTrue("the prose declares the Silently wrong count");
        var numberWords = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        var wrongCount = wrongProse.Groups[1].Value;
        Check("Silently wrong prose", int.TryParse(wrongCount, out var numeric) ? numeric : Array.IndexOf(numberWords, wrongCount), totals[0]);

        // These prose summaries repeat category totals and absent counts outside the tables/headings.
        foreach (var (category, pattern) in new[]
        {
            ("Operators", @"\*\*Operators are nearly complete\*\* — (\d+) absent of (\d+)"),
            ("Statements", @"(\d+)% of VB6's statements \((\d+)\s+of (\d+)"),
            ("In-box objects", @"(\d+)% of the\s+in-box surface \(.*?; (\d+) of (\d+)\)")
        })
        {
            var prose = Regex.Match(text, pattern);
            prose.Success.Should().BeTrue($"the damage summary declares {category} counts");
            var rows = inventory.Where(t => t.Heading.StartsWith(category + " —", StringComparison.Ordinal)).SelectMany(t => t.Rows).ToArray();
            var absent = rows.Count(r => r[1] is "Dies" or "Won't load");
            Check($"Damage summary: {category} / absent", int.Parse(prose.Groups[^2].Value), absent);
            Check($"Damage summary: {category} / Total", int.Parse(prose.Groups[^1].Value), rows.Length);
            if (prose.Groups.Count == 4)
                Check($"Damage summary: {category} / percent", int.Parse(prose.Groups[1].Value), (int)Math.Round(100.0 * absent / rows.Length));
        }
        var functionGroups = Regex.Matches(text, @"(?m)^\*\*([^*\r\n]+)\*\* \((\d+)\)\r?$");
        functionGroups.Should().NotBeEmpty("intrinsic function groups declare their sizes in prose");
        for (var i = 0; i < functionGroups.Count; i++)
        {
            var group = functionGroups[i];
            var end = i + 1 < functionGroups.Count ? functionGroups[i + 1].Index : text.IndexOf("### Keywords and modifiers", group.Index, StringComparison.Ordinal);
            var rows = Tables(text[group.Index..end]).SelectMany(t => t.Rows).Count();
            Check($"Intrinsic function group {group.Groups[1].Value}", int.Parse(group.Groups[2].Value), rows);
        }
        var familyHeading = Regex.Match(families.Heading, @"— (\d+) names in (\d+) families");
        familyHeading.Success.Should().BeTrue("the constants heading declares names and families");
        Check("Intrinsic constants names", int.Parse(familyHeading.Groups[1].Value), families.Rows.Sum(r => int.Parse(r[4])));
        Check("Intrinsic constants families", int.Parse(familyHeading.Groups[2].Value), families.Rows.Count);
        foreach (var family in families.Rows)
            Check($"Constant family {family[0]}", int.Parse(family[4]), family.Skip(1).Take(3).Sum(int.Parse));
        Check("Absent constants", families.Rows.Sum(r => int.Parse(r[3])), inventory.Where(t => t.Heading.StartsWith("Constants that", StringComparison.Ordinal)).SelectMany(t => t.Rows).Count(r => r[1] == "Dies"));

        foreach (var status in legend.Take(3))
        {
            var section = rated.Where(t => t.Heading.StartsWith((status == "Dies" ? "Dies mid-run" : status) + " —", StringComparison.Ordinal)).ToArray();
            section.Should().ContainSingle($"{status} needs its status section");
            var expected = inventory.SelectMany(t => t.Rows).Where(r => r[1] == status).Select(r => r[0]).Order().ToArray();
            var actual = section.SelectMany(t => t.Rows).Where(r => r[1] == status).Select(r => r[0]).Order().ToArray();
            foreach (var name in expected.Except(actual)) errors.Add($"{status}: inventory name '{name}' missing from status section");
            foreach (var name in actual.Except(expected)) errors.Add($"{status}: status section name '{name}' missing from inventory");
            foreach (var row in section.SelectMany(t => t.Rows).Where(r => r[1] != status))
                errors.Add($"{status}: '{row[0]}' has status '{row[1]}' in the wrong section");
        }
        errors.Should().BeEmpty("docs/MISSING_LANGUAGE.md must be recounted from its rows: " + string.Join("; ", errors));
        void Check(string label, int documented, int actual)
        {
            if (documented != actual) errors.Add($"{label}: says {documented}, rows give {actual}");
        }
    }
}
