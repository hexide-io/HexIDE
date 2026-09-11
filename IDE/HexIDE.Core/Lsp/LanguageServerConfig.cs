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

    /// <summary>
    /// How much the protocol capture may hold, for every connection that does not override it.
    /// </summary>
    /// <remarks>
    /// <b>Here rather than in the application settings file, which is a deviation from the design record
    /// and a deliberate one.</b> The requirement is that a rejected value is reported through the
    /// configuration-problems channel — and that channel belongs to this file, is validated on load, and is
    /// already rendered in the Language &amp; Debug Servers window. The settings file has no problems list
    /// and nothing that would show one, so a mistyped limit there would be silently clamped, which is the
    /// exact failure the requirement exists to prevent. Moving the global tier once an Options page exists
    /// is cheap; shipping a silent clamp is not.
    /// </remarks>
    [JsonPropertyName("capture")]
    public CaptureLimitsEntry? Capture { get; set; }
}

/// <summary>
/// Capture limits as written by hand: every field optional, absent meaning "leave this one alone".
/// </summary>
/// <remarks>
/// Nullable throughout for the same reason every other property in this file is — a field the user did not
/// write must not be distinguishable from one they wrote the default value into, because layering an
/// override over a default is the whole mechanism.
/// </remarks>
public sealed class CaptureLimitsEntry
{
    [JsonPropertyName("envelopeEntries")]
    public int? EnvelopeEntries { get; set; }

    [JsonPropertyName("prologueEntries")]
    public int? PrologueEntries { get; set; }

    [JsonPropertyName("payloadBytesPerConnection")]
    public long? PayloadBytesPerConnection { get; set; }

    /// <summary>The ceiling across every connection together. Meaningful only at file level.</summary>
    /// <remarks>
    /// Written inside a server entry it is reported as an ignored field rather than quietly obeyed: a
    /// per-server entry raising a global ceiling would let one server's configuration decide what every
    /// other server is allowed to cost.
    /// </remarks>
    [JsonPropertyName("globalPayloadBytes")]
    public long? GlobalPayloadBytes { get; set; }

    [JsonPropertyName("frameBytes")]
    public int? FrameBytes { get; set; }

    /// <summary>These values layered over a baseline, leaving whatever was not written alone.</summary>
    /// <param name="includeGlobalCeiling">
    /// False where these came from a single server's entry. The ceiling is shared, so reading it from one
    /// entry would let that server decide what every other server is allowed to cost.
    /// </param>
    public HexIDE.Conversations.CaptureLimits Over(
        HexIDE.Conversations.CaptureLimits baseline, bool includeGlobalCeiling = true) =>
        baseline with
        {
            EnvelopeEntries = EnvelopeEntries ?? baseline.EnvelopeEntries,
            PrologueEntries = PrologueEntries ?? baseline.PrologueEntries,
            PayloadBytesPerConnection = PayloadBytesPerConnection ?? baseline.PayloadBytesPerConnection,
            GlobalPayloadBytes = (includeGlobalCeiling ? GlobalPayloadBytes : null)
                                 ?? baseline.GlobalPayloadBytes,
            FrameBytes = FrameBytes ?? baseline.FrameBytes,
        };

    /// <summary>Whether anything was written at all.</summary>
    public bool IsEmpty =>
        EnvelopeEntries is null && PrologueEntries is null && PayloadBytesPerConnection is null
        && GlobalPayloadBytes is null && FrameBytes is null;
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
    /// <summary>
    /// How much this server should be asked to say about its own work: <c>off</c>, <c>messages</c> or
    /// <c>verbose</c>. Absent means off.
    /// </summary>
    /// <remarks>
    /// Here rather than in application settings because it travels in the initialization request, so it has
    /// to be known before the process exists. Changing it on a server that is already running is a separate
    /// operation the protocol provides, and does not go through this file.
    /// </remarks>
    [JsonPropertyName("trace")]
    public string? Trace { get; set; }

    [JsonPropertyName("priority")]
    public int? Priority { get; set; }

    /// <summary>Absent means on. <c>false</c> keeps the entry in the file while creating no client.</summary>
    [JsonPropertyName("enabled")]
    public bool? Enabled { get; set; }

    /// <summary>
    /// How much the capture may hold for this server, over the file's own defaults.
    /// </summary>
    /// <remarks>
    /// Per server because a budget is exactly the thing raised for one noisy backend and not the rest.
    /// Measured on an identical twenty-edit script, one server returned roughly a hundred times another's
    /// inbound bytes — almost entirely completion replies — so a single number for all of them either
    /// starves the quiet server's history or pays for the noisy one everywhere.
    /// </remarks>
    [JsonPropertyName("capture")]
    public CaptureLimitsEntry? Capture { get; set; }

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

    /// <summary>
    /// The entry says <c>"enabled": false</c>, so no server was registered for it.
    /// </summary>
    /// <remarks>
    /// Not an error, and recorded anyway: a disabled entry produces no connection, so without a row of
    /// its own "I switched this off" and "this was never configured" are the same observable state — an
    /// absence. The point of switching something off temporarily is being able to see that you did.
    /// </remarks>
    Disabled,

    /// <summary>
    /// A field that is real, spelled correctly, and meaningless for this entry's transport — a
    /// <c>command</c> on a <c>pipe</c> entry, say.
    /// </summary>
    /// <remarks>
    /// Distinct from <see cref="UnrecognisedField"/> because the extension-data catch cannot see these:
    /// they are declared properties, so they bind cleanly and are then simply never read. The entry
    /// parses, registers, and connects to nothing, with no problem reported anywhere — a silently skipped
    /// row one level deeper than a typo.
    /// </remarks>
    IgnoredField,
}

// Names are pinned per property rather than left to the naming policy. This is a file people have
// written and keep; a C# rename must not quietly change what their file has to say.
[JsonSerializable(typeof(LanguageServerConfigFile))]
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase)]
public partial class LanguageServerConfigJsonContext : JsonSerializerContext { }
