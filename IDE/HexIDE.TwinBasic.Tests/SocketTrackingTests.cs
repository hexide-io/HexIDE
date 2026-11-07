using System.Net;
using System.Net.Sockets;

namespace HexIDE.TwinBasic.Tests;

public sealed class SocketTrackingTests : SocketTrackingTest
{
    [Fact]
    public void Tracker_detects_undisposed_listener()
    {
        using var listener = Track(new TcpListener(IPAddress.Loopback, 0));
        listener.Start();
        FluentActions.Invoking(AssertNoOpenSockets).Should().Throw<Xunit.Sdk.XunitException>();
        listener.Dispose();
        AssertNoOpenSockets();
    }

    [Fact]
    public void Tracker_detects_undisposed_client()
    {
        using var client = Track(new TcpClient());
        FluentActions.Invoking(AssertNoOpenSockets).Should().Throw<Xunit.Sdk.XunitException>();
        client.Dispose();
        AssertNoOpenSockets();
    }
}
