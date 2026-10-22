using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerProjectPreparationTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Stages_exact_bytes_handles_reverse_requests_and_waits_for_acknowledgements()
    {
        using var peer = await Peer.Create();
        var original = await File.ReadAllBytesAsync(peer.Source, Token);
        var package = new TrackingStream([0, 0xFF, 0x80, 42]);
        var provider = new Provider((id, _) =>
        {
            Assert.Equal("包/opaque-id", id);
            return ValueTask.FromResult<Stream?>(package);
        });
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, provider, cancellationToken: Token);
        var load = await peer.Request();
        Assert.Equal(1, load.Command);
        Assert.Equal(2, load.Id);
        var staged = Encoding.UTF8.GetString(load.Payload);
        Assert.NotEqual(peer.Source, staged);
        Assert.Equal(original, await File.ReadAllBytesAsync(staged, Token));
        await peer.Reply(-1, 243, [0, 0, 0, 0, .. "{\"unknown\":true}"u8]);
        var settings = await peer.Request();
        Assert.Equal(24, settings.Command);
        Assert.Equal(new byte[4], settings.Payload);
        await peer.Reply(-1, 255, Encoding.UTF8.GetBytes("包/opaque-id"));
        var provided = await peer.Request();
        Assert.Equal(4, provided.Command);
        var nameLength = BinaryPrimitives.ReadInt32LittleEndian(provided.Payload);
        Assert.Equal(Encoding.UTF8.GetByteCount("包/opaque-id"), nameLength);
        Assert.Equal("包/opaque-id", Encoding.UTF8.GetString(provided.Payload, 4, nameLength));
        Assert.Equal("AP+AKg==", Encoding.ASCII.GetString(provided.Payload, 4 + nameLength, provided.Payload.Length - 4 - nameLength));
        await package.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        Assert.True(package.WasDisposed);
        await peer.Reply(-1, 123, [9]); // Unknown notification: preserve, never execute.
        await peer.Reply(load.Id, 0, [10]); // Load can complete before reverse acknowledgements.
        await peer.Reply(provided.Id, 0, []);
        Assert.False(prepare.IsCompleted);
        await peer.Reply(settings.Id, 0, []);
        var ready = await peer.Request();
        Assert.Equal(3, ready.Command);
        Assert.Empty(ready.Payload);
        await peer.Reply(ready.Id, 0, [11]);
        using var result = await prepare;
        Assert.Equal(new byte[] { 10 }, result.LoadReply);
        Assert.Equal(new byte[] { 11 }, result.ReadyReply);
        Assert.Equal(new[] { 243, 255, 123 }, result.Notifications.Select(x => x.Code));
        Assert.Equal(original, await File.ReadAllBytesAsync(peer.Source, Token));
        Assert.Single(Directory.GetFileSystemEntries(peer.SourceDirectory));
        result.Dispose();
        result.Dispose();
        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)));
        Assert.Equal(original, await File.ReadAllBytesAsync(peer.Source, Token));
    }

    [Theory]
    [InlineData(-4)]
    [InlineData(-5)]
    [InlineData(-6)]
    [InlineData(-3)]
    public async Task Compiler_rejection_removes_stage_closes_root_and_does_not_restart(int status)
    {
        using var peer = await Peer.Create();
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing, cancellationToken: Token);
        var load = await peer.Request();
        var staged = Encoding.UTF8.GetString(load.Payload);
        await peer.Reply(load.Id, status, "private compiler detail"u8.ToArray());
        var error = await Assert.ThrowsAsync<CompilerProjectStatusException>(() => prepare);
        Assert.Equal(status, error.StatusCode);
        Assert.DoesNotContain("private compiler detail", error.ToString());
        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => peer.Root.Transport.ReceiveAsync(Token));
        Assert.True(File.Exists(peer.Source));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("provider-fault")]
    [InlineData("oversize")]
    [InlineData("empty")]
    [InlineData("bad-id")]
    [InlineData("bad-settings")]
    [InlineData("wrong-id")]
    [InlineData("duplicate-id")]
    public async Task Invalid_preparation_fails_closed(string fault)
    {
        using var peer = await Peer.Create();
        var provider = new Provider((_, _) => fault switch
        {
            "provider-fault" => throw new IOException("private-provider-secret"),
            "empty" => ValueTask.FromResult<Stream?>(new MemoryStream()),
            "oversize" => ValueTask.FromResult<Stream?>(new MemoryStream(new byte[9])),
            _ => ValueTask.FromResult<Stream?>(null)
        });
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, provider, new(MaximumPackageBytes: 8), Token);
        var load = await peer.Request();
        var staged = Encoding.UTF8.GetString(load.Payload);
        switch (fault)
        {
            case "bad-id": await peer.Reply(-1, 255, [0xFF]); break;
            case "bad-settings": await peer.Reply(-1, 243, [0, 0, 0, 0, .. "{bad}"u8]); break;
            case "wrong-id": await peer.Reply(999, 0, []); break;
            case "duplicate-id":
                await peer.Reply(-1, 243, [0, 0, 0, 0, .. "{}"u8]);
                await peer.Request();
                await peer.Reply(load.Id, 0, []);
                await peer.Reply(load.Id, 0, []);
                break;
            default: await peer.Reply(-1, 255, "opaque"u8.ToArray()); break;
        }
        var error = fault == "provider-fault"
            ? await Assert.ThrowsAsync<IOException>(() => prepare)
            : (Exception)await Assert.ThrowsAsync<InvalidDataException>(() => prepare);
        Assert.DoesNotContain("private-provider-secret", error.ToString());
        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => peer.Root.Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Cancellation_of_late_provider_is_bounded_and_eventual_stream_is_disposed()
    {
        using var peer = await Peer.Create();
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var opened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pending = new TaskCompletionSource<Stream?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new Provider((_, _) => { opened.SetResult(); return new(pending.Task); });
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, provider, cancellationToken: cancel.Token);
        var load = await peer.Request();
        await peer.Reply(-1, 255, "opaque"u8.ToArray());
        await opened.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        cancel.Cancel();
        var error = await Assert.ThrowsAsync<OperationCanceledException>(() => prepare.WaitAsync(TimeSpan.FromSeconds(5), Token));
        Assert.Equal(cancel.Token, error.CancellationToken);
        Assert.False(Directory.Exists(Path.GetDirectoryName(Encoding.UTF8.GetString(load.Payload))));
        var late = new TrackingStream([1]);
        pending.SetResult(late);
        await late.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
    }

    [Fact]
    public async Task Deadline_cleans_stage_when_compiler_does_not_reply()
    {
        using var peer = await Peer.Create();
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing,
            new(Timeout: TimeSpan.FromMilliseconds(300)), Token);
        var load = await peer.Request();
        await Assert.ThrowsAsync<TimeoutException>(() => prepare);
        Assert.False(Directory.Exists(Path.GetDirectoryName(Encoding.UTF8.GetString(load.Payload))));
    }

    [Fact]
    public async Task Package_total_and_notification_budgets_span_the_whole_preparation()
    {
        using var peer = await Peer.Create();
        var provider = new Provider((_, _) => ValueTask.FromResult<Stream?>(new MemoryStream(new byte[4])));
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, provider,
            new(MaximumTotalPackageBytes: 6), Token);
        await peer.Request();
        await peer.Reply(-1, 255, "first"u8.ToArray());
        var packageReply = await peer.Request();
        await peer.Reply(packageReply.Id, 0, []);
        await peer.Reply(-1, 255, "second"u8.ToArray());
        await Assert.ThrowsAsync<InvalidDataException>(() => prepare);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Notification_and_received_byte_budgets_are_enforced(bool notifications)
    {
        using var peer = await Peer.Create();
        var limits = notifications ? new CompilerProjectPreparationLimits(MaximumNotifications: 0) : new(MaximumReceivedBytes: 8);
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing, limits, Token);
        await peer.Request();
        await peer.Reply(-1, 123, [1]);
        await Assert.ThrowsAsync<InvalidDataException>(() => prepare);
    }

    [Fact]
    public async Task Second_preparation_cannot_steal_or_dispose_first_owner()
    {
        using var peer = await Peer.Create();
        var first = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing, cancellationToken: Token);
        var load = await peer.Request();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing, cancellationToken: Token));
        await peer.Reply(load.Id, 0, []);
        var ready = await peer.Request();
        await peer.Reply(ready.Id, 0, []);
        using var result = await first;
        Assert.True(File.Exists(result.StagedProjectPath));
    }

    [Fact]
    public async Task Oversize_source_is_rejected_and_original_is_untouched()
    {
        using var peer = await Peer.Create();
        var before = await File.ReadAllBytesAsync(peer.Source, Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source,
            Provider.Missing, new(MaximumProjectBytes: 1), Token));
        Assert.Equal(before, await File.ReadAllBytesAsync(peer.Source, Token));
        await Assert.ThrowsAsync<ObjectDisposedException>(() => peer.Root.Transport.ReceiveAsync(Token));
    }

    [Fact]
    public async Task Retained_stage_can_be_cleaned_after_external_owner_releases_it()
    {
        using var peer = await Peer.Create();
        var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing, cancellationToken: Token);
        var load = await peer.Request();
        var staged = Encoding.UTF8.GetString(load.Payload);
        using var externalOwner = new FileStream(staged, FileMode.Open, FileAccess.Read, FileShare.None);
        await peer.Reply(load.Id, -4, []);
        if (OperatingSystem.IsWindows())
        {
            var error = await Assert.ThrowsAsync<CompilerProjectCleanupException>(() => prepare);
            Assert.Equal(Path.GetDirectoryName(staged), error.StagingDirectory);
            var causes = Assert.IsType<AggregateException>(error.InnerException);
            Assert.Equal(-4, Assert.IsType<CompilerProjectStatusException>(causes.InnerExceptions[0]).StatusCode);
            externalOwner.Dispose();
            error.RetryCleanup();
            error.RetryCleanup();
        }
        else
        {
            // Unix permits unlinking a file with an open handle.
            await Assert.ThrowsAsync<CompilerProjectStatusException>(() => prepare);
        }
        Assert.False(Directory.Exists(Path.GetDirectoryName(staged)));
    }

    [Fact]
    public async Task File_provider_uses_only_explicit_exact_ids_and_does_not_treat_ids_as_paths()
    {
        using var peer = await Peer.Create();
        var mapping = new Dictionary<string, string> { ["known"] = peer.Source };
        var provider = new CompilerPackageFileProvider(mapping);
        mapping.Clear();
        await using var stream = await provider.OpenReadAsync("known", Token);
        Assert.NotNull(stream);
        Assert.Equal(new FileInfo(peer.Source).Length, stream.Length);
        Assert.Null(await provider.OpenReadAsync("KNOWN", Token));
        Assert.Null(await provider.OpenReadAsync(peer.Source, Token));
        Assert.Null(await provider.OpenReadAsync("../../unknown", Token));
        Assert.Throws<IOException>(() => CompilerProjectPreparation.DeleteStage(peer.SourceDirectory));
        Assert.True(File.Exists(peer.Source));
    }

    private sealed class Provider(Func<string, CancellationToken, ValueTask<Stream?>> open) : ICompilerPackageProvider
    {
        public static Provider Missing { get; } = new((_, _) => ValueTask.FromResult<Stream?>(null));
        public ValueTask<Stream?> OpenReadAsync(string packageId, CancellationToken cancellationToken = default) => open(packageId, cancellationToken);
    }

    private sealed class TrackingStream(byte[] data) : MemoryStream(data)
    {
        public bool WasDisposed { get; private set; }
        public TaskCompletionSource Disposed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        protected override void Dispose(bool disposing) { base.Dispose(disposing); WasDisposed = true; Disposed.TrySetResult(); }
    }

    private sealed class Peer(TcpClient client, TcpClient server) : IDisposable
    {
        public string SourceDirectory { get; } = Path.Combine(Path.GetTempPath(), "hexide-preparation-test-" + Guid.NewGuid().ToString("N"));
        public string Source => Path.Combine(SourceDirectory, "source.twinproj");
        public CompilerRootSession Root { get; private set; } = null!;
        private readonly WebSocket socket = WebSocket.CreateFromStream(server.GetStream(), true, null, Timeout.InfiniteTimeSpan);
        public static async Task<Peer> Create()
        {
            using var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var client = new TcpClient();
            await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, Token);
            var peer = new Peer(client, await listener.AcceptTcpClientAsync(Token));
            Directory.CreateDirectory(peer.SourceDirectory);
            await File.WriteAllBytesAsync(peer.Source, [1, 2, 3, 4], Token);
            var transport = new CompilerWebSocketTransport(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan));
            using var language = JsonDocument.Parse("{\"language\":\"test\"}");
            var init = CompilerRootSession.InitializeAsync(transport, new([], language.RootElement), cancellationToken: Token);
            await peer.Request();
            await peer.Reply(0, 0, Integer(42));
            await peer.Request();
            await peer.Reply(1, 0, [.. Integer(1001), .. Integer(1002), .. Integer(1003), .. Integer(1004), .. Integer(1005), .. "test"u8]);
            peer.Root = await init;
            return peer;
        }
        public async Task<(byte Command, int Id, byte[] Payload)> Request()
        {
            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(5));
            var buffer = new byte[8192];
            var result = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
            Assert.Equal(WebSocketMessageType.Binary, result.MessageType);
            Assert.True(result.EndOfMessage);
            Assert.True(result.Count >= 5);
            return (buffer[0], BinaryPrimitives.ReadInt32LittleEndian(buffer.AsSpan(1)), buffer[5..result.Count]);
        }
        public async Task Reply(int id, int status, byte[] payload) =>
            await socket.SendAsync(((byte[])[.. Integer(id), .. Integer(status), .. payload]).AsMemory(), WebSocketMessageType.Binary, true, Token);
        public void Dispose()
        {
            Root?.Dispose();
            socket.Dispose();
            client.Dispose();
            server.Dispose();
            File.Delete(Source);
            Directory.Delete(SourceDirectory);
        }
    }

    private static byte[] Integer(int value)
    {
        var bytes = new byte[4];
        BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
        return bytes;
    }
}
