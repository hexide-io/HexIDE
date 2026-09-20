using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Bookmarks;

public class BookmarkService : IBookmarkService
{
    private readonly Dictionary<DocumentIdentity, SortedSet<int>> _bookmarks = new();

    public event Action<DocumentIdentity>? BookmarksChanged;

    public void SetBookmarks(DocumentIdentity document, IEnumerable<int> lines)
    {
        var set = new SortedSet<int>(lines);
        if (set.Count == 0)
            _bookmarks.Remove(document);
        else
            _bookmarks[document] = set;
        // Raised for the empty case too, as the breakpoint store always has. It used to return first, so
        // clearing a document's bookmarks emptied the store and left the gutter's dots on screen — and,
        // because the sidecar saves on this event, the cleared bookmarks came back on the next load.
        BookmarksChanged?.Invoke(document);
    }

    public void Toggle(DocumentIdentity document, int line)
    {
        if (!_bookmarks.TryGetValue(document, out var set))
        {
            set = new SortedSet<int>();
            _bookmarks[document] = set;
        }

        if (!set.Remove(line))
            set.Add(line);

        BookmarksChanged?.Invoke(document);
    }

    public bool IsBookmarked(DocumentIdentity document, int line)
        => _bookmarks.TryGetValue(document, out var set) && set.Contains(line);

    public int? NextBookmark(DocumentIdentity document, int currentLine)
    {
        if (!_bookmarks.TryGetValue(document, out var set) || set.Count == 0)
            return null;

        // Find first bookmark after currentLine, wrapping around
        var after = set.GetViewBetween(currentLine + 1, int.MaxValue);
        return after.Count > 0 ? after.Min : set.Min;
    }

    public int? PreviousBookmark(DocumentIdentity document, int currentLine)
    {
        if (!_bookmarks.TryGetValue(document, out var set) || set.Count == 0)
            return null;

        // Find last bookmark before currentLine, wrapping around
        var before = set.GetViewBetween(int.MinValue, currentLine - 1);
        return before.Count > 0 ? before.Max : set.Max;
    }

    public void ClearAll(DocumentIdentity document)
    {
        if (_bookmarks.TryGetValue(document, out var set) && set.Count > 0)
        {
            set.Clear();
            BookmarksChanged?.Invoke(document);
        }
    }

    public void ClearProject(ProjectDefinition project)
    {
        var mine = _bookmarks.Keys.Where(document => document.IsIn(project)).ToList();
        foreach (var document in mine)
        {
            _bookmarks.Remove(document);
            BookmarksChanged?.Invoke(document);
        }
    }

    public IReadOnlyList<int> GetBookmarks(DocumentIdentity document)
    {
        if (!_bookmarks.TryGetValue(document, out var set))
            return Array.Empty<int>();
        return new List<int>(set);
    }
}
