using System.Text.Json;

namespace HexIDE.Lsp;

/// <summary>
/// Whether a language server may give AI Chat tools and a context fragment, and how its tools are approved (#4257).
/// </summary>
/// <remarks>
/// <para>
/// <b>Off is the default of a registration,</b> so a server is offered to AI Chat only where the route that made its
/// registration says so. The configuration file's entries and a consented add-in's servers go through
/// <c>LanguageServerRegistrationFactory</c>, which gives them <see cref="Hints"/> unless the entry says otherwise. Anything
/// a project or workspace might one day supply would come by another route, and so offers nothing until that route
/// decides to.
/// </para>
/// </remarks>
public enum ServerChatToolsPolicy
{
    /// <summary>The server's tools and context are not used.</summary>
    Off,

    /// <summary>A tool asks before each call unless it says it only reads (see <see cref="ServerChatToolsApproval"/>).</summary>
    Hints,

    /// <summary>Every call to the server's tools asks first, whatever the tool says about itself.</summary>
    AskEveryCall,
}

/// <summary>The <c>chatTools</c> values of a <c>lsp-servers.json</c> entry, and what each means.</summary>
public static class ServerChatToolsSetting
{
    public const string Off = "off";
    public const string Hints = "hints";
    public const string Ask = "ask";

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
