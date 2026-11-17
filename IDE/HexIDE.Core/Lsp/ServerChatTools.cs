using System.Text.Json;

namespace HexIDE.Lsp;

/// <summary>
/// Whether a language server may give AI Chat tools and a context fragment, and how its tools are approved (#4257).
/// </summary>
/// <remarks>
/// <para>
/// <b>A registration offers nothing unless its route says it may</b> (its <c>ChatTools</c> is null by default). The
/// configuration file's entries and a consented add-in's servers go through <c>LanguageServerRegistrationFactory</c>, which
/// gives them the entry's policy, <see cref="Hints"/> unless the entry says otherwise. Anything a project or workspace
/// might one day supply would come by another route, and so offers nothing until that route decides to. The person's own
/// setting (<see cref="ServerChatToolsSetting.ParseUser"/>) then wins over the entry's.
/// </para>
/// </remarks>
public enum ServerChatToolsPolicy
{
    /// <summary>The server's tools and context are not used.</summary>
    Off,

    /// <summary>A tool asks before each call unless it says it only reads (see <see cref="ServerChatToolsApproval"/>).</summary>
    Hints,

    /// <summary>
    /// Every call to the server's tools asks first, whatever the tool says about itself, and the server's context fragment
    /// is not used, since it would go on every send without a question.
    /// </summary>
    AskEveryCall,
}

/// <summary>The <c>chatTools</c> values of a <c>lsp-servers.json</c> entry, and what each means.</summary>
public static class ServerChatToolsSetting
{
    public const string Off = "off";
    public const string Hints = "hints";
    public const string Ask = "ask";

    /// <summary>The person's setting for "use the tools, asking as their hints say": <see cref="ServerChatToolsPolicy.Hints"/>.</summary>
    public const string On = "on";

    /// <summary>
    /// The policy a written value names, ignoring case and surrounding space; <see cref="ServerChatToolsPolicy.Hints"/>
    /// for none. Null for a value that is none of the three, which the loader reports.
    /// </summary>
    public static ServerChatToolsPolicy? Parse(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "" => ServerChatToolsPolicy.Hints,
        Off => ServerChatToolsPolicy.Off,
        Hints => ServerChatToolsPolicy.Hints,
        Ask => ServerChatToolsPolicy.AskEveryCall,
        _ => null,
    };

    /// <summary>
    /// The policy the person's own setting names (<c>on</c>, <c>ask</c> or <c>off</c>, the values Options writes), or null
    /// where they set none, which leaves the entry's.
    /// </summary>
    public static ServerChatToolsPolicy? ParseUser(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        On => ServerChatToolsPolicy.Hints,
        Ask => ServerChatToolsPolicy.AskEveryCall,
        Off => ServerChatToolsPolicy.Off,
        _ => null,
    };

    /// <summary>
    /// The person's setting for each server, as the composition reads it: from Options' own map, by server id, on every
    /// call. Nothing else in the settings is read. A server's own settings, which its add-in may write (#4307), live in a
    /// slot of their own and can't name a policy.
    /// </summary>
    public static Func<string, ServerChatToolsPolicy?> FromUserSettings(HexIDE.IDE.ISettingsService settings) =>
        id => settings.ServerChatTools.TryGetValue(id, out var value) ? ParseUser(value) : null;

    /// <summary>The value Options writes for a policy.</summary>
    public static string UserValue(ServerChatToolsPolicy policy) => policy switch
    {
        ServerChatToolsPolicy.Hints => On,
        ServerChatToolsPolicy.AskEveryCall => Ask,
        _ => Off,
    };

    /// <summary>
    /// What applies to a server: nothing where its route can't offer tools; otherwise the person's setting, or else its
    /// entry's.
    /// </summary>
    public static ServerChatToolsPolicy Effective(ServerChatToolsPolicy? entry, ServerChatToolsPolicy? user) =>
        entry is { } fromEntry ? user ?? fromEntry : ServerChatToolsPolicy.Off;
}

/// <summary>
/// A server's <c>experimental.hexide.chatTools</c> capability: the methods it answers for AI Chat (#4257).
/// </summary>
/// <remarks>
/// <para>
/// The server names its own methods, so a server keeps its own method names (RDCore's <c>rdcore/tools/list</c>) and
/// HexIDE defines one capability key. Shape:
/// <c>"experimental": {"hexide.chatTools": {"list": "…", "call": "…", "context": "…"}}</c>, with <c>context</c>
/// optional.
/// </para>
/// <para>
/// <b>A named method stays a chat tool.</b> It is only ever sent to the server that named it, and only as AI Chat's
/// list, call or context request. The approval rules apply to a call whatever the method is called, so a server that
/// names <c>textDocument/rename</c> gains nothing by it.
/// </para>
/// </remarks>
public sealed record ServerChatToolsCapability(string List, string Call, string? Context)
{
    /// <summary>The key under <c>experimental</c>.</summary>
    public const string Key = "hexide.chatTools";

    /// <summary>The longest method name taken from a server.</summary>
    public const int MaxMethodLength = 128;

    /// <summary>
    /// The capability a server's <c>initialize</c> result declares, or null where it declares none, or one without a
    /// <c>list</c> and a <c>call</c> method HexIDE can send.
    /// </summary>
    public static ServerChatToolsCapability? Read(JsonElement? capabilities)
    {
        if (capabilities is not { ValueKind: JsonValueKind.Object } caps
            || !caps.TryGetProperty("experimental", out var experimental)
            || experimental.ValueKind != JsonValueKind.Object
            || !experimental.TryGetProperty(Key, out var declared)
            || declared.ValueKind != JsonValueKind.Object)
            return null;

        var list = Method(declared, "list");
        var call = Method(declared, "call");
        if (list is null || call is null) return null;
        return new ServerChatToolsCapability(list, call, Method(declared, "context"));
    }

    private static string? Method(JsonElement declared, string name) =>
        declared.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
        && value.GetString() is { Length: > 0 and <= MaxMethodLength } method
        && method.Trim() == method && !method.Any(char.IsControl)
            ? method
            : null;
}

/// <summary>Whether a server's tool asks before it runs (#4257).</summary>
public static class ServerChatToolsApproval
{
    /// <summary>
    /// True when a call to the tool asks first. It asks unless the server's policy is <see cref="ServerChatToolsPolicy.Hints"/>,
    /// the tool says <c>readOnlyHint: true</c> and <c>destructiveHint: false</c>, and it doesn't say it runs code.
    /// </summary>
    /// <remarks>
    /// The hints are the server's own claims about its tool, and MCP, whose shape they follow, calls them hints rather than
    /// guarantees. An absent hint takes MCP's default, which is the cautious one: not read-only, and destructive.
    /// </remarks>
    public static bool Asks(ServerChatToolsPolicy policy, bool? readOnlyHint, bool? destructiveHint, bool runsCode) =>
        policy != ServerChatToolsPolicy.Hints
        || runsCode
        || readOnlyHint != true
        || destructiveHint != false;
}

/// <summary>A server whose route lets it offer AI Chat tools, as Options lists it for the person's choice (#4257).</summary>
/// <param name="Id">The server's id, which the person's setting is keyed by.</param>
/// <param name="DisplayName">Its name, for the person.</param>
/// <param name="Origin">The add-in that contributed it, or null for the configuration file's and the bundled server.</param>
/// <param name="Entry">What its entry says, which applies while the person sets nothing.</param>
public sealed record ServerChatToolsCandidate(string Id, string DisplayName, string? Origin, ServerChatToolsPolicy Entry);
