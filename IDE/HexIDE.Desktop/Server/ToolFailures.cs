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
                Log.Error(ex, "MCP tool {Tool} failed with an unhandled {ExceptionType}", tool, reported.GetType().Name);
                return new CallToolResult
                {
                    IsError = true,
                    Content = [new TextContentBlock { Text = Describe(tool, reported) }],
                };
            }
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

        // Both forms, because TEMP on Windows usually carries the short one. Neither contains the other, so the
        // order does not matter.
        foreach (var form in ProfileForms(profile))
            message = message.Replace(form, placeholder, comparison);
        return message;
    }

    private static IEnumerable<string> ProfileForms(string profile)
    {
        if (OperatingSystem.IsWindows() && ShortPathOf(profile) is { } shortForm
            && !string.Equals(shortForm, profile, StringComparison.OrdinalIgnoreCase))
            yield return shortForm;
        yield return profile;
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
