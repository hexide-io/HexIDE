using System.Collections.Concurrent;
using System.Net.Sockets;
using Microsoft.Win32.SafeHandles;

namespace HexIDE.TwinBasic.Tests;

public abstract class SocketTrackingTest : IDisposable
{
    private static readonly ConcurrentDictionary<object, SocketTrackingTest> tests = new();
    private readonly List<SafeSocketHandle> handles = [];
    internal Action? AfterAcquire { get; set; }

    protected SocketTrackingTest() => tests[TestContext.Current.Test!] = this;
    internal static SocketTrackingTest Current => tests[TestContext.Current.Test!];

    internal static TcpListener Track(TcpListener listener)
    {
        Current.handles.Add(listener.Server.SafeHandle);
        return listener;
    }

    internal static TcpClient Track(TcpClient client)
    {
        Current.handles.Add(client.Client.SafeHandle);
        return client;
    }

    internal void AssertNoOpenSockets() => handles.Should().OnlyContain(handle => handle.IsClosed,
        "every test socket must be disposed, including when peer creation fails");

    public void Dispose()
    {
        tests.TryRemove(TestContext.Current.Test!, out _);
        AssertNoOpenSockets();
    }
}

// Own resources until the fully constructed peer can take responsibility for them.
internal sealed class TestSocketResources : IDisposable
{
    private readonly Stack<IDisposable> resources = new();
    internal T Own<T>(T resource) where T : IDisposable
    {
        resources.Push(resource);
        if (resource is TcpClient client) SocketTrackingTest.Track(client);
        SocketTrackingTest.Current.AfterAcquire?.Invoke();
        return resource;
    }
    internal void Complete() => resources.Clear();
    public void Dispose()
    {
        if (!resources.TryPop(out var resource)) return;
        try { resource.Dispose(); }
        finally { Dispose(); }
    }
}
