namespace HexIDE.TwinBasic.Tests;

public sealed class CompilerRootSessionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Initialization_uses_decoded_messages_and_retains_unknown_notifications()
    {
        using var peer = await CompilerSessionTestPeer.Create(initialize: false);
        var pending = peer.Initialize();
        (await peer.Request()).Message.Should().BeOfType<InMemoryCompilerSessionCodec.Preinitialize>();
        await peer.Reply(-1, 987, [0xFF, 0x80]);
        await peer.Reply(0, value: new CompilerProcessInfo(123));
        (await peer.Request()).Message.Should().BeOfType<InMemoryCompilerSessionCodec.Initialize>();
        await peer.Reply(1, value: new CompilerChannelMetadata(1001, 1002, 1003, 1004, 1005, "instance"));
        using var root = await pending;
        root.Connection.ProcessId.Should().Be(123);
        root.Connection.FileSystemPort.Should().Be(1001);
        root.Connection.DebuggerPort.Should().Be(1002);
        root.Connection.LanguagePort.Should().Be(1003);
        root.Connection.FormImagePort.Should().Be(1004);
        root.Connection.LiveBackupPort.Should().Be(1005);
        root.Connection.ServerId.Should().Be("instance");
        var notification = root.Connection.InitializationNotifications.Should().ContainSingle().Which;
        notification.Code.Should().Be(987);
        notification.Payload.Should().Equal([0xFF, 0x80]);
    }

    [Theory]
    [InlineData(1, 0, "Unexpected compiler initialization reply id.")]
    [InlineData(0, -1, "Compiler initialization failed with status -1.")]
    [InlineData(0, 1, "Compiler initialization failed with status 1.")]
    public async Task Invalid_reply_fails_before_reading_its_data(int id, int status, string message)
    {
        using var peer = await CompilerSessionTestPeer.Create(initialize: false);
        var pending = peer.Initialize();
        await peer.Request();
        await peer.Reply(id, status);
        await FluentActions.Awaiting(() => pending).Should().ThrowExactlyAsync<InvalidDataException>().WithMessage(message);
        await FluentActions.Awaiting(() => peer.Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Notification_budget_is_enforced_before_decoding_notification_data()
    {
        using var peer = await CompilerSessionTestPeer.Create(initialize: false);
        var pending = peer.Initialize(new(MaximumNotifications: 0));
        await peer.Request();
        await peer.Reply(-1, 243);
        await FluentActions.Awaiting(() => pending).Should().ThrowExactlyAsync<InvalidDataException>()
            .WithMessage("Compiler handshake exceeds the notification budget.");
    }

    [Fact]
    public async Task Receive_budget_spans_both_phases_and_notifications()
    {
        using var peer = await CompilerSessionTestPeer.Create(initialize: false);
        var pending = peer.Initialize(new(MaximumReceivedBytes: 32));
        await peer.Request();
        for (var i = 0; i < 16; i++) await peer.Reply(-1, 123);
        await peer.Reply(0, value: new CompilerProcessInfo(123));
        await peer.Request();
        for (var i = 0; i < 16; i++) await peer.Reply(-1, 123);
        await FluentActions.Awaiting(() => pending).Should().ThrowExactlyAsync<InvalidDataException>()
            .WithMessage("Compiler handshake exceeds the receive byte budget.");
    }

    [Fact]
    public async Task Cancellation_preserves_token_and_closes_transport()
    {
        using var peer = await CompilerSessionTestPeer.Create(initialize: false);
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var pending = peer.Initialize(token: cancel.Token);
        await peer.Request();
        cancel.Cancel();
        var error = await FluentActions.Awaiting(() => pending).Should().ThrowExactlyAsync<OperationCanceledException>();
        error.Which.CancellationToken.Should().Be(cancel.Token);
        await FluentActions.Awaiting(() => peer.Transport.ReceiveAsync(Token)).Should().ThrowExactlyAsync<ObjectDisposedException>();
    }

    [Fact]
    public async Task Deadline_covers_time_between_decoded_notifications()
    {
        using var peer = await CompilerSessionTestPeer.Create(initialize: false);
        var pending = peer.Initialize(new(Timeout: TimeSpan.FromMilliseconds(250)));
        await peer.Request();
        await peer.Reply(-1, 123);
        await FluentActions.Awaiting(() => pending).Should().ThrowExactlyAsync<TimeoutException>()
            .WithMessage("Compiler root initialization timed out.");
    }
}
