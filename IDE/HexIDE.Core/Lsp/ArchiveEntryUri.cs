namespace HexIDE.Lsp;

/// <summary>The wire name of an archive member: its container and ordinal name segments.</summary>
public static class ArchiveEntryUri
{
    public static string ForEntry(string containerPath, IEnumerable<string> segments) =>
        Build(LspDocumentUri.ForFile(containerPath), segments);

    private static string Build(string containerUri, IEnumerable<string> segments) =>
        "archive-entry:/" + string.Join('/', segments.Select(Uri.EscapeDataString))
        + "?container=" + Uri.EscapeDataString(containerUri);

    public static bool TryParse(string uri, out string containerUri, out string[] segments)
    {
        containerUri = "";
        segments = [];
        const string prefix = "archive-entry:/";
        if (!uri.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        var query = uri.IndexOf("?container=", StringComparison.Ordinal);
        if (query < prefix.Length || uri.Contains('#')) return false;
        var container = Uri.UnescapeDataString(uri[(query + "?container=".Length)..]);
        if (!Uri.TryCreate(container, UriKind.Absolute, out var parsed) || !parsed.IsFile) return false;
        containerUri = container;
        var path = uri[prefix.Length..query];
        segments = path.Length == 0 ? [] : path.Split('/').Select(Uri.UnescapeDataString).ToArray();
        return true;
    }

    internal static string Normalize(string containerUri, IEnumerable<string> segments) =>
        Build(containerUri, segments);
}
