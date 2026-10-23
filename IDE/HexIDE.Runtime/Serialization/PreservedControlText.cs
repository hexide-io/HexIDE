using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// What the designer needs to know of a control HexIDE does not model, read from the text it is carried as
/// (<c>ComponentInstance.PreservedChildSubtrees</c>).
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
        var propertyDepth = 0;
        foreach (var raw in SourceLines.Split(text))
        {
            var line = raw.Trim();
            if (line.StartsWith("BeginProperty", StringComparison.OrdinalIgnoreCase))
                propertyDepth++;
            else if (line.Equals("EndProperty", StringComparison.OrdinalIgnoreCase))
                propertyDepth--;
            else if (propertyDepth == 0 && TabIndexLine.Match(line) is { Success: true } match
                     && int.TryParse(match.Groups[1].Value, out var tabIndex))
                yield return tabIndex;
        }
    }
}
