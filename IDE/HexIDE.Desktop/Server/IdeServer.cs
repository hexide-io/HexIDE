using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using Serilog;

namespace HexIDE.Desktop.Server;

internal static class IdeServer
{
    /// <param name="session">The agent session this IDE was started for (<c>--session</c>), or null to serve every caller.</param>
    public static async Task RunAsync(int port, string? session, IdeContext context, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        // The IDE owns the process. ConsoleLifetime would swallow SIGTERM and stop only the server.
        builder.Services.RemoveAll<IHostLifetime>();
        builder.Services.AddSingleton<IHostLifetime, IdeServerLifetime>();

        // Listen on loopback and ONLY loopback, stated as an endpoint rather than as a URL.
        //
        // The two are not equivalent. A URL in `app.Urls` is overridden by ASPNETCORE_URLS if that happens
        // to be set in the developer's environment, so a stray variable can widen this to every interface
        // without anything here changing; an explicit ListenLocalhost wins over that variable, and Kestrel
        // says so in its log when the two disagree. It also cannot be widened by a careless edit to a
        // string, which "http://localhost:{port}" invites and "http://*:{port}" would silently accept.
        //
        // Kestrel binds both loopback addresses here and tolerates either being unavailable, which the
        // IPv4 and IPv6 literals written out by hand would not.
        builder.WebHost.ConfigureKestrel(options => options.ListenLocalhost(port));

        builder.Services.AddSingleton(context);
        // What the program prints, for the replies of the tools that run and stop it (#2531).
        ProgramPrinted.Shared.Attach(context.ProjectRunnerService, () => context.ProjectRunnerService.IsRunning);
        builder.Services.AddHttpContextAccessor();
        // A call from another agent session is refused before anything else (#1306), and an exception a tool did not
        // catch is reported with its type and message rather than the SDK's bare "An error occurred invoking" (#603).
        ToolFilters.Register(builder.Services
                .AddMcpServer()
                // Stateless, so a client attached before a relaunch is still answered after it. See McpTransport.
                .WithHttpTransport(McpTransport.Configure)
                .WithTools<HexIdeTools>(ReplyJson.Options), session)
            // A modal dialog open once a call is done is named in its reply (#681), and so is a held button (#2231).
            .WithRequestFilters(filters => filters
                .AddCallToolFilter(OpenDialogNote.Filter)
                .AddCallToolFilter(HeldButtonNote.Filter)
                .AddCallToolFilter(InlineSnapshot.Filter));

        var app = builder.Build();

        app.UseLoopbackOnly(port);

        app.MapGet("/health", (IdeContext ctx) => Results.Ok(new HealthResponse(
            Status: "ok",
            Project: ctx.ProjectManager.StartupProject?.Name,
            LspRunning: ctx.LspClient.IsRunning,
            Pid: Environment.ProcessId,
            Session: session,
            Timestamp: DateTimeOffset.UtcNow)));

        app.MapMcp("/mcp");

        // Distinguished from a failure later on, because only a failure to START means this process is an IDE
        // with no server, which a client cannot tell from one that has a server (hexide-io/HexIDE#53).
        try
        {
            await app.StartAsync(ct);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            throw new IdeServerStartException(port, ex);
        }

        // Only now: this line used to be written before the bind was attempted, so a port already in use
        // left a log that said the server was listening.
        Log.Information("HexIDE MCP server listening on http://localhost:{Port}", port);
        // And on screen, in the status bar, for the same reason (#2451).
        HexIDE.Automation.AutomationServerStatus.Current.Listening(port, session, ToolFailures.WithoutProfile(
            AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)));
        await app.WaitForShutdownAsync(ct);
    }
}

/// <param name="Pid">
/// The process answering. A launcher that polls for a 200 needs this to know the 200 came from the process it
/// just started and not from an older one still holding the port (hexide-io/HexIDE#53).
/// </param>
/// <param name="Session">
/// The agent session the IDE was started for with <c>--session</c>, or null when it serves every caller. A launcher
/// that checks this beside <paramref name="Pid"/> knows the IDE answering is the one it meant (hexide-io/HexIDE#1306).
/// </param>
internal record HealthResponse(
    string Status,
    string? Project,
    bool LspRunning,
    int Pid,
    string? Session,
    DateTimeOffset Timestamp);

/// <summary>The automation server never started listening. The IDE must not carry on without it.</summary>
internal sealed class IdeServerStartException(int port, Exception inner)
    : Exception($"The automation server could not start on port {port}.", inner)
{
    public int Port { get; } = port;
}

internal sealed class IdeServerLifetime : IHostLifetime
{
    public Task WaitForStartAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
