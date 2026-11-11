using System.Text;
using System.Text.Encodings.Web;
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
            // Set inserts at the front. Reverse the form's documented order, keeping id first.
            foreach (var (path, value) in changes.OrderBy(p => p.Key == "id" ? 0 : 1).Reverse())
                added = Set(added, path.Split('.'), value, formatNewFields: false);
            return AddEntry(text, servers, added);
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

    /// <summary>
    /// Writes text as it was typed. The file is the user's to read, not a web page, so only what JSON itself requires is
    /// escaped: the default encoder would write "café" as "café" and "C++" as "C++".
    /// </summary>
    public static readonly JsonSerializerOptions AsTyped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private static string AddEntry(string text, Node servers, string added)
    {
        static string IndentAt(string source, int position)
        {
            var start = source.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
            var end = start;
            while (end < position && source[end] is ' ' or '\t') end++;
            return source[start..end];
        }

        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var arrayIndent = IndentAt(text, servers.Start);
        var entryIndent = servers.Items.Count > 0 ? IndentAt(text, servers.Items[0].Start) : "";
        var unit = entryIndent.StartsWith(arrayIndent, StringComparison.Ordinal)
            ? entryIndent[arrayIndent.Length..] : "";
        if (unit.Length == 0) unit = arrayIndent.Length > 0 ? arrayIndent : "  ";
        if (entryIndent.Length <= arrayIndent.Length) entryIndent = arrayIndent + unit;
        using var document = Parse(added);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions
        {
            Indented = true, IndentCharacter = unit[0], IndentSize = unit.Length, NewLine = newline,
            Encoder = AsTyped.Encoder,
        }))
            document.RootElement.WriteTo(writer);
        var formatted = Encoding.UTF8.GetString(stream.ToArray());
        bool HasInlineArray(Node node) => (text[node.Start] == '[' && node.Items.Count > 0
            && !text[node.Start..node.End].Contains('\n'))
            || node.Properties.Values.Any(HasInlineArray) || node.Items.Any(HasInlineArray);
        if (servers.Items.Any(HasInlineArray))
        {
            var arrays = new List<Node>();
            void Collect(Node node)
            {
                if (formatted[node.Start] == '[') arrays.Add(node);
                else foreach (var child in node.Properties.Values.Concat(node.Items)) Collect(child);
            }
            Collect(Node.Read(formatted));
            foreach (var array in arrays.OrderByDescending(n => n.Start))
            {
                using var items = Parse(formatted[array.Start..array.End]);
                var inline = "[" + string.Join(", ", items.RootElement.EnumerateArray()
                    .Select(item => JsonSerializer.Serialize(item, AsTyped))) + "]";
                formatted = formatted[..array.Start] + inline + formatted[array.End..];
            }
        }
        formatted = formatted.Replace(newline, newline + entryIndent, StringComparison.Ordinal);
        if (servers.Items.Count > 0)
        {
            var position = servers.Items[0].Start;
            var lineStart = text.LastIndexOf('\n', Math.Max(0, position - 1)) + 1;
            var prefix = string.IsNullOrWhiteSpace(text[lineStart..position]) ? "" : newline + entryIndent;
            return text.Insert(position, prefix + formatted + "," + newline + entryIndent);
        }
        var suffix = text[(servers.Start + 1)..(servers.End - 1)].Contains('\n') ? "" : newline + arrayIndent;
        return text.Insert(servers.Start + 1, newline + entryIndent + formatted + suffix);
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

    private static string Set(string text, string[] path, string value, bool formatNewFields = true)
    {
        var root = Node.Read(text);
        if (!root.Properties.TryGetValue(path[0], out var node))
        {
            var nested = path.Length == 1 ? value : Set("{}", path[1..], value, formatNewFields);
            return formatNewFields ? Insert(text, root, path[0], nested)
                : text.Insert(root.Start + 1, JsonSerializer.Serialize(path[0], AsTyped) + ":" + nested + (root.Properties.Count > 0 ? "," : ""));
        }
        var replacement = path.Length == 1 ? value
            : Set(text[node.Start..node.End].TrimStart().StartsWith('{') ? text[node.Start..node.End] : "{}", path[1..], value, formatNewFields);
        return text[..node.Start] + replacement + text[node.End..];
    }

    private static readonly string[] FieldOrder =
    [
        "id", "extensions", "languageId", "transport", "command", "arguments", "environment", "workingDirectory",
        "pipeName", "pipeRole", "endpoint", "tcpRole", "websocketRole", "displayName", "initializationOptions",
        "trace", "priority", "enabled", "capture", "workspaceArtifact", "projectImport",
    ];

    private static string Insert(string text, Node root, string name, string value)
    {
        if (root.Properties.Count == 0)
            return text.Insert(root.Start + 1, JsonSerializer.Serialize(name, AsTyped) + ":" + value + (root.Properties.Count > 0 ? "," : ""));
        var newline = text.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
        var first = root.PropertyStarts.First().Value;
        var line = text.LastIndexOf('\n', first) + 1;
        var indent = new string(text[line..first].TakeWhile(c => c is ' ' or '\t').ToArray());
        if (indent.Length == 0) indent = "  ";
        var rank = Array.IndexOf(FieldOrder, name);
        var next = root.PropertyStarts.FirstOrDefault(p => rank >= 0 && Array.IndexOf(FieldOrder, p.Key) > rank);
        var literal = JsonSerializer.Serialize(name, AsTyped) + ": " + value;
        if (next.Key is not null)
            return text.Insert(next.Value, (text.Contains('\n') ? "" : newline + indent) + literal + "," + newline + indent);
        return text.Insert(root.Properties.Last().Value.End, "," + newline + indent + literal);
    }

    private sealed class Node
    {
        public int Start;
        public int End;
        public Dictionary<string, Node> Properties { get; } = new(StringComparer.Ordinal);
        public List<Node> Items { get; } = [];
        public Dictionary<string, int> PropertyStarts { get; } = new(StringComparer.Ordinal);

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
                    node.PropertyStarts[name] = Encoding.UTF8.GetCharCount(bytes.AsSpan(0, (int)reader.TokenStartIndex));
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
