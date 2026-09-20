using System;
using System.Collections.Generic;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Bookmarks;

public interface IBookmarkService
{
    void Toggle(DocumentIdentity document, int line);
    void SetBookmarks(DocumentIdentity document, IEnumerable<int> lines);
    bool IsBookmarked(DocumentIdentity document, int line);
    int? NextBookmark(DocumentIdentity document, int currentLine);
    int? PreviousBookmark(DocumentIdentity document, int currentLine);
    void ClearAll(DocumentIdentity document);

    /// <summary>
    /// Forget every document of a project, without touching any other project's.
    /// </summary>
    /// <remarks>
    /// Filters this store's own keys, and must keep doing so. Walking the project's current forms and
    /// modules instead looks equivalent and is not: a document <em>removed</em> from the project while it
    /// carried marks is in neither list, so its entry would be left behind — and the key is the definition,
    /// which holds its project, which holds every other document and buffer in it. An unreachable entry in
    /// a store that lives as long as the session would therefore pin the whole project graph in memory for
    /// the rest of it.
    /// </remarks>
    void ClearProject(ProjectDefinition project);
    IReadOnlyList<int> GetBookmarks(DocumentIdentity document);
    event Action<DocumentIdentity>? BookmarksChanged;
}
