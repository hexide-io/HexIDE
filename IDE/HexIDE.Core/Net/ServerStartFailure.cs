using System;
using System.Net.Sockets;

namespace HexIDE.Net;

/// <summary>
/// What to tell the person who launched HexIDE when its automation server could not start, and which exit
/// code to leave with.
/// </summary>
/// <remarks>
/// A second HexIDE started with a port the first already holds used to open a normal window with no
/// server behind it. Kestrel did report the failure, but it was caught and written only to the log, so a
/// client kept talking to the <em>first</em> instance with nothing to say it was the wrong one
/// (hexide-io/HexIDE#53). The failure is now fatal, and this says why in the terms a person can act on.
/// </remarks>
public static class ServerStartFailure
{
    /// <summary>The port named by <c>--server-port</c> is held by something else.</summary>
    public const int PortInUseExitCode = 3;

    /// <summary>The server failed to start for any other reason.</summary>
    public const int FailedToStartExitCode = 4;

    /// <summary>
    /// Whether the failure is a port already in use. Kestrel wraps the socket error in its own
    /// <c>AddressInUseException</c>, itself sometimes wrapped again, so the whole chain is searched; the
    /// Kestrel type is matched by name because this assembly does not reference ASP.NET Core.
    /// </summary>
    public static bool IsPortInUse(Exception failure)
    {
        for (var ex = failure; ex is not null; ex = ex.InnerException)
        {
            if (ex is SocketException { SocketErrorCode: SocketError.AddressAlreadyInUse })
                return true;
            if (ex.GetType().Name == "AddressInUseException")
                return true;
        }
        return false;
    }

    /// <summary>The exit code and the message for a server that did not start on <paramref name="port"/>.</summary>
    public static (int ExitCode, string Message) Describe(Exception failure, int port)
    {
        if (IsPortInUse(failure))
            return (PortInUseExitCode,
                $"HexIDE did not start: port {port} is already in use, most likely by another HexIDE started with "
              + $"--server-port {port}. Starting anyway would open an IDE with no automation server, and a client "
              + "would go on talking to the other one. Stop that one, or pass a different port."
              + Environment.NewLine);

        var cause = failure;
        while (cause.InnerException is not null)
            cause = cause.InnerException;
        return (FailedToStartExitCode,
            $"HexIDE did not start: the automation server could not listen on port {port}. {cause.Message}"
          + Environment.NewLine);
    }
}
