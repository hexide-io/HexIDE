using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.TwinBasic.Tests;

public sealed partial class CompilerWireCodecTests
{
    public sealed class CompilerProjectPreparationWireTests : SocketTrackingTest
    {
        [Theory]
        [InlineData(1)]
        [InlineData(2)]
        [InlineData(3)]
        [InlineData(4)]
        public async Task Failed_peer_creation_disposes_sockets(int acquisition)
        {
            var remaining = acquisition;
            AfterAcquire = () =>
            {
                if (--remaining == 0) throw new IOException("Injected peer creation failure");
            };
            await FluentActions.Awaiting(async () => { using var peer = await Peer.Create(); })
                .Should().ThrowExactlyAsync<IOException>().WithMessage("Injected peer creation failure");
            AssertNoOpenSockets();
        }

        private static CancellationToken Token => TestContext.Current.CancellationToken;

        [Fact]
        public async Task Stages_exact_bytes_handles_reverse_requests_and_waits_for_acknowledgements()
        {
            using var peer = await Peer.Create();
            var original = await File.ReadAllBytesAsync(peer.Source, Token);
            var package = new TrackingStream([0, 0xFF, 0x80, 42]);
            var provider = new Provider((id, _) =>
            {
                id.Should().Be("包/opaque-id");
                return ValueTask.FromResult<Stream?>(package);
            });
            var prepare = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, provider, cancellationToken: Token);
            var load = await peer.Request();
            load.Command.Should().Be(1);
            load.Id.Should().Be(2);
            var staged = Encoding.UTF8.GetString(load.Payload);
            staged.Should().NotBe(peer.Source);
            (await File.ReadAllBytesAsync(staged, Token)).Should().Equal(original);
            await peer.Reply(-1, 243, [0, 0, 0, 0, .. "{\"unknown\":true}"u8]);
            var settings = await peer.Request();
            settings.Command.Should().Be(24);
            settings.Payload.Should().Equal(new byte[4]);
            await peer.Reply(-1, 255, Encoding.UTF8.GetBytes("包/opaque-id"));
            var provided = await peer.Request();
            provided.Command.Should().Be(4);
            var nameLength = BinaryPrimitives.ReadInt32LittleEndian(provided.Payload);
            nameLength.Should().Be(Encoding.UTF8.GetByteCount("包/opaque-id"));
            Encoding.UTF8.GetString(provided.Payload, 4, nameLength).Should().Be("包/opaque-id");
            Encoding.ASCII.GetString(provided.Payload, 4 + nameLength, provided.Payload.Length - 4 - nameLength).Should().Be("AP+AKg==");
            await package.Disposed.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            package.WasDisposed.Should().BeTrue();
            await peer.Reply(-1, 123, [9]); // Unknown notification: preserve, never execute.
            await peer.Reply(load.Id, 0, [10]); // Load can complete before reverse acknowledgements.
            await peer.Reply(provided.Id, 0, []);
            prepare.IsCompleted.Should().BeFalse();
            await peer.Reply(settings.Id, 0, []);
            var ready = await peer.Request();
            ready.Command.Should().Be(3);
            ready.Payload.Should().BeEmpty();
            await peer.Reply(ready.Id, 0, [11]);
            using var result = await prepare;
            result.LoadReply.Should().Equal(new byte[] { 10 });
            result.ReadyReply.Should().Equal(new byte[] { 11 });
            result.Notifications.Select(x => x.Code).Should().Equal(new[] { 243, 255, 123 });
            (await File.ReadAllBytesAsync(peer.Source, Token)).Should().Equal(original);
            Directory.GetFileSystemEntries(peer.SourceDirectory).Should().ContainSingle();
            result.Dispose();
            result.Dispose();
            Directory.Exists(Path.GetDirectoryName(staged)).Should().BeFalse();
            (await File.ReadAllBytesAsync(peer.Source, Token)).Should().Equal(original);
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
            var error = (await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<CompilerProjectStatusException>()).Which;
            error.StatusCode.Should().Be(status);
            error.ToString().Should().NotContain("private compiler detail");
            Directory.Exists(Path.GetDirectoryName(staged)).Should().BeFalse();
            await FluentActions.Awaiting(() => peer.Root.Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
            File.Exists(peer.Source).Should().BeTrue();
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
                ? (await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<IOException>()).Which
                : (Exception)(await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<InvalidDataException>()).Which;
            error.ToString().Should().NotContain("private-provider-secret");
            Directory.Exists(Path.GetDirectoryName(staged)).Should().BeFalse();
            await FluentActions.Awaiting(() => peer.Root.Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
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
            var error = (await FluentActions.Awaiting(() => prepare.WaitAsync(TimeSpan.FromSeconds(5), Token)).Should().ThrowExactlyAsync<OperationCanceledException>()).Which;
            error.CancellationToken.Should().Be(cancel.Token);
            Directory.Exists(Path.GetDirectoryName(Encoding.UTF8.GetString(load.Payload))).Should().BeFalse();
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
            await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<TimeoutException>();
            Directory.Exists(Path.GetDirectoryName(Encoding.UTF8.GetString(load.Payload))).Should().BeFalse();
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
            await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<InvalidDataException>();
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
            await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<InvalidDataException>();
        }

        [Fact]
        public async Task Second_preparation_cannot_steal_or_dispose_first_owner()
        {
            using var peer = await Peer.Create();
            var first = CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing, cancellationToken: Token);
            var load = await peer.Request();
            await FluentActions.Awaiting(() => CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source, Provider.Missing, cancellationToken: Token)).Should().ThrowExactlyAsync<InvalidOperationException>();
            await peer.Reply(load.Id, 0, []);
            var ready = await peer.Request();
            await peer.Reply(ready.Id, 0, []);
            using var result = await first;
            File.Exists(result.StagedProjectPath).Should().BeTrue();
        }

        [Fact]
        public async Task Oversize_source_is_rejected_and_original_is_untouched()
        {
            using var peer = await Peer.Create();
            var before = await File.ReadAllBytesAsync(peer.Source, Token);
            await FluentActions.Awaiting(() => CompilerProjectPreparation.PrepareAsync(peer.Root, peer.Source,
                Provider.Missing, new(MaximumProjectBytes: 1), Token)).Should().ThrowExactlyAsync<InvalidDataException>();
            (await File.ReadAllBytesAsync(peer.Source, Token)).Should().Equal(before);
            await FluentActions.Awaiting(() => peer.Root.Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
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
                var error = (await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<CompilerProjectCleanupException>()).Which;
                error.StagingDirectory.Should().Be(Path.GetDirectoryName(staged));
                var causes = error.InnerException.Should().BeOfType<AggregateException>().Which;
                (causes.InnerExceptions[0]).Should().BeOfType<CompilerProjectStatusException>().Which.StatusCode.Should().Be(-4);
                externalOwner.Dispose();
                error.RetryCleanup();
                error.RetryCleanup();
            }
            else
            {
                // Unix permits unlinking a file with an open handle.
                await FluentActions.Awaiting(() => prepare).Should().ThrowExactlyAsync<CompilerProjectStatusException>();
            }
            Directory.Exists(Path.GetDirectoryName(staged)).Should().BeFalse();
        }

        [Fact]
        public async Task File_provider_uses_only_explicit_exact_ids_and_does_not_treat_ids_as_paths()
        {
            using var peer = await Peer.Create();
            var mapping = new Dictionary<string, string> { ["known"] = peer.Source };
            var provider = new CompilerPackageFileProvider(mapping);
            mapping.Clear();
            await using var stream = await provider.OpenReadAsync("known", Token);
            stream.Should().NotBeNull();
            stream.Length.Should().Be(new FileInfo(peer.Source).Length);
            (await provider.OpenReadAsync("KNOWN", Token)).Should().BeNull();
            (await provider.OpenReadAsync(peer.Source, Token)).Should().BeNull();
            (await provider.OpenReadAsync("../../unknown", Token)).Should().BeNull();
            FluentActions.Invoking(() => CompilerProjectPreparation.DeleteStage(peer.SourceDirectory)).Should().ThrowExactly<IOException>();
            File.Exists(peer.Source).Should().BeTrue();
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
                using var resources = new TestSocketResources();
                using var listener = SocketTrackingTest.Track(new TcpListener(IPAddress.Loopback, 0));
                listener.Start();
                var client = resources.Own(new TcpClient());
                await client.ConnectAsync((IPEndPoint)listener.LocalEndpoint, Token);
                var server = resources.Own(await listener.AcceptTcpClientAsync(Token));
                var peer = resources.Own(new Peer(client, server));
                Directory.CreateDirectory(peer.SourceDirectory);
                await File.WriteAllBytesAsync(peer.Source, [1, 2, 3, 4], Token);
                var transport = resources.Own(new CompilerWebSocketTransport(WebSocket.CreateFromStream(client.GetStream(), false, null, Timeout.InfiniteTimeSpan)));
                using var language = JsonDocument.Parse("{\"language\":\"test\"}");
                var init = CompilerRootSession.InitializeAsync(transport, new([], CompilerWireCodec.ReadLanguageData(language.RootElement)), cancellationToken: Token);
                await peer.Request();
                await peer.Reply(0, 0, Integer(42));
                await peer.Request();
                await peer.Reply(1, 0, [.. Integer(1001), .. Integer(1002), .. Integer(1003), .. Integer(1004), .. Integer(1005), .. "test"u8]);
                peer.Root = await init;
                resources.Complete();
                return peer;
            }
            public async Task<(byte Command, int Id, byte[] Payload)> Request()
            {
                using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
                deadline.CancelAfter(TimeSpan.FromSeconds(5));
                var buffer = new byte[8192];
                var result = await socket.ReceiveAsync(buffer.AsMemory(), deadline.Token);
                result.MessageType.Should().Be(WebSocketMessageType.Binary);
                result.EndOfMessage.Should().BeTrue();
                (result.Count >= 5).Should().BeTrue();
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
                if (File.Exists(Source)) File.Delete(Source);
                if (Directory.Exists(SourceDirectory)) Directory.Delete(SourceDirectory);
            }
        }

        private static byte[] Integer(int value)
        {
            var bytes = new byte[4];
            BinaryPrimitives.WriteInt32LittleEndian(bytes, value);
            return bytes;
        }
    }

}
