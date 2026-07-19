using System;
using System.Collections.Generic;

namespace HexIDE.Bookmarks;

public class BookmarkService : IBookmarkService
{
    private readonly Dictionary<string, SortedSet<int>> _bookmarks = new();

    public event Action<string>? BookmarksChanged;

    public void SetBookmarks(string documentUri, IEnumerable<int> lines)
    {
        var set = new SortedSet<int>(lines);
        if (set.Count == 0)
        {
            _bookmarks.Remove(documentUri);
            return;
        }
        _bookmarks[documentUri] = set;
        BookmarksChanged?.Invoke(documentUri);
    }

    public void Toggle(string documentUri, int line)
    {
        if (!_bookmarks.TryGetValue(documentUri, out var set))
        {
            set = new SortedSet<int>();
            _bookmarks[documentUri] = set;
        }

        if (!set.Remove(line))
            set.Add(line);

        BookmarksChanged?.Invoke(documentUri);
    }

    public bool IsBookmarked(string documentUri, int line)
        => _bookmarks.TryGetValue(documentUri, out var set) && set.Contains(line);

    public int? NextBookmark(string documentUri, int currentLine)
    {
        if (!_bookmarks.TryGetValue(documentUri, out var set) || set.Count == 0)
            return null;

        // Find first bookmark after currentLine, wrapping around
        var after = set.GetViewBetween(currentLine + 1, int.MaxValue);
        return after.Count > 0 ? after.Min : set.Min;
    }

    public int? PreviousBookmark(string documentUri, int currentLine)
    {
        if (!_bookmarks.TryGetValue(documentUri, out var set) || set.Count == 0)
            return null;

        // Find last bookmark before currentLine, wrapping around
        var before = set.GetViewBetween(int.MinValue, currentLine - 1);
        return before.Count > 0 ? before.Max : set.Max;
    }

    public void ClearAll(string documentUri)
    {
        if (_bookmarks.TryGetValue(documentUri, out var set) && set.Count > 0)
        {
            set.Clear();
            BookmarksChanged?.Invoke(documentUri);
        }
    }

    public IReadOnlyList<int> GetBookmarks(string documentUri)
    {
        if (!_bookmarks.TryGetValue(documentUri, out var set))
            return Array.Empty<int>();
        return new List<int>(set);
    }
}
