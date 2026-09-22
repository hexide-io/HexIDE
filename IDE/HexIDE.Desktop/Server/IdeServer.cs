using HexIDE.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol.AspNetCore;
using Serilog;

namespace HexIDE.Desktop.Server;

internal static class IdeServer
{
    public static async Task RunAsync(int port, IdeContext context, CancellationToken ct)
    {
        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();

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
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<HexIdeTools>()
            // An exception a tool did not catch is reported with its type and message rather than the SDK's
            // bare "An error occurred invoking" (#603).
            .WithRequestFilters(filters => filters.AddCallToolFilter(ToolFailures.Filter));

        var app = builder.Build();

        // Ahead of every endpoint, health included: that endpoint reports the open project's name and
        // whether language services are up, which is not something to hand to whoever asks.
        app.Use(async (context, next) =>
        {
            if (!LoopbackOnly.Allows(
                    context.Request.Host.Host,
                    context.Request.Host.Port,
                    port,
                    context.Request.Headers.Origin.ToString()))
            {
                // Refused, and saying so. A 404 would be quieter but would leave a developer whose client
                // is genuinely misconfigured unable to tell "wrong address" from "turned me away".
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });

        app.MapGet("/health", (IdeContext ctx) => Results.Ok(new HealthResponse(
            Status: "ok",
            Project: ctx.ProjectManager.StartupProject?.Name,
            LspRunning: ctx.LspClient.IsRunning,
            Pid: Environment.ProcessId,
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
        await app.WaitForShutdownAsync(ct);
    }
}

/// <param name="Pid">
/// The process answering. A launcher that polls for a 200 needs this to know the 200 came from the process it
/// just started and not from an older one still holding the port (hexide-io/HexIDE#53).
/// </param>
internal record HealthResponse(
    string Status,
    string? Project,
    bool LspRunning,
    int Pid,
    DateTimeOffset Timestamp);

/// <summary>The automation server never started listening. The IDE must not carry on without it.</summary>
internal sealed class IdeServerStartException(int port, Exception inner)
    : Exception($"The automation server could not start on port {port}.", inner)
{
    public int Port { get; } = port;
}
