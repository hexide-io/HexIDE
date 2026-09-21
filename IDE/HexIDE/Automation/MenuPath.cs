using System.Linq;
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
    /// The header as displayed. Mirrors Avalonia's <c>AccessText.RemoveAccessKeyMarker</c>, which is internal:
    /// the first underscore not doubled and not last marks the access key and is dropped, wherever it falls,
    /// and every doubled underscore then becomes a literal one.
    /// </summary>
    public static string StripAccessKey(string header)
    {
        var marker = FindAccessKeyMarker(header);
        var text = marker >= 0 ? header.Remove(marker, 1) : header;
        return text.Replace("__", "_");
    }

    private static int FindAccessKeyMarker(string text)
    {
        for (var i = text.IndexOf('_'); i >= 0 && i + 1 < text.Length; i = text.IndexOf('_', i + 2))
        {
            if (text[i + 1] != '_')
                return i;
        }
        return -1;
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
                    : $"It holds: {string.Join(", ", candidates.Select(DisplayedHeader))}";
                return new Result(null, $"No item '{segment}' in {where}. {holds}");
            }

            found = match;
            items = match.Items;
        }

        return new Result(found, null);
    }

    private static string DisplayedHeader(MenuItem item) => StripAccessKey(item.Header?.ToString() ?? string.Empty);
}
