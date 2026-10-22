using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;

namespace HexIDE.Desktop.Server;

/// <summary>
/// Ties an IDE started with <c>--session</c> to the agent session that started it, and answers any other session's
/// tool call with what went wrong instead of running it (#1306).
/// </summary>
/// <remarks>
/// <para>
/// Ports keep sessions apart only while every session sets its own (#1212). Two sessions that set nothing both reach
/// 5123, and a session given the port of a stale IDE drives that IDE. Either way every reply looks exactly as it would
/// from the session's own build, so a change can be verified against a build that does not contain it. The client
/// names its session in a header (<c>.mcp.json</c> sends <c>HEXIDE_SESSION</c>), and this compares it with the one
/// the IDE was started for.
/// </para>
/// <para>
/// <b>A refused call is answered as an error result, never as a refused request.</b> Claude Code closes its
/// transport after three consecutive connection errors, and its tools then stay gone until <c>/mcp</c>, so a 403
/// here would turn "wrong IDE" into "no tools", which says less. Only <c>tools/call</c> is guarded: listing the tools
/// changes nothing, and a session that can list them can also read why its calls are refused.
/// </para>
/// <para>
/// <b>A guard against mistakes between cooperating sessions, not a security control.</b> The header is trivially
/// forged, and anything that can open the port can drive the IDE whatever it says (#352). An IDE started without
/// <c>--session</c> registers nothing and serves every caller, which is what a contributor with nothing set gets.
/// </para>
/// </remarks>
internal static class SessionGuard
{
    /// <summary>The header a client names its session in.</summary>
    public const string Header = "X-HexIDE-Session";

    /// <summary>
    /// Adds the guard to a server under construction, for an IDE started for <paramref name="session"/>. Adds nothing
    /// when there is no session. Called before any other tool-call filter, so a caller on the wrong IDE is told that
    /// first, rather than how to fix its arguments for a tool on an IDE that is not its own.
    /// </summary>
    public static IMcpServerBuilder Register(IMcpServerBuilder builder, string? session)
    {
        if (string.IsNullOrWhiteSpace(session))
            return builder;

        builder.Services.AddHttpContextAccessor();
        return builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, ct) =>
        {
            var caller = request.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers[Header].ToString();
            if (Refusal(session, caller) is { } refusal)
            {
                Log.Information("MCP tool {Tool} refused a call from another session: {Refusal}",
                    request.Params?.Name ?? "(unnamed)", refusal);
                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = refusal }] };
            }

            return await next(request, ct);
        }));
    }

    /// <summary>
    /// Why a call from <paramref name="caller"/> is refused by an IDE started for <paramref name="session"/>, or null
    /// when it is the same session. Names compare without regard to case or surrounding spaces, and an empty or absent
    /// header names no session, which is what <c>.mcp.json</c> sends from a session started without
    /// <c>HEXIDE_SESSION</c>.
    /// </summary>
    /// <remarks>
    /// The text may be pasted into a public issue, so it names the build by its directory with the user's profile
    /// replaced, and never names the IDE's <c>--user-data-dir</c>.
    /// </remarks>
    internal static string? Refusal(string session, string? caller)
    {
        var named = caller?.Trim();
        if (string.Equals(named, session.Trim(), StringComparison.OrdinalIgnoreCase))
            return null;

        var from = string.IsNullOrEmpty(named) ? "a client that named no session" : $"session '{named}'";
        return ToolFailures.WithoutProfile(
            $"This HexIDE was started for session '{session.Trim()}', and this call came from {from}, so it was not run. "
          + "A session reaches the IDE on the port HEXIDE_MCP_PORT gives it and names itself with HEXIDE_SESSION, and "
          + "both are read when the session starts: check where this one was started. This IDE is PID "
          + $"{Environment.ProcessId}, built in {AppContext.BaseDirectory}.");
    }
}
