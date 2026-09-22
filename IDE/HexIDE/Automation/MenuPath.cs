using System.Linq;
using System.Windows.Input;
using Avalonia.Controls;

namespace HexIDE.Automation;

/// <summary>
/// Resolves a slash-separated menu path (<c>Project/Add Module</c>) to a menu entry, the way a user reads the
/// menu: by its displayed text, not by the header string with its access-key markers.
/// </summary>
/// <remarks>
/// The walk goes through logical <see cref="ItemsControl.Items"/>, which a closed menu already holds. Only
/// the <em>visual</em> children wait for the popup to open, so nothing here needs a menu expanded first.
/// </remarks>
public static class MenuPath
{
    /// <summary>
    /// The entry a path named, or why it could not be found. <see cref="Error"/> is null exactly when it was
    /// found. <see cref="Item"/> is the <see cref="MenuItem"/> when there is one; an entry of an
    /// <c>ItemsSource</c>-backed submenu that has not been opened is a view model with no container yet, and
    /// then only <see cref="Header"/>, <see cref="Command"/> and <see cref="CommandParameter"/> are set.
    /// </summary>
    public readonly record struct Result(
        MenuItem? Item, string? Error, string? Header = null, ICommand? Command = null, object? CommandParameter = null);

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

    /// <summary>One entry of a menu, whatever it is made of.</summary>
    private readonly record struct Entry(
        string Header, MenuItem? Item, ICommand? Command, object? Parameter, IEnumerable<object?> Children, bool Visible);

    public static Result Resolve(IEnumerable<object?> menuBarItems, string path)
    {
        var segments = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length == 0)
            return new Result(null, "Path is empty");

        IEnumerable<object?> items = menuBarItems;
        MenuItem? parent = null;
        Entry? found = null;
        foreach (var segment in segments)
        {
            var entries = items.Select(i => EntryOf(i, parent)).OfType<Entry>().ToList();

            // The segment as typed is what the menu displays, and a displayed underscore is literal: stripping
            // the segment as well lost it, so "Remove My_App" could not reach the item showing exactly that
            // (#544). The stripped form is tried second, for a caller who typed the raw header.
            var match = entries.FirstOrDefault(e => Named(e, segment)) is { Header: not null } exact
                ? exact
                : entries.FirstOrDefault(e => Named(e, StripAccessKey(segment)));
            var where = found is null ? "the menu bar" : $"menu '{found.Value.Header}'";

            if (match.Header is null)
            {
                var visible = entries.Where(e => e.Visible).Select(e => e.Header).ToList();
                var holds = visible.Count == 0 ? "It has no items." : $"It holds: {string.Join(", ", visible)}";
                return new Result(null, $"No item '{segment}' in {where}. {holds}");
            }

            // A hidden item is not in the menu the user sees, so it is neither listed nor run: invoking the
            // desktop-hidden "Go to github repo" opened a browser from an entry nothing displays (#544).
            if (!match.Visible)
                return new Result(null,
                    $"'{match.Header}' is in {where} but hidden in this IDE, so it was not invoked.");

            found = match;
            parent = match.Item;
            items = match.Children;
        }

        var hit = found!.Value;
        return new Result(hit.Item, null, hit.Header, hit.Command, hit.Parameter);
    }

    private static bool Named(Entry e, string text) => string.Equals(e.Header, text, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// A menu entry from a logical item: a <see cref="MenuItem"/>, the container realised for a bound item, or,
    /// for an <c>ItemsSource</c>-backed submenu that has not been opened, the item's own view model.
    /// </summary>
    /// <remarks>
    /// Such a submenu holds view models until its popup opens, so it used to answer "It has no items." while
    /// Recent Projects was plainly showing entries (#544). Their container theme binds <c>Header</c> and
    /// <c>Command</c> straight off the view model, so reading the same members gives what the menu would show
    /// and run. A separator, or anything else without a header, is not an entry.
    /// </remarks>
    private static Entry? EntryOf(object? item, MenuItem? parent)
    {
        if (item is null)
            return null;

        if ((item as MenuItem ?? parent?.ContainerFromItem(item) as MenuItem) is { } menuItem)
            return new Entry(StripAccessKey(menuItem.Header?.ToString() ?? string.Empty), menuItem,
                menuItem.Command, menuItem.CommandParameter, menuItem.Items, menuItem.IsVisible);

        var type = item.GetType();
        if (type.GetProperty("Header")?.GetValue(item)?.ToString() is not { } header)
            return null;
        return new Entry(StripAccessKey(header), null,
            type.GetProperty("Command")?.GetValue(item) as ICommand,
            type.GetProperty("CommandParameter")?.GetValue(item),
            type.GetProperty("Items")?.GetValue(item) as IEnumerable<object?> ?? [],
            Visible: true);
    }
}
