using HexIDE.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

namespace HexIDE.Desktop.Server;

/// <summary>The loopback gate shared by the IDE and its real-HTTP tests.</summary>
internal static class LoopbackMiddleware
{
    public static void UseLoopbackOnly(this WebApplication app, int port)
    {
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
    }
}
