using System.Runtime.InteropServices;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Turns an exception a tool did not catch into a reply that says what happened.
/// </summary>
/// <remarks>
/// <para>
/// Without this, the SDK answers any such exception with "An error occurred invoking '&lt;tool&gt;'." and
/// nothing else, so a caller cannot tell a bad argument from a defect from a broken IDE. It had been met
/// three separate times, each time dug out of the log (#603).
/// </para>
/// <para>
/// A backstop, not a substitute: a tool should still refuse bad input itself, with a reason, before anything
/// throws. The reply says it is a defect so it cannot be mistaken for one of those refusals. The stack goes to
/// the IDE log under the same tool name, not to the caller. <see cref="McpException"/> is left to the SDK, and
/// so is a cancellation the caller asked for; a tool's OWN timeout surfacing as a cancellation is a defect
/// like any other and is reported here.
/// </para>
/// <para>
/// The reply asks to be reported, so its text is written to be pasted into a public issue: the user's profile
/// directory is replaced with <c>%USERPROFILE%</c> (or <c>~</c>) wherever it appears, in its long and its
/// 8.3 short form. That is for the report, not a security control; the server's own exposure is #352. The log
/// keeps the message as thrown.
/// </para>
/// </remarks>
internal static class ToolFailures
{
    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            try
            {
                return await next(request, ct);
            }
            catch (Exception ex) when (ex is not McpException
                                       && !(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                var tool = request.Params?.Name ?? "(unnamed)";
                var reported = ex is AggregateException { InnerExceptions.Count: 1 } single
                    ? single.InnerExceptions[0]
                    : ex;
                if (IsArgumentBinding(reported))
                {
                    Log.Information("MCP tool {Tool} refused its arguments: {Message}", tool, reported.Message);
                    return new CallToolResult
                    {
                        IsError = true,
                        Content = [new TextContentBlock { Text = Refusal(tool, reported, request.Params?.Arguments, SchemaOf(request)) }],
                    };
                }

                Log.Error(ex, "MCP tool {Tool} failed with an unhandled {ExceptionType}", tool, reported.GetType().Name);
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = Describe(tool, reported) }],
                };
            }
        };

    /// <summary>
    /// True when the SDK could not bind the call's arguments to the tool's parameters, so the tool never ran: a missing
    /// required argument, or one whose JSON is not the parameter's type (#1270).
    /// </summary>
    /// <remarks>
    /// The SDK binds without checking the schema, and both throw from its parameter marshaller, which a tool's own code
    /// never runs in, and is off the stack by the time a tool runs. So the throw's frames decide it: an ArgumentException a tool
    /// throws itself is still a defect. Calling these a defect told a caller to report a bug and not to fix its call.
    /// </remarks>
    internal static bool IsArgumentBinding(Exception ex) =>
        ex is ArgumentException or System.Text.Json.JsonException
        && ex.StackTrace?.Contains("GetParameterMarshaller", StringComparison.Ordinal) == true;

    /// <summary>What a caller is told when its arguments could not be bound: which, and why, and that nothing was done.</summary>
    internal static string Refusal(
        string tool, Exception ex, IDictionary<string, System.Text.Json.JsonElement>? arguments,
        System.Text.Json.JsonElement? schema)
    {
        var problems = new List<string>();
        var properties = schema is { ValueKind: System.Text.Json.JsonValueKind.Object } s
                         && s.TryGetProperty("properties", out var p) && p.ValueKind == System.Text.Json.JsonValueKind.Object
            ? p : (System.Text.Json.JsonElement?)null;
        if (ex is ArgumentException { ParamName: { } param } argument)
            problems.Add(argument.Message.Replace($" (Parameter '{param}')", "", StringComparison.Ordinal));
        foreach (var (name, value) in arguments ?? new Dictionary<string, System.Text.Json.JsonElement>())
        {
            if (properties is not { } known) break;
            if (!known.TryGetProperty(name, out var declared))
                problems.Add($"it takes no '{name}' parameter");
            else if (Types(declared) is { Count: > 0 } types && !types.Any(t => Fits(value, t)))
                problems.Add($"'{name}' must be {string.Join(" or ", types.Where(t => t != "null"))}, not {Kind(value)}");
        }
        if (ex is not ArgumentException && problems.Count == 0)
            problems.Add(WithoutProfile(ex.Message));

        return $"'{tool}' refused its arguments, and nothing was done: {string.Join("; ", problems.Select(p => p.TrimEnd('.')))}. Its input schema "
               + "names each parameter and its type.";
    }

    private static System.Text.Json.JsonElement? SchemaOf(RequestContext<CallToolRequestParams> request) =>
        (request.MatchedPrimitive as McpServerTool)?.ProtocolTool.InputSchema
        ?? (request.Params?.Name is { } name && request.Server.ServerOptions.ToolCollection is { } tools
            && tools.TryGetPrimitive(name, out var tool) ? tool.ProtocolTool.InputSchema : null);

    private static List<string> Types(System.Text.Json.JsonElement declared) =>
        !declared.TryGetProperty("type", out var type) ? []
        : type.ValueKind == System.Text.Json.JsonValueKind.String ? [type.GetString()!]
        : type.ValueKind == System.Text.Json.JsonValueKind.Array
            ? [.. type.EnumerateArray().Where(t => t.ValueKind == System.Text.Json.JsonValueKind.String).Select(t => t.GetString()!)]
            : [];

    private static bool Fits(System.Text.Json.JsonElement value, string type) => type switch
    {
        "boolean" => value.ValueKind is System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False,
        "integer" => value.ValueKind == System.Text.Json.JsonValueKind.Number && value.TryGetInt64(out _),
        "number" => value.ValueKind == System.Text.Json.JsonValueKind.Number,
        "string" => value.ValueKind == System.Text.Json.JsonValueKind.String,
        "array" => value.ValueKind == System.Text.Json.JsonValueKind.Array,
        "object" => value.ValueKind == System.Text.Json.JsonValueKind.Object,
        "null" => value.ValueKind == System.Text.Json.JsonValueKind.Null,
        _ => true,
    };

    private static string Kind(System.Text.Json.JsonElement value) => value.ValueKind switch
    {
        System.Text.Json.JsonValueKind.True or System.Text.Json.JsonValueKind.False => "a boolean",
        System.Text.Json.JsonValueKind.Number => "a number",
        System.Text.Json.JsonValueKind.String => "a string",
        System.Text.Json.JsonValueKind.Array => "an array",
        System.Text.Json.JsonValueKind.Object => "an object",
        _ => "null",
    };

    internal static string Describe(string tool, Exception ex) =>
        $"'{tool}' failed with an unhandled {ex.GetType().FullName}: {WithoutProfile(ex.Message)} "
        + "This is a defect in the tool, not a refusal of your arguments: please report it. "
        + $"The stack trace is in the IDE log ({LogFolder}), under 'MCP tool {tool} failed'.";

    /// <summary>Where <c>LoggingSetup.GetLogDirectory("ide")</c> puts the log, written as the platform names it.</summary>
    private static string LogFolder =>
        OperatingSystem.IsWindows() ? @"%LOCALAPPDATA%\HexIDE\logs\ide"
        : OperatingSystem.IsMacOS() ? "~/Library/Application Support/HexIDE/logs/ide"
        : "~/.local/share/HexIDE/logs/ide";

    internal static string WithoutProfile(string message)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (string.IsNullOrEmpty(profile))
            return message;

        var placeholder = OperatingSystem.IsWindows() ? "%USERPROFILE%" : "~";
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        // Every spelling: long and 8.3 short (TEMP on Windows usually carries the short one), each with back- and
        // forward slashes. None contains another, so the order does not matter.
        foreach (var form in ProfileForms(profile))
            message = message.Replace(form, placeholder, comparison);
        return message;
    }

    private static IEnumerable<string> ProfileForms(string profile)
    {
        var forms = new List<string> { profile };
        if (OperatingSystem.IsWindows() && ShortPathOf(profile) is { } shortForm
            && !string.Equals(shortForm, profile, StringComparison.OrdinalIgnoreCase))
            forms.Add(shortForm);

        // And each spelled with forward slashes, as a file:///C:/Users/... URI carries it in an LSP message (#606).
        foreach (var form in forms.ToList())
            if (form.Contains('\\'))
                forms.Add(form.Replace('\\', '/'));
        return forms;
    }

    private static string? ShortPathOf(string path)
    {
        var buffer = new char[1024];
        var length = GetShortPathName(path, buffer, (uint)buffer.Length);
        return length is > 0 and < 1024 ? new string(buffer, 0, (int)length) : null;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetShortPathNameW", SetLastError = true)]
    private static extern uint GetShortPathName(string longPath, char[] shortPath, uint bufferLength);
}
