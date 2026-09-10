using System;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tools.ObjectBrowser;

/// <summary>
/// One hit from a workspace-wide symbol search: a name, the thing it belongs to, and enough to get there.
/// </summary>
/// <remarks>
/// <b>Separate from <see cref="OBMemberViewModel"/> because the two are reached differently.</b> A member
/// belongs to a class the browser already holds, so navigating to it starts from that class. A search hit
/// belongs to nothing the browser has loaded — a server found it in a file, and a URI with a position is
/// the whole of what is known about where it lives.
/// </remarks>
public sealed class OBSearchResultViewModel
{
    private readonly SymbolInformation symbol;

    public OBSearchResultViewModel(SymbolInformation symbol, OBMemberKind kind)
    {
        this.symbol = symbol;
        Kind = kind;
    }

    public string Name => symbol.Name;
    public OBMemberKind Kind { get; }
    public string Uri => symbol.Location.Uri;

    /// <summary>One-based, as an editor counts lines. The protocol counts from zero.</summary>
    public int Line => symbol.Location.Range.Start.Line + 1;

    public int Column => symbol.Location.Range.Start.Character;

    public string KindGlyph => OBMemberViewModel.GlyphFor(Kind);

    /// <summary>
    /// Where the hit is, as a person would say it: the container the server named, or the document.
    /// </summary>
    /// <remarks>
    /// <c>containerName</c> is optional and plenty of servers omit it, so the document is the fallback
    /// rather than the second line — a result that says only "CalcTotal" and nothing about where it was
    /// found is not usable for choosing between several hits of the same name, which is the case the list
    /// exists for.
    /// </remarks>
    public string Where =>
        !string.IsNullOrWhiteSpace(symbol.ContainerName)
            ? $"{symbol.ContainerName}  —  {DocumentName(Uri)}:{Line}"
            : $"{DocumentName(Uri)}:{Line}";

    /// <summary>
    /// The last segment of a URI, unescaped, for display only.
    /// </summary>
    /// <remarks>
    /// Not <c>System.IO.Path</c>: this is a URI rather than a host path, and its separator is a forward
    /// slash on every platform. An unparseable URI is shown whole, which is ugly and honest — a server that
    /// sent something odd should cost display polish, not hide the result it found.
    /// </remarks>
    private static string DocumentName(string uri)
    {
        if (!System.Uri.TryCreate(uri, UriKind.Absolute, out var parsed))
            return uri;
        var path = parsed.GetComponents(UriComponents.Path, UriFormat.Unescaped);
        var slash = path.LastIndexOf('/');
        var last = slash >= 0 ? path[(slash + 1)..] : path;
        return last.Length > 0 ? last : uri;
    }
}
