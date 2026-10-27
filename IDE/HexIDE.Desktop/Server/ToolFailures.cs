using System.Globalization;
using System.Numerics;
using System.Reflection;
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
internal static partial class ToolFailures
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
            var (problems, ignored) = ArgumentCheck.Check(request.Params?.Arguments, SchemaOf(request), tool);
            if (problems.Count > 0)
                return Refused(tool, problems);
            try
            {
                var result = await next(request, ct);
                return ignored.Count == 0 ? result : WithIgnored(result, ignored);
            }
            catch (Exception ex) when (ex is not McpException
                                       && !(ex is OperationCanceledException && ct.IsCancellationRequested))
            {
                var reported = ex is AggregateException { InnerExceptions.Count: 1 } single
                    ? single.InnerExceptions[0]
                    : ex;
                if (IsArgumentBinding(reported))
                    return Refused(tool, BindingProblems(reported, request.Params?.Arguments, tool));

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
    internal static List<string> BindingProblems(Exception ex, IDictionary<string, JsonElement>? arguments,
        string tool)
    {
        if (ex is ArgumentException argument)
            return [argument.Message.Replace(" (Parameter 'arguments')", "", StringComparison.Ordinal)];

        // The schema calls both int and long "integer". Reproduce the SDK's binding against the actual
        // method parameter, including numeric strings and array items, rather than guessing from JSON values.
        var problems = new List<string>();
        if (ParameterTypes.TryGetValue(tool, out var parameters) && arguments is not null)
            foreach (var (name, value) in arguments)
                if (parameters.TryGetValue(name, out var type))
                    try { _ = value.Deserialize(type, ReplyJson.Options); }
                    catch (JsonException binding)
                    {
                        var problem = IntegralProblem(value, type) is { } overflow
                            ? overflow + ParameterGuidance(tool, name) : WithoutProfile(binding.Message);
                        problems.Add($"'{name}': {problem}");
                    }
        return problems.Count > 0 ? problems : [WithoutProfile(ex.Message)];
    }

    internal static string? IntegralProblem(JsonElement value, Type type)
    {
        type = Nullable.GetUnderlyingType(type) ?? type;
        if (type.IsArray && value.ValueKind == JsonValueKind.Array)
        {
            var index = 0;
            foreach (var item in value.EnumerateArray())
            {
                if (IntegralProblem(item, type.GetElementType()!) is { } problem)
                    return $"item {index}: {problem}";
                index++;
            }
            return null;
        }
        var bounds = Type.GetTypeCode(type) switch
        {
            TypeCode.SByte => (new BigInteger(sbyte.MinValue), new BigInteger(sbyte.MaxValue)),
            TypeCode.Byte => (new BigInteger(byte.MinValue), new BigInteger(byte.MaxValue)),
            TypeCode.Int16 => (new BigInteger(short.MinValue), new BigInteger(short.MaxValue)),
            TypeCode.UInt16 => (new BigInteger(ushort.MinValue), new BigInteger(ushort.MaxValue)),
            TypeCode.Int32 => (new BigInteger(int.MinValue), new BigInteger(int.MaxValue)),
            TypeCode.UInt32 => (new BigInteger(uint.MinValue), new BigInteger(uint.MaxValue)),
            TypeCode.Int64 => (new BigInteger(long.MinValue), new BigInteger(long.MaxValue)),
            TypeCode.UInt64 => (new BigInteger(ulong.MinValue), new BigInteger(ulong.MaxValue)),
            _ => (BigInteger.Zero, BigInteger.Zero),
        };
        if (bounds == (BigInteger.Zero, BigInteger.Zero)
            || value.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
            return null;
        var text = value.ValueKind == JsonValueKind.String ? value.GetString()! : value.GetRawText();
        if (!BigInteger.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number)
            || (number >= bounds.Item1 && number <= bounds.Item2))
            return null;
        return number < bounds.Item1
            ? FormattableString.Invariant($"{text} is smaller than any whole number this argument can hold ({bounds.Item1})")
            : FormattableString.Invariant($"{text} is larger than any whole number this argument can hold ({bounds.Item2})");
    }

    internal static string ParameterGuidance(string tool, string name)
    {
        var description = typeof(ToolFailures).Assembly.GetTypes()
            .Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
            .SelectMany(t => t.GetMethods())
            .Where(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name == tool)
            .SelectMany(m => m.GetParameters()).FirstOrDefault(p => p.Name == name)
            ?.GetCustomAttribute<DescriptionAttribute>()?.Description;
        return description is null ? "" : $". {description}";
    }

    // Tools are declared in this assembly; the test host adds its synthetic tool signatures.
    internal static readonly Dictionary<string, Dictionary<string, Type>> ParameterTypes = typeof(ToolFailures).Assembly
        .GetTypes().Where(t => t.GetCustomAttribute<McpServerToolTypeAttribute>() is not null)
        .SelectMany(t => t.GetMethods())
        .Where(m => m.GetCustomAttribute<McpServerToolAttribute>()?.Name is not null)
        .GroupBy(m => m.GetCustomAttribute<McpServerToolAttribute>()!.Name!)
        .ToDictionary(g => g.Key, g => g.SelectMany(m => m.GetParameters()).Where(p => p.Name is not null)
            .GroupBy(p => p.Name!).Where(p => p.Select(x => x.ParameterType).Distinct().Count() == 1)
            .ToDictionary(p => p.Key, p => p.First().ParameterType));

    private static CallToolResult WithIgnored(CallToolResult result, List<string> ignored)
    {
        var content = result.Content.ToList();
        var index = content.FindIndex(c => c is TextContentBlock);
        JsonObject? reply = null;
        if (index >= 0)
            try { reply = JsonNode.Parse(((TextContentBlock)content[index]).Text) as JsonObject; }
            catch (JsonException) { }
        if (reply is not null)
        {
            reply["ignoredArguments"] = JsonSerializer.SerializeToNode(ignored, ReplyJson.Options);
            content[index] = new TextContentBlock { Text = reply.ToJsonString(ReplyJson.Options) };
        }
        else
            content.Add(new TextContentBlock { Text = string.Join("; ", ignored) });
        return new CallToolResult
        {
            Content = content,
            IsError = result.IsError,
            StructuredContent = result.StructuredContent,
            Meta = result.Meta,
        };
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

        // And each spelled with forward slashes, as a file:///<drive>:/Users/... URI carries it in an LSP message (#606).
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

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, EntryPoint = "GetShortPathNameW", SetLastError = true)]
    private static partial uint GetShortPathName(string longPath, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[] shortPath, uint bufferLength);
}
