using Microsoft.AspNetCore.Builder;
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

        builder.Services.AddSingleton(context);
        builder.Services
            .AddMcpServer()
            .WithHttpTransport()
            .WithTools<HexIdeTools>();

        var app = builder.Build();

        app.Urls.Add($"http://localhost:{port}");

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
