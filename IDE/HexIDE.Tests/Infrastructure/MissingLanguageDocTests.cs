using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Recounts the language coverage document from its Markdown rows. This guards internal consistency,
/// not runtime fidelity: changing a rating still requires the evidence described in the document.
/// </summary>
public class MissingLanguageDocTests
{
    [Theory]
    [InlineData("vbMsgBoxRight")]
    [InlineData("vbMsgBoxRtlReading")]
    public void MsgBoxDocumentsMeasuredLayoutFlags(string flag)
    {
        var text = File.ReadAllText(Path.Combine(RepoTree.Root(), "docs", "MISSING_LANGUAGE.md"));
        var row = Tables(text).SelectMany(t => t.Rows).Single(r => r[0] == "MsgBox");

        row[2].Should().Contain(flag).And.Contain("#3958").And.Contain("mirrors").And.NotContain("accepted, no effect");
    }

    private sealed record Table(string Heading, int HeadingIndex, string[] Columns, List<string[]> Rows);
    private static string Clean(string text)
    {
        text = text.Trim();
        if (text.StartsWith("**", StringComparison.Ordinal) && text.EndsWith("**", StringComparison.Ordinal))
            text = text[2..^2];
        return text.Trim('`');
    }

    private static List<Table> Tables(string text, Dictionary<string[], Group[]>? locations = null)
    {
        var tables = new List<Table>();
        var heading = "";
        var headingIndex = 0;
        Table? table = null;
        var errors = new List<string>();
        var lineNumber = 0;
        var headerLine = 0;
        var expectsDelimiter = false;
        // Keep original offsets so fixes preserve formatting and line endings.
        var cellPattern = new Regex(@"(?<!\\)\|((?:\\.|[^|\r\n])*)");
        foreach (Match lineMatch in Regex.Matches(text, @"(?m)^[^\r\n]*"))
        {
            lineNumber++;
            var line = lineMatch.Value;
            if (line.StartsWith('#'))
            {
                heading = line.TrimStart('#', ' ');
                headingIndex = lineMatch.Index + line.Length - heading.Length;
            }
            if (!line.StartsWith('|'))
            {
                if (expectsDelimiter) errors.Add($"{table!.Heading}: line {headerLine + 1} must be a delimiter row immediately after the header");
                expectsDelimiter = false;
                table = null;
                continue;
            }
            var cellGroups = new List<Group>();
            var cellMatch = cellPattern.Match(text, lineMatch.Index, line.Length - 1);
            while (cellMatch.Success)
            {
                cellGroups.Add(cellMatch.Groups[1]);
                cellMatch = cellMatch.NextMatch();
                if (cellMatch.Index >= lineMatch.Index + line.Length - 1) break;
            }
            var cells = cellGroups.Select(g => Clean(g.Value)).ToArray();
            var isDelimiter = cells.Length > 0 && cellGroups.All(g => Regex.IsMatch(g.Value.Trim(), @"^:?-+:?$"));
            if (table is null)
            {
                if (isDelimiter)
                {
                    errors.Add($"{heading}: line {lineNumber} has a delimiter row without a header");
                    continue;
                }
                table = new Table(heading, headingIndex, cells, []);
                tables.Add(table);
                headerLine = lineNumber;
                expectsDelimiter = true;
            }
            else
            {
                if (cells.Length != table.Columns.Length)
                    errors.Add($"{table.Heading}: line {lineNumber} has {cells.Length} cells; header has {table.Columns.Length}");
                if (expectsDelimiter && !isDelimiter)
                    errors.Add($"{table.Heading}: line {lineNumber} must be a delimiter row immediately after the header");
                else if (!expectsDelimiter && isDelimiter)
                    errors.Add($"{table.Heading}: line {lineNumber} has a delimiter row outside the position immediately after the header");
                expectsDelimiter = false;
                if (!isDelimiter)
                {
                    table.Rows.Add(cells);
                    locations?.Add(cells, cellGroups.ToArray());
                }
            }
        }
        if (expectsDelimiter) errors.Add($"{table!.Heading}: line {headerLine + 1} must be a delimiter row immediately after the header");
        errors.Should().BeEmpty("docs/MISSING_LANGUAGE.md tables must have valid structure: " + string.Join("; ", errors));
        return tables;
    }

    [Theory]
    [InlineData("| Name | Status |\n| A | Dies |\n|---|---|", 3)]
    [InlineData("| Name | Status |\n| A | Dies |", 3)]
    [InlineData("| Name | Status |\n|---|---|\n| A | Dies | extra |", 4)]
    [InlineData("| Name | Status |\n|---|---|\n|---|---|", 4)]
    [InlineData("| Name | Status |\n|---|---|\n| A |", 4)]
    [InlineData("| Name | Status |\nProse", 3)]
    [InlineData("| Name | Status |", 3)]
    [InlineData("|---|---|", 2)]
    public void TableStructureRejectsMalformedRows(string table, int line)
    {
        // The first three shapes passed the old guard (#2902); duplicate prose is deliberately allowed.
        foreach (var lineEnding in new[] { "\n", "\r\n" })
        {
            var check = () => CheckTableStructure(("## Sample\n" + table).ReplaceLineEndings(lineEnding));
            check.Should().Throw<Exception>().WithMessage($"*Sample*line {line}*");
        }
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void TableStructureAcceptsEscapedPipesAndSeparateTables(string lineEnding)
    {
        var text = """
            ## Sample
            | Name | Status |
            |:---|---:|
            | A \| B | Dies |

            Repeated prose.
            Repeated prose.

            | Other |
            |---|
            | C |
            """;
        CheckTableStructure(text.ReplaceLineEndings(lineEnding));
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void DocumentTablesHaveValidStructure(string lineEnding)
    {
        var text = File.ReadAllText(Path.Combine(RepoTree.Root(), "docs", "MISSING_LANGUAGE.md"));
        CheckTableStructure(text.ReplaceLineEndings(lineEnding));
    }

    private static void CheckTableStructure(string text)
    {
        Tables(text);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void CountsRatingsAndStatusSectionsAgreeWithTheInventory(string lineEnding)
    {
        var path = Path.Combine(RepoTree.Root(), "docs", "MISSING_LANGUAGE.md");
        var text = File.ReadAllText(path);
        if (Environment.GetEnvironmentVariable("HEXIDE_MISSING_LANGUAGE_FIX") == "1")
        {
            var fixedText = Recount(text, true, Console.WriteLine);
            if (fixedText != text) File.WriteAllText(path, fixedText);
            text = fixedText;
        }
        Recount(text.ReplaceLineEndings(lineEnding), false);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void FixRecountsWrongFiguresAndTheGuardAcceptsTheResult(string lineEnding)
    {
        var original = Recount(File.ReadAllText(Path.Combine(RepoTree.Root(), "docs", "MISSING_LANGUAGE.md")), true).ReplaceLineEndings(lineEnding);
        var locations = new Dictionary<string[], Group[]>();
        var tables = Tables(original, locations);
        var summary = tables.Single(t => t.Columns[0] == "Category");
        var families = tables.Single(t => t.Columns[0] == "Family (VB6 enum)");
        var figures = summary.Rows.SelectMany(r => locations[r].Skip(1))
            .Concat(families.Rows.Select(r => locations[r][^1]));
        var wrong = original;
        foreach (var figure in figures.OrderByDescending(g => g.Index))
        {
            var number = Regex.Match(figure.Value, @"\d+");
            wrong = wrong.Remove(figure.Index + number.Index, number.Length).Insert(figure.Index + number.Index, "999");
        }
        wrong = Regex.Replace(wrong, @"(?m)^(#{2,3} [^\r\n]*?— )\d+", "${1}999");
        wrong = Regex.Replace(wrong, @"\d+ (silently wrong|absent|partial|no-op|supported|families)", "999 $1");
        wrong = Regex.Replace(wrong, @"\*\*\d+ names\*\*", "**999 names**");
        wrong = Regex.Replace(wrong, @"The \w+( Silently wrong rows? below)", "The nineteen$1");
        wrong = Regex.Replace(wrong, @"(?m)^(\*\*[^*\r\n]+\*\* \()\d+(\)\r?$)", "${1}999$2");
        wrong = Regex.Replace(wrong, @"(Operators are nearly complete\*\* — )\d+ absent of \d+", "${1}999 absent of 999");
        wrong = Regex.Replace(wrong, @"\d+% of VB6's statements \(\d+(\s+)of \d+", "999% of VB6's statements (999${1}of 999");
        wrong = Regex.Replace(wrong, @"\d+% of the(\s+)in-box surface \((.*?); \d+ of \d+\)", "999% of the${1}in-box surface ($2; 999 of 999)");
        var check = () => Recount(wrong, false);
        check.Should().Throw<Exception>();
        var fixedText = Recount(wrong, true);
        Recount(fixedText, false).Should().Be(fixedText);
        fixedText.Should().Be(original);
    }

    [Theory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void FixLeavesCorrectDocumentByteIdentical(string lineEnding)
    {
        var text = Recount(File.ReadAllText(Path.Combine(RepoTree.Root(), "docs", "MISSING_LANGUAGE.md")), true).ReplaceLineEndings(lineEnding);
        System.Text.Encoding.UTF8.GetBytes(Recount(text, true)).Should().Equal(System.Text.Encoding.UTF8.GetBytes(text));
    }

    private static string Recount(string text, bool fix, Action<string>? report = null)
    {
        var locations = new Dictionary<string[], Group[]>();
        var tables = Tables(text, locations);
        var edits = new Dictionary<int, (int Length, string Value)>();
        var legend = tables.Single(t => t.Columns[0] == "Status").Rows.Select(r => r[0]).ToArray();
        legend.Should().HaveCount(6, "How to read this defines the six ratings");
        var summary = tables.Single(t => t.Columns[0] == "Category");
        var families = tables.Single(t => t.Columns[0] == "Family (VB6 enum)");
        var rated = tables.Where(t => t.Columns.Length > 1 && t.Columns[1] == "Status").ToArray();
        rated.Should().NotBeEmpty("the inventory tables must be parsed");
        var inventory = rated.Where(t => summary.Rows.Any(r => t.Heading.StartsWith(r[0] + " —", StringComparison.Ordinal))
                                     || t.Heading.StartsWith("Constants by name", StringComparison.Ordinal)).ToArray();
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
            Check(group.Key, match.Groups[1], count, group.First().HeadingIndex);
            // The parenthetical breakdown uses 'absent' for Dies + Won't load.
            foreach (Match part in Regex.Matches(group.Key, @"(\d+) (silently wrong|absent|partial|no-op|supported)"))
            {
                var label = part.Groups[2].Value;
                var actual = group.SelectMany(t => t.Rows).Count(r => label == "absent"
                    ? r[1] is "Dies" or "Won't load" : r[1].Equals(label, StringComparison.OrdinalIgnoreCase));
                Check($"{group.Key}: {label}", part.Groups[1], actual, group.First().HeadingIndex);
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
                Check($"At a glance: {category[0]} / {legend[i]}", locations[category][i + 1], counts[i]);
                totals[i] += counts[i];
            }
            Check($"At a glance: {category[0]} / Total", locations[category][^1], counts.Sum());
        }
        summary.Rows.Count(r => r[0] == "Total").Should().Be(1, "At a glance needs exactly one Total row");
        var total = summary.Rows.Single(r => r[0] == "Total");
        for (var i = 0; i < legend.Length; i++) Check($"At a glance: Total / {legend[i]}", locations[total][i + 1], totals[i]);
        Check("At a glance: Total / Total", locations[total][^1], totals.Sum());
        var intro = Regex.Matches(text, @"\*\*(\d+) names\*\*");
        intro.Should().ContainSingle("the opening blockquote declares the inventory size in bold");
        Check("Intro names (At a glance Total)", intro[0].Groups[1], totals.Sum());

        var wrongProse = Regex.Match(text, @"The (\w+) Silently wrong rows? below");
        wrongProse.Success.Should().BeTrue("the prose declares the Silently wrong count");
        var numberWords = new[] { "zero", "one", "two", "three", "four", "five", "six", "seven", "eight", "nine",
            "ten", "eleven", "twelve", "thirteen", "fourteen", "fifteen", "sixteen", "seventeen", "eighteen", "nineteen" };
        Check("Silently wrong prose", wrongProse.Groups[1], totals[0], words: numberWords);

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
            Check($"Damage summary: {category} / absent", prose.Groups[^2], absent);
            Check($"Damage summary: {category} / Total", prose.Groups[^1], rows.Length);
            if (prose.Groups.Count == 4)
                Check($"Damage summary: {category} / percent", prose.Groups[1], (int)Math.Round(100.0 * absent / rows.Length));
        }
        var functionGroups = Regex.Matches(text, @"(?m)^\*\*([^*\r\n]+)\*\* \((\d+)\)\r?$");
        functionGroups.Should().NotBeEmpty("intrinsic function groups declare their sizes in prose");
        for (var i = 0; i < functionGroups.Count; i++)
        {
            var group = functionGroups[i];
            var end = i + 1 < functionGroups.Count ? functionGroups[i + 1].Index : text.IndexOf("### Keywords and modifiers", group.Index, StringComparison.Ordinal);
            var rows = Tables(text[group.Index..end]).SelectMany(t => t.Rows).Count();
            Check($"Intrinsic function group {group.Groups[1].Value}", group.Groups[2], rows);
        }
        var familyHeading = Regex.Match(families.Heading, @"— (\d+) names in (\d+) families");
        familyHeading.Success.Should().BeTrue("the constants heading declares names and families");
        Check("Intrinsic constants names", familyHeading.Groups[1], families.Rows.Sum(r => r.Skip(1).Take(3).Sum(int.Parse)), families.HeadingIndex);
        Check("Intrinsic constants families", familyHeading.Groups[2], families.Rows.Count, families.HeadingIndex);
        foreach (var family in families.Rows)
            Check($"Constant family {family[0]}", locations[family][4], family.Skip(1).Take(3).Sum(int.Parse));
        var absentConstants = inventory.Where(t => t.Heading.StartsWith("Constants by name", StringComparison.Ordinal)).SelectMany(t => t.Rows).Count(r => r[1] == "Dies");
        if (families.Rows.Sum(r => int.Parse(r[3])) != absentConstants)
            errors.Add("Absent constants: family Dies counts disagree with the named inventory");

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
        foreach (var (index, edit) in edits.OrderByDescending(e => e.Key))
            text = text.Remove(index, edit.Length).Insert(index, edit.Value);
        return text;

        void Check(string label, Group value, int actual, int offset = 0, string[]? words = null)
        {
            var token = Clean(value.Value);
            var documented = int.TryParse(token, out var numeric) ? numeric : Array.IndexOf(words!, token);
            if (documented == actual) return;
            var message = $"{label}: says {documented}, rows give {actual}";
            if (!fix) { errors.Add(message); return; }
            var replacement = words is not null && actual < words.Length ? words[actual] : actual.ToString(System.Globalization.CultureInfo.InvariantCulture);
            edits.Add(offset + value.Index + value.Value.IndexOf(token, StringComparison.Ordinal), (token.Length, replacement));
            report?.Invoke(message);
        }
    }
}
