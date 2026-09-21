using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using HexIDE.Net;

namespace HexIDE.Tests.Server;

/// <summary>
/// Guards hexide-io/HexIDE#53: a port that is already taken must be recognised however deeply the socket
/// error is wrapped, and must produce the exit code and message that say so. The last test takes a real
/// port and binds it twice, so the shape asserted is the one the operating system produces, not one
/// written from memory.
/// </summary>
public class ServerStartFailureTests
{
    private sealed class AddressInUseException(string message, Exception inner)
        : InvalidOperationException(message, inner);

    private static SocketException InUse() => new((int)SocketError.AddressAlreadyInUse);

    [Fact]
    public void A_bare_socket_error_is_port_in_use()
    {
        ServerStartFailure.IsPortInUse(InUse()).Should().BeTrue();
    }

    [Fact]
    public void Kestrels_wrapping_is_seen_through()
    {
        // Kestrel's shape: IOException("Failed to bind to address ...") around AddressInUseException around
        // the SocketException.
        var failure = new IOException("Failed to bind to address http://127.0.0.1:5123: address already in use.",
            new AddressInUseException("Only one usage of each socket address is normally permitted.", InUse()));

        var (code, message) = ServerStartFailure.Describe(failure, 5123);

        code.Should().Be(ServerStartFailure.PortInUseExitCode);
        message.Should().Contain("port 5123 is already in use").And.Contain("--server-port 5123");
    }

    [Fact]
    public void The_Kestrel_type_is_recognised_by_name_even_without_a_socket_error_inside()
    {
        ServerStartFailure.IsPortInUse(new AddressInUseException("in use", new Exception("no socket here")))
            .Should().BeTrue();
    }

    [Fact]
    public void Any_other_failure_says_the_server_could_not_listen_and_gives_the_cause()
    {
        var failure = new IOException("outer",
            new SocketException((int)SocketError.AccessDenied));

        var (code, message) = ServerStartFailure.Describe(failure, 80);

        code.Should().Be(ServerStartFailure.FailedToStartExitCode);
        message.Should().Contain("could not listen on port 80").And.NotContain("already in use");
    }

    [Fact]
    public void A_port_really_held_by_another_listener_is_classified_as_in_use()
    {
        var holder = new TcpListener(IPAddress.Loopback, 0) { ExclusiveAddressUse = true };
        holder.Start();
        try
        {
            var port = ((IPEndPoint)holder.LocalEndpoint).Port;
            var second = new TcpListener(IPAddress.Loopback, port) { ExclusiveAddressUse = true };

            var failure = FluentActions.Invoking(() => second.Start()).Should().Throw<SocketException>().Which;

            ServerStartFailure.IsPortInUse(failure).Should().BeTrue();
        }
        finally { holder.Stop(); }
    }
}
