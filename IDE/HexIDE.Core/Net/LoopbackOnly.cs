using System;
using System.Net;

namespace HexIDE.Net;

/// <summary>
/// Whether an HTTP request to a local automation server really came from this machine.
/// </summary>
/// <remarks>
/// <b>Binding a server to loopback is not the control it looks like.</b> It stops a request arriving from
/// the network. It stops nothing that a browser already running on this machine can be talked into
/// sending, and a browser is the one client that reaches localhost without anybody choosing to.
///
/// <para>
/// The attack is DNS rebinding, and it defeats the ordinary same-origin protections rather than being
/// caught by them. A page served from <c>evil.example</c> is same-origin with itself. The name is then
/// re-resolved to <c>127.0.0.1</c>, so the browser sends its request to the server on this machine
/// believing nothing has changed, and no cross-origin preflight ever happens — there is, as far as it can
/// tell, nothing to preflight. What the trick cannot hide is the <c>Host</c> header, which still names
/// <c>evil.example</c>, because that is the name the browser was asked for. Comparing that against the
/// loopback names is therefore the whole of the fix, and it is why the Model Context Protocol requires a
/// local HTTP server to make exactly this check.
/// </para>
///
/// <para>
/// <b>This defends the browser case and only the browser case, deliberately.</b> Anything that can already
/// open a socket to the port can put whatever it likes in the header. But such a caller never needed
/// rebinding in the first place, and what is missing against it is authentication, which is a larger
/// change with its own consequences for every client. Do not read this class as more than it is: it closes
/// the hole loopback leaves open, not the hole loopback was never covering.
/// </para>
/// </remarks>
public static class LoopbackOnly
{
    /// <summary>
    /// True when <paramref name="host"/> and <paramref name="origin"/> are consistent with a request that
    /// originated on this machine and was addressed to the port this server actually bound.
    /// </summary>
    /// <param name="host">The host part of the <c>Host</c> header, without its port.</param>
    /// <param name="hostPort">The port part of the <c>Host</c> header, or null when it carried none.</param>
    /// <param name="boundPort">The port the server is listening on.</param>
    /// <param name="origin">The <c>Origin</c> header, or null/empty when there was none.</param>
    public static bool Allows(string? host, int? hostPort, int boundPort, string? origin)
    {
        // A Host naming the right machine but the wrong port is not this server's traffic, and a Host
        // carrying no port at all cannot be checked — both are refused rather than guessed at.
        if (hostPort != boundPort) return false;
        if (!IsLoopbackName(host)) return false;

        // Absent is the ordinary case and is not suspicious: an automation client is not a web page and
        // sends no Origin. Present is worth reading, because the only thing that sends one is a browser.
        if (string.IsNullOrEmpty(origin)) return true;

        // An opaque origin serialises as the literal "null", which is not a URI and so fails here. That is
        // the right answer: a sandboxed frame is exactly the caller that should not be driving this IDE.
        return Uri.TryCreate(origin, UriKind.Absolute, out var parsed)
               && parsed.Port == boundPort
               && IsLoopbackName(parsed.Host);
    }

    /// <summary>
    /// Whether a bare host names this machine — <c>localhost</c>, or any address in a loopback range.
    /// </summary>
    /// <remarks>
    /// The whole of <c>127.0.0.0/8</c> counts, not just <c>127.0.0.1</c>, because all of it is loopback and
    /// refusing part of it would reject a legitimate client for no gain. The bracket strip is for IPv6:
    /// a literal travels as <c>[::1]</c> in both a Host header and a URI's host, and
    /// <see cref="IPAddress.TryParse(string, out IPAddress)"/> does not accept the brackets.
    /// </remarks>
    private static bool IsLoopbackName(string? host)
    {
        if (string.IsNullOrEmpty(host)) return false;
        if (string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)) return true;

        var literal = host.Length > 1 && host[0] == '[' && host[^1] == ']' ? host[1..^1] : host;
        return IPAddress.TryParse(literal, out var address) && IPAddress.IsLoopback(address);
    }
}
