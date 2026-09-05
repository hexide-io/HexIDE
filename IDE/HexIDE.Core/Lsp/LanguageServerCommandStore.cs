using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Lsp;

/// <summary>One entry's command, as it was when the IDE last recorded having seen it.</summary>
public sealed class SeenCommand
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    /// <summary>
    /// The command and its arguments, as one string. Arguments are included deliberately: <c>node</c> is
    /// harmless and <c>node /tmp/x.js</c> is whatever <c>x.js</c> says, so a change of arguments is as
    /// much a change of what will run as a change of executable.
    /// </summary>
    [JsonPropertyName("command")]
    public string? Command { get; set; }
}

public sealed class SeenCommandFile
{
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("entries")]
    public SeenCommand[]? Entries { get; set; }
}

[JsonSerializable(typeof(SeenCommandFile))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class SeenCommandJsonContext : JsonSerializerContext { }

/// <summary>
/// Remembers which command each entry named the last time the IDE ran, so a command it has not seen before
/// can be announced rather than launched quietly.
///
/// <para>
/// <b>What this is for.</b> Typing a path into your own configuration is consent. A file appearing with a
/// path in it is not — and <c>lsp-servers.json</c> is an ordinary file that any process running as the user
/// may write. An entry naming an executable is launched on every start thereafter, so without this, writing
/// that file is a durable way to have the IDE run something indefinitely and silently. That is the shape
/// the add-in consent design exists to prevent, and it does not stop being that shape because the program
/// happens to speak LSP.
/// </para>
///
/// <para>
/// <b>What this is deliberately NOT.</b> No signing, no certificate chain, no revocation. Those are
/// proportionate to loading code into the IDE's own process, which is what an add-in does; a language
/// server is a separate process the user already installed by other means. The whole requirement here is
/// that the first launch of a new command is not silent.
/// </para>
/// </summary>
public sealed class LanguageServerCommandStore
{
    /// <summary>The shape this store understands. A file declaring a later one is not read.</summary>
    public const int SupportedVersion = 1;

    private readonly string _filePath;
    private readonly Dictionary<string, string> _byId = new(StringComparer.Ordinal);

    public LanguageServerCommandStore() : this(DefaultFilePath()) { }

    /// <param name="filePath">Explicit path, so tests drive real files rather than a mocked filesystem.</param>
    public LanguageServerCommandStore(string filePath)
    {
        _filePath = filePath;
        Load();
    }

    public static string DefaultFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "HexIDE", "lsp-servers-seen.json");
    }

    /// <summary>
    /// Whether this entry has named some other command before — or none at all.
    ///
    /// <para>
    /// An unreadable store answers "yes" for everything. Announcing a command the user has already seen is
    /// a nuisance; staying quiet about one they have not is the failure this exists to prevent, so the
    /// unreadable case fails towards the nuisance.
    /// </para>
    /// </summary>
    public bool IsNewOrChanged(string id, string command) =>
        !_byId.TryGetValue(id, out var seen) || !string.Equals(seen, command, StringComparison.Ordinal);

    /// <summary>Records this command as seen, and persists immediately.</summary>
    public void Record(string id, string command)
    {
        _byId[id] = command;
        Save();
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;

            var file = JsonSerializer.Deserialize(
                File.ReadAllBytes(_filePath), SeenCommandJsonContext.Default.SeenCommandFile);

            // A future shape is not guessed at. Treating it as "nothing seen" re-announces every command,
            // which is the safe direction — the same call the add-in consent store makes.
            if (file is null || file.Version > SupportedVersion) return;

            foreach (var entry in file.Entries ?? [])
                if (!string.IsNullOrEmpty(entry.Id) && entry.Command is not null)
                    _byId[entry.Id] = entry.Command;
        }
        catch
        {
            // Deliberately silent and deliberately empty: an unreadable store means every command is
            // announced again, which is exactly what an attacker deleting this file would achieve, and is
            // the outcome that costs a person a notice rather than costing them a silent launch.
            _byId.Clear();
        }
    }

    private void Save()
    {
        try
        {
            var directory = Path.GetDirectoryName(_filePath);
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

            var file = new SeenCommandFile
            {
                Entries = _byId.Select(kv => new SeenCommand { Id = kv.Key, Command = kv.Value }).ToArray(),
            };

            // Written through a temp file and moved into place: a store truncated by an interrupted write
            // would re-announce everything, which is noisy rather than dangerous, but is still avoidable.
            var temporary = _filePath + ".tmp";
            File.WriteAllBytes(
                temporary, JsonSerializer.SerializeToUtf8Bytes(file, SeenCommandJsonContext.Default.SeenCommandFile));
            File.Move(temporary, _filePath, overwrite: true);
        }
        catch
        {
            // Failing to remember costs a repeated notice, never a silent launch. Not worth failing a start.
        }
    }
}
