using System.Linq;
using System.Text;
using Avalonia.Controls;

namespace HexIDE.Automation;

/// <summary>
/// Resolves a slash-separated menu path (<c>Project/Add Module</c>) to a <see cref="MenuItem"/>, the way a
/// user reads the menu: by its displayed text, not by the header string with its access-key markers.
/// </summary>
/// <remarks>
/// The walk goes through logical <see cref="ItemsControl.Items"/>, which a closed menu already holds. Only
/// the <em>visual</em> children wait for the popup to open, so nothing here needs a menu expanded first.
/// </remarks>
public static class MenuPath
{
    /// <summary>The item a path named, or why it could not be found. Exactly one is non-null.</summary>
    public readonly record struct Result(MenuItem? Item, string? Error);

    /// <summary>
    /// The header as displayed: a single underscore marks the access key and is dropped, a doubled one is a
    /// literal underscore. Matches Avalonia's access-text rule, wherever in the header the key falls.
    /// </summary>
    public static string StripAccessKey(string header)
    {
        var text = new StringBuilder(header.Length);
        for (var i = 0; i < header.Length; i++)
        {
            if (header[i] != '_')
                text.Append(header[i]);
            else if (i + 1 < header.Length && header[i + 1] == '_')
                text.Append(header[++i]);
        }
        return text.ToString();
    }

    public static Result Resolve(IEnumerable<object?> menuBarItems, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return new Result(null, "Path is empty");

        var items = menuBarItems;
        MenuItem? found = null;
        foreach (var segment in segments)
        {
            var wanted = StripAccessKey(segment);
            var candidates = items.OfType<MenuItem>().ToList();
            var match = candidates.FirstOrDefault(mi =>
                string.Equals(DisplayedHeader(mi), wanted, StringComparison.OrdinalIgnoreCase));

            if (match is null)
            {
                var where = found is null ? "the menu bar" : $"menu '{DisplayedHeader(found)}'";
                var holds = candidates.Count == 0
                    ? "It has no items."
                    : $"It holds: {string.Join(", ", candidates.Select(DisplayedHeader))}.";
                return new Result(null, $"No item '{segment}' in {where}. {holds}");
            }

            found = match;
            items = match.Items;
        }

        return new Result(found, null);
    }

    private static string DisplayedHeader(MenuItem item) => StripAccessKey(item.Header?.ToString() ?? string.Empty);
}
