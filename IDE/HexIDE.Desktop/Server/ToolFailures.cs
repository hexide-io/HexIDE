using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
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
    /// <summary>
    /// Checks text before the SDK converts its JsonNode request to CallToolRequestParams. That conversion writes
    /// the node again, and an escaped lone surrogate throws there before any tool-call filter can run (#1337).
    /// </summary>
    public static McpMessageHandler TextFilter(McpMessageHandler next) => async (context, ct) =>
    {
        if (context.JsonRpcMessage is JsonRpcRequest { Method: "tools/call", Params: JsonObject parameters } call
            && parameters["arguments"] is { } arguments)
        {
            var problems = new List<string>();
            try
            {
                if (arguments is JsonObject named)
                {
                    foreach (var argument in named)
                        if (InvalidText(argument.Value))
                            problems.Add($"'{argument.Key}' is not valid text (an unpaired UTF-16 surrogate)");
                }
                else if (InvalidText(arguments))
                    problems.Add("the request's arguments are not valid text (an unpaired UTF-16 surrogate)");
            }
            catch (InvalidOperationException)
            {
                // An invalid property name cannot itself be quoted in a reply.
                problems.Add("the request's arguments are not valid text (an unpaired UTF-16 surrogate)");
            }
            if (problems.Count > 0)
            {
                string? tool;
                try { tool = parameters["name"]?.GetValue<string>(); }
                catch (InvalidOperationException) { tool = null; }
                await context.Server.SendMessageAsync(new JsonRpcResponse
                {
                    Id = call.Id,
                    Result = JsonSerializer.SerializeToNode(Refused(tool ?? "(unnamed)", problems)),
                }, ct);
                return;
            }
        }
        await next(context, ct);
    };

    private static bool InvalidText(JsonNode? node)
    {
        if (node is JsonObject obj)
            return obj.Any(property => InvalidText(property.Value));
        if (node is JsonArray array)
            return array.Any(InvalidText);
        if (node is JsonValue value && value.GetValueKind() == JsonValueKind.String)
        {
            try { _ = value.GetValue<string>(); }
            catch (InvalidOperationException) { return true; }
        }
        return false;
    }

    public static McpRequestHandler<CallToolRequestParams, CallToolResult> Filter(
        McpRequestHandler<CallToolRequestParams, CallToolResult> next) =>
        async (request, ct) =>
        {
            var tool = request.Params?.Name ?? "(unnamed)";
            if (ArgumentCheck.Problems(request.Params?.Arguments, SchemaOf(request)) is { Count: > 0 } problems)
                return Refused(tool, problems);
            try
            {
                return await next(request, ct);
            }
            catch (Exception ex) when (ex is not McpException
                                       && !(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                var reported = ex is AggregateException { InnerExceptions.Count: 1 } single
                    ? single.InnerExceptions[0]
                    : ex;
                if (IsArgumentBinding(reported))
                    return Refused(tool, BindingProblems(reported, request.Params?.Arguments));

                Log.Error(ex, "MCP tool {Tool} failed with an unhandled {ExceptionType}", tool, reported.GetType().Name);
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = Describe(tool, reported) }],
                };
            }
        };

    /// <summary>
    /// True when the SDK could not bind an argument that <see cref="ArgumentCheck"/> passed, so the tool never ran: a
    /// required one missing where the schema could not be read, or JSON the parameter's type cannot hold, such as an
    /// integer too big for it (#1270).
    /// </summary>
    /// <remarks>
    /// Both throw from the SDK's parameter marshaller, and the missing one names its parameter as <c>arguments</c>. A
    /// service the SDK resolves for a parameter also throws there, with another name, and stays a defect. Calling these
    /// a defect told a caller to report a bug and not to fix its call.
    /// </remarks>
    internal static bool IsArgumentBinding(Exception ex) =>
        ex is ArgumentException { ParamName: "arguments" } and not ArgumentNullException or System.Text.Json.JsonException
        && ex.StackTrace?.Contains("GetParameterMarshaller", StringComparison.Ordinal) == true;

    private static CallToolResult Refused(string tool, List<string> problems)
    {
        var text = Refusal(tool, problems);
        Log.Information("MCP tool {Tool} refused its arguments: {Refusal}", tool, text);
        return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = text }] };
    }

    /// <summary>What a caller is told when its arguments could not be bound: which, and why, and that nothing was done.</summary>
    internal static string Refusal(string tool, IEnumerable<string> problems) =>
        $"'{tool}' refused its arguments, and nothing was done: {string.Join("; ", problems.Select(p => p.TrimEnd('.')))}. "
        + "Its input schema names each parameter and its type.";

    /// <summary>The problem a binding failure names, which never leaves the refusal saying nothing.</summary>
    internal static List<string> BindingProblems(Exception ex, IDictionary<string, System.Text.Json.JsonElement>? arguments)
    {
        if (ex is ArgumentException argument)
            return [argument.Message.Replace(" (Parameter 'arguments')", "", StringComparison.Ordinal)];

        // The serializer's message names neither the parameter nor anything useful past its type ("Path: $ |
        // LineNumber: 0"). Every integer parameter is an int, so one out of its range is the likely cause.
        var tooBig = (arguments ?? new Dictionary<string, System.Text.Json.JsonElement>())
            .Where(a => a.Value.ValueKind == System.Text.Json.JsonValueKind.Number
                        && a.Value.TryGetInt64(out var n) && n is > int.MaxValue or < int.MinValue)
            .Select(a => $"'{a.Key}' is {a.Value.GetRawText()}, outside the range of a 32-bit integer")
            .ToList();
        if (tooBig.Count > 0)
            return tooBig;
        var message = ex.Message;
        var cut = message.IndexOf(" Path: ", StringComparison.Ordinal);
        return [WithoutProfile(cut > 0 ? message[..cut] : message)];
    }

    private static System.Text.Json.JsonElement? SchemaOf(RequestContext<CallToolRequestParams> request) =>
        (request.MatchedPrimitive as McpServerTool)?.ProtocolTool.InputSchema
        ?? (request.Params?.Name is { } name && request.Server.ServerOptions.ToolCollection is { } tools
            && tools.TryGetPrimitive(name, out var tool) ? tool.ProtocolTool.InputSchema : null);

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
