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
            .WithTools<HexIdeTools>();

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
            Timestamp: DateTimeOffset.UtcNow)));

        app.MapMcp("/mcp");

        Log.Information("HexIDE MCP server listening on http://localhost:{Port}", port);

        await app.StartAsync(ct);
        await app.WaitForShutdownAsync(ct);
    }
}

internal record HealthResponse(
    string Status,
    string? Project,
    bool LspRunning,
    DateTimeOffset Timestamp);
