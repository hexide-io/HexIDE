using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VbTextCompare against every pair vb6.exe was measured on (#1567): the whole Text order of the characters Chr(0) to
/// Chr(255) and 64 longer strings, a held-out set of 569 strings the rows were not built from, and the pairs that tell how
/// a word-sort character's position is counted. The comparer holds no culture data, so every host must give the same
/// answers: these run unchanged on Windows, Linux and macOS.
/// </summary>
public class TextCompareTests
{
    [Fact]
    public void Every_pair_of_the_measured_Text_order_compares_as_vb6_exe_compared_it() =>
        Mismatches(TextCompareMeasurements.FullOrder).Should().BeEmpty();

    [Fact]
    public void Every_pair_of_the_held_out_strings_compares_as_vb6_exe_compared_it() =>
        Mismatches(TextCompareMeasurements.HeldOut).Should().BeEmpty();

    [Fact]
    public void A_word_sort_characters_position_counts_the_letters_before_it()
    {
        var wrong = TextCompareMeasurements.PositionPairs
            .Where(p => VbTextCompare.Compare(p.A, p.B) != p.Sign)
            .Select(p => $"{Show(p.A)} vs {Show(p.B)}: vb6.exe {p.Sign}, here {VbTextCompare.Compare(p.A, p.B)}");
        wrong.Should().BeEmpty();
    }

    // A Collection's keys use the comparer's hash, so strings it finds equal must hash alike: "straße" and "STRASSE" are
    // one key only if they land in one bucket.
    [Fact]
    public void Strings_that_compare_equal_hash_alike()
    {
        var wrong = new List<string>();
        foreach (var group in TextCompareMeasurements.FullOrder.Concat(TextCompareMeasurements.HeldOut))
            foreach (var s in group)
                if (VbTextCompare.Comparer.GetHashCode(s) != VbTextCompare.Comparer.GetHashCode(group[0])
                    && s.Length > 0 && group[0].Length > 0)
                    wrong.Add($"{Show(s)} and {Show(group[0])}");
        wrong.Should().BeEmpty();
    }

    [Fact]
    public void The_comparer_is_the_compare()
    {
        VbTextCompare.Comparer.Equals("straße", "STRASSE").Should().BeTrue();
        VbTextCompare.Comparer.Equals("e", "é").Should().BeFalse();
        VbTextCompare.Comparer.Compare("a", "B").Should().BeNegative();
        VbTextCompare.Comparer.Equals("", "\0").Should().BeFalse();
    }

    // Every pair across the groups: a string in an earlier group is below one in a later group, and two in one group are
    // equal. The first few that are not, shown by code point.
    private static List<string> Mismatches(string[][] groups)
    {
        var flat = groups.SelectMany((g, i) => g.Select(s => (Group: i, Text: s))).ToList();
        var wrong = new List<string>();
        for (var i = 0; i < flat.Count; i++)
            for (var j = i + 1; j < flat.Count; j++)
            {
                var want = flat[i].Group.CompareTo(flat[j].Group);
                var got = VbTextCompare.Compare(flat[i].Text, flat[j].Text);
                if (got != want && wrong.Count < 20)
                    wrong.Add($"{Show(flat[i].Text)} vs {Show(flat[j].Text)}: vb6.exe {want}, here {got}");
            }
        return wrong;
    }

    private static string Show(string s) =>
        s.Length == 0 ? "\"\"" : string.Join(".", s.Select(c => ((int)c).ToString("X4")));
}
