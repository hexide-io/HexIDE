using System;
using System.Collections.Generic;

namespace HexIDE.Bookmarks;

public interface IBookmarkService
{
    void Toggle(string documentUri, int line);
    void SetBookmarks(string documentUri, IEnumerable<int> lines);
    bool IsBookmarked(string documentUri, int line);
    int? NextBookmark(string documentUri, int currentLine);
    int? PreviousBookmark(string documentUri, int currentLine);
    void ClearAll(string documentUri);
    IReadOnlyList<int> GetBookmarks(string documentUri);
    event Action<string>? BookmarksChanged;
}
