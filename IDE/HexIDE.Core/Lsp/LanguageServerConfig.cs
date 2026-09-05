using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Lsp;

/// <summary>
/// The on-disk shape of <c>lsp-servers.json</c>. A persisted contract, so every property is explicit and
/// nothing here is renamed casually.
/// </summary>
public sealed class LanguageServerConfigFile
{
    /// <summary>
    /// The file's shape, not the IDE's. Present so a future format can be recognised rather than
    /// misread — a file from a newer HexIDE is ignored with a warning instead of being half-understood.
    /// </summary>
    [JsonPropertyName("version")]
    public int Version { get; set; } = 1;

    [JsonPropertyName("servers")]
    public LanguageServerEntry[]? Servers { get; set; }
}

/// <summary>
/// One server, as written by a user or contributed as a default.
///
/// <para>
/// <b>Flat rather than nested per transport.</b> A <c>transport: { kind, command, args }</c> object models
/// the domain more tidily, and is worse to hand-write — which is the only way this file is produced. The
/// fields that do not apply to the chosen transport are simply absent, and validation says so if one is
/// missing that is needed.
/// </para>
///
/// <para>
/// <b>Every property is nullable, including the value types.</b> That is what distinguishes "the user did
/// not say" from "the user said the default value" — needed because an entry that omits a priority must
/// rank differently from one that writes <c>0</c>, and an entry that omits <c>enabled</c> is on.
/// </para>
/// </summary>
public sealed class LanguageServerEntry
{
    /// <summary>Stable identity. A user entry carrying a default's id replaces that default.</summary>
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("displayName")]
    public string? DisplayName { get; set; }

    /// <summary>
    /// The file extensions this server claims, leading dot included. Routing keys on these rather than on
    /// a language name, so that two servers may claim one extension and disagree about what it is called.
    /// </summary>
    [JsonPropertyName("extensions")]
    public string[]? Extensions { get; set; }

    /// <summary>
    /// What THIS server should be told a document is, in <c>didOpen</c>. Per-server rather than global:
    /// one server's <c>python</c> is another's <c>python3</c>, and each has its own connection, so neither
    /// has to be wrong.
    /// </summary>
    [JsonPropertyName("languageId")]
    public string? LanguageId { get; set; }

    /// <summary><c>stdio</c>, <c>pipe</c> or <c>websocket</c>.</summary>
    [JsonPropertyName("transport")]
    public string? Transport { get; set; }

    // ── stdio ─────────────────────────────────────────────────────────────────────────────────────────
    [JsonPropertyName("command")]
    public string? Command { get; set; }
    [JsonPropertyName("arguments")]
    public string? Arguments { get; set; }

    /// <summary>Where to run it. Defaults to the project's own directory when absent.</summary>
    [JsonPropertyName("workingDirectory")]
    public string? WorkingDirectory { get; set; }

    // ── websocket ─────────────────────────────────────────────────────────────────────────────────────
    [JsonPropertyName("endpoint")]
    public string? Endpoint { get; set; }

    // ── pipe ──────────────────────────────────────────────────────────────────────────────────────────
    [JsonPropertyName("pipeName")]
    public string? PipeName { get; set; }

    /// <summary><c>connect</c> (default) or <c>listen</c>.</summary>
    [JsonPropertyName("pipeRole")]
    public string? PipeRole { get; set; }

    /// <summary>
    /// Decides which server answers where only one can. Absent means zero; the entries HexIDE contributes
    /// as defaults sit below zero, so a user's server wins without them having to know this field exists.
    /// </summary>
    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    /// <summary>Absent means on. <c>false</c> keeps the entry in the file while creating no client.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>
    /// Anything the IDE did not recognise, captured rather than dropped.
    ///
    /// <para>
    /// This is what makes a misspelled field reportable. Without it, <c>"comand"</c> is silently ignored,
    /// the entry keeps its default of "no command", and the user gets a server that fails for no visible
    /// reason. The entry is still used — an unrecognised field is not grounds to discard the rest of it.
    /// </para>
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? Unrecognized { get; set; }
}

/// <summary>
/// Something wrong with the configuration, carried rather than thrown.
///
/// <para>
/// Collected instead of raised because one bad entry must not stop the others, and because these need to
/// reach a person: a language server that is absent and one that is attached with nothing to say look
/// identical from the editor.
/// </para>
/// </summary>
/// <param name="EntryId">The entry it concerns, where one could be identified.</param>
/// <param name="Message">What is wrong, in terms of the file the user wrote.</param>
/// <param name="EntryRejected">
/// True when the entry cannot be used at all. False for a problem worth reporting that still leaves a
/// usable entry — an unrecognised field being the case that matters.
/// </param>
/// <param name="Kind">
/// What sort of problem it is. Present because these do not deserve equal presentation: a misspelled field
/// is a nuisance, and "this entry will launch a program you have not seen before" is not. Retrofitting the
/// distinction once something renders these would mean guessing it back out of the message text.
/// </param>
public sealed record LanguageServerConfigProblem(
    string? EntryId,
    string Message,
    bool EntryRejected,
    LanguageServerConfigProblemKind Kind = LanguageServerConfigProblemKind.Configuration);

public enum LanguageServerConfigProblemKind
{
    /// <summary>The file or an entry is malformed — a missing field, an unknown transport, bad JSON.</summary>
    Configuration,

    /// <summary>A field the IDE did not recognise. Usually a typo; the entry is otherwise usable.</summary>
    UnrecognisedField,

    /// <summary>
    /// This entry names a command the IDE has not launched before, or has changed since it last did.
    /// Not an error — the ordinary case is a user who just wrote it — but the one problem here that is
    /// about trust rather than syntax.
    /// </summary>
    UnseenCommand,
}

// Names are pinned per property rather than left to the naming policy. This is a file people have
// written and keep; a C# rename must not quietly change what their file has to say.
[JsonSerializable(typeof(LanguageServerConfigFile))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class LanguageServerConfigJsonContext : JsonSerializerContext { }
