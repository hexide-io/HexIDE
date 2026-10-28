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
/// <b>Both sides must agree, and "no session" is one of the answers.</b> An IDE started without <c>--session</c>
/// serves a client that names no session, which is what a contributor with nothing set gets, and refuses one that
/// names a session: that client was meant for an IDE started for it, and the likeliest way to land here instead is a
/// session that set <c>HEXIDE_SESSION</c> and not <c>HEXIDE_MCP_PORT</c>, and so reached 5123. What this cannot catch
/// is two sessions that both set nothing: neither names itself, so there is nothing to compare.
/// </para>
/// <para>
/// <b>A guard against mistakes between cooperating sessions, not a security control.</b> The header is trivially
/// forged, and anything that can open the port can drive the IDE whatever it says (#352).
/// </para>
/// </remarks>
internal static class SessionGuard
{
    /// <summary>The header a client names its session in.</summary>
    public const string Header = "X-HexIDE-Session";

    /// <summary>
    /// Adds the guard to a server under construction, for an IDE started for <paramref name="session"/>, or for none
    /// when it is null. Called before any other tool-call filter, so a caller on the wrong IDE is told that first,
    /// rather than how to fix its arguments for a tool on an IDE that is not its own.
    /// </summary>
    public static IMcpServerBuilder Register(IMcpServerBuilder builder, string? session)
    {
        builder.Services.AddHttpContextAccessor();
        return builder.WithRequestFilters(filters => filters.AddCallToolFilter(next => async (request, ct) =>
        {
            var caller = request.Services?.GetService<IHttpContextAccessor>()?.HttpContext?.Request.Headers[Header].ToString();
            if (Refusal(session, caller) is { } refusal)
            {
                Log.Information("MCP tool {Tool} refused a call from another session: {Refusal}",
                    request.Params?.Name ?? "(unnamed)", refusal);
                // Counted where a person can see it, as a stale or misdirected session would otherwise go unnoticed (#2451).
                HexIDE.Automation.AutomationServerStatus.Current.Refused(caller);
                return new CallToolResult { IsError = true, Content = [new TextContentBlock { Text = refusal }] };
            }

            return await next(request, ct);
        }));
    }

    /// <summary>
    /// Why a call from <paramref name="caller"/> is refused by an IDE started for <paramref name="session"/>, or null
    /// when the two agree: the same session, or neither naming one. Names compare without regard to case or
    /// surrounding spaces, and an empty or absent value names no session, which is what <c>.mcp.json</c> sends from a
    /// session started without <c>HEXIDE_SESSION</c>.
    /// </summary>
    /// <remarks>
    /// The text may be pasted into a public issue, so it names the build by its directory with the user's profile
    /// replaced, and never names the IDE's <c>--user-data-dir</c>.
    /// </remarks>
    internal static string? Refusal(string? session, string? caller)
    {
        var serves = string.IsNullOrWhiteSpace(session) ? null : session.Trim();
        var named = string.IsNullOrWhiteSpace(caller) ? null : caller.Trim();
        if (string.Equals(named, serves, StringComparison.OrdinalIgnoreCase))
            return null;

        var started = serves is null ? "for no session" : $"for session '{serves}'";
        var from = named is null ? "a client that named no session" : $"session '{named}'";
        return ToolFailures.WithoutProfile(
            $"This HexIDE was started {started}, and this call came from {from}, so it was not run. A client names its "
          + $"session in the {Header} header, which this repository's .mcp.json fills from HEXIDE_SESSION, and reaches "
          + "the IDE on the port HEXIDE_MCP_PORT gives it. Both are read when the session starts, so check where this "
          + "one was started, and start an IDE for it with scripts/dev-ide.ps1. This IDE is PID "
          + $"{Environment.ProcessId}, built in {AppContext.BaseDirectory}.");
    }
}
