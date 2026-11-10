using System.Text;
using System.Text.Json;
using HexIDE.IDE;
using Microsoft.Extensions.Logging;

namespace HexIDE.Lsp;

/// <summary>Edits only named values in the JSONC file; the surrounding text is never serialized.</summary>
public sealed class LanguageServerEditorStore(
    string filePath, IReadOnlyList<LanguageServerEntry> defaults, ILogger<LanguageServerConfigLoader> logger,
    LanguageServerCommandStore seen, IWorkspaceArtifactProviderRegistry? providers = null)
{
    public IReadOnlyList<LanguageServerEntry> Defaults { get; } = defaults;
    private readonly LanguageServerConfigLoader _loader = new(filePath, logger);

    public string Read() => File.Exists(filePath) ? File.ReadAllText(filePath) : "{\"version\":1,\"servers\":[]}";

    public LanguageServerConfigResult Load() => _loader.Load(Defaults, artifactProviders: providers);

    public LanguageServerConfigResult Preview(string text) => _loader.Preview(text, Defaults, providers);

    public IReadOnlyList<string> UserIds(string text)
    {
        using var document = Parse(text);
        return document.RootElement.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array
            ? servers.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
                .Select(e => e.GetProperty("id").GetString()!).Distinct(StringComparer.Ordinal).ToArray() : [];
    }

    public string? UserEntry(string text, string id)
    {
        using var document = Parse(text);
        return document.RootElement.TryGetProperty("servers", out var servers) && servers.ValueKind == JsonValueKind.Array
            ? servers.EnumerateArray().LastOrDefault(e => e.ValueKind == JsonValueKind.Object
                && e.TryGetProperty("id", out var value) && value.ValueKind == JsonValueKind.String && value.GetString() == id)
                is { ValueKind: JsonValueKind.Object } entry ? entry.GetRawText() : null
            : null;
    }

    public static JsonDocument Parse(string text) => JsonDocument.Parse(text, new JsonDocumentOptions
    {
        CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
    });

    /// <summary>Values are JSON literals. Dotted paths address the documented nested form fields.</summary>
    public string Change(string text, string? originalId, IReadOnlyDictionary<string, string> changes)
    {
        var root = Node.Read(text);
        if (!root.Properties.TryGetValue("servers", out var servers))
            return Change(Insert(text, root, "servers", "[]"), originalId, changes);
        if (text[servers.Start] != '[')
            return Change(text[..servers.Start] + "[]" + text[servers.End..], originalId, changes);
        var entry = originalId is null ? null : servers.Items.LastOrDefault(n =>
            n.Properties.TryGetValue("id", out var id) && JsonSerializer.Deserialize<string>(text[id.Start..id.End]) == originalId);
        if (entry is null)
        {
            var added = "{}";
            foreach (var (path, value) in changes) added = Set(added, path.Split('.'), value);
            var separator = servers.Items.Count > 0 ? "," : "";
            return text.Insert(servers.Start + 1, added + separator);
        }
        foreach (var (path, value) in changes)
        {
            // Reparse after each edit: offsets beyond a changed value have moved.
            root = Node.Read(text);
            entry = root.Properties["servers"].Items.Last(n =>
                n.Properties.TryGetValue("id", out var id) && JsonSerializer.Deserialize<string>(text[id.Start..id.End]) == originalId);
            var replacement = Set(text[entry.Start..entry.End], path.Split('.'), value);
            text = text[..entry.Start] + replacement + text[entry.End..];
        }
        return text;
    }

    public bool Save(string expected, string updated, LanguageServerEntry entry)
    {
        // Do not overwrite a hand edit made while the form was open.
        if (Read() != expected) return false;
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
        Encoding encoding = new UTF8Encoding(false);
        if (File.Exists(filePath))
        {
            using var reader = new StreamReader(filePath, encoding, detectEncodingFromByteOrderMarks: true);
            reader.ReadToEnd();
            encoding = reader.CurrentEncoding;
        }
        AtomicFileWriter.WriteAllBytes(filePath, [.. encoding.GetPreamble(), .. encoding.GetBytes(updated)]);
        if (entry.Id is { } id && LanguageServerConfigLoader.LaunchedCommandOf(entry) is { } command)
            seen.Record(id, command);
        return true;
    }

    private static string Set(string text, string[] path, string value)
    {
        var root = Node.Read(text);
        if (!root.Properties.TryGetValue(path[0], out var node))
        {
            var nested = path.Length == 1 ? value : Set("{}", path[1..], value);
            return Insert(text, root, path[0], nested);
        }
        var replacement = path.Length == 1 ? value
            : Set(text[node.Start..node.End].TrimStart().StartsWith('{') ? text[node.Start..node.End] : "{}", path[1..], value);
        return text[..node.Start] + replacement + text[node.End..];
    }

    private static string Insert(string text, Node root, string name, string value) =>
        text.Insert(root.Start + 1, JsonSerializer.Serialize(name) + ":" + value + (root.Properties.Count > 0 ? "," : ""));

    private sealed class Node
    {
        public int Start;
        public int End;
        public Dictionary<string, Node> Properties { get; } = new(StringComparer.Ordinal);
        public List<Node> Items { get; } = [];

        public static Node Read(string text)
        {
            var bytes = Encoding.UTF8.GetBytes(text);
            var reader = new Utf8JsonReader(bytes, new JsonReaderOptions
            {
                CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true,
            });
            reader.Read();
            return Read(ref reader, bytes);
        }

        private static Node Read(ref Utf8JsonReader reader, byte[] bytes)
        {
            var node = new Node { Start = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, (int)reader.TokenStartIndex)) };
            if (reader.TokenType == JsonTokenType.StartObject)
                while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
                {
                    var name = reader.GetString()!;
                    reader.Read();
                    node.Properties[name] = Read(ref reader, bytes);
                }
            else if (reader.TokenType == JsonTokenType.StartArray)
                while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                    node.Items.Add(Read(ref reader, bytes));
            node.End = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, (int)reader.BytesConsumed));
            return node;
        }
    }
}
