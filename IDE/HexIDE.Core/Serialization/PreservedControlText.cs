using System.Text.RegularExpressions;
using HexIDE.Language;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// What the designer needs to know of a control HexIDE does not model, read from the text it is carried as
/// (<c>ComponentInstance.PreservedChildSubtrees</c>). In Core (#5633), since the designer and the VB6 form
/// reader both read it, and it reads only the text.
/// </summary>
public static class PreservedControlText
{
    private static readonly Regex TabIndexLine = new(@"^TabIndex\s*=\s*(\d+)", RegexOptions.IgnoreCase);

    /// <summary>
    /// The TabIndex of each control in a preserved block, its own and those nested in it. Such a control keeps its
    /// place in the tab order, so a new control must not take its number (#925). A line inside a
    /// <c>BeginProperty</c> block is a property's, not a control's. Lines end as <see cref="SourceLines"/> ends them,
    /// so a block read from a file whose lines end with a lone CR is read line by line too.
    /// </summary>
    public static IEnumerable<int> TabIndexes(string text)
    {
        foreach (var number in TabIndexNumbers(text))
            yield return int.Parse(text.AsSpan(number.Start, number.Length), System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// <paramref name="text"/> with the TabIndex of its <paramref name="ordinal"/>th control, in the order
    /// <see cref="TabIndexes"/> reads them, set to <paramref name="value"/>, and nothing else changed.
    /// </summary>
    public static string WithTabIndex(string text, int ordinal, int value)
    {
        var number = TabIndexNumbers(text).ElementAt(ordinal);
        return text.Remove(number.Start, number.Length).Insert(number.Start,
            value.ToString(System.Globalization.CultureInfo.InvariantCulture));
    }

    private static IEnumerable<(int Start, int Length)> TabIndexNumbers(string text)
    {
        var propertyDepth = 0;
        for (var start = 0; start < text.Length;)
        {
            var (end, next) = SourceLines.LineAt(text, start);
            var raw = text[start..end];
            var line = raw.Trim();
            if (line.StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase))
                propertyDepth++;
            else if (line.Equals("EndProperty", StringComparison.OrdinalIgnoreCase))
                propertyDepth--;
            else if (propertyDepth == 0 && TabIndexLine.Match(line) is { Success: true } match
                     && int.TryParse(match.Groups[1].Value, out _))
                yield return (start + raw.Length - raw.TrimStart().Length + match.Groups[1].Index,
                    match.Groups[1].Length);
            start = next;
        }
    }
}
