using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Tests.LspClient;

public class CodeLensSessionTests
{
    private const string Uri = "file:///Module1.bas";
    private static CodeLens Lens(int line, string? title = null) => new(
        new LspRange(new Position(line, 0), new Position(line, 4)),
        title is null ? null : new Command(title, "test.run"));

    private static ILspClient Client(CodeLens[] lenses)
    {
        var client = Substitute.For<ILspClient>();
        client.RequestCodeLensesAsync(Uri, Arg.Any<CancellationToken>()).Returns(lenses);
        client.ResolveCodeLensAsync(Arg.Any<CodeLens>(), Arg.Any<CancellationToken>())
            .Returns(ci => ci.Arg<CodeLens>() with { Command = new Command("1 reference", "test.run") });
        return client;
    }

    [Fact]
    public async Task ServerRefreshFetchesAgainAndDisposeUnsubscribes()
    {
        var client = Client([Lens(0)]);
        var session = new CodeLensSession(client, Uri);
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        client.RequestCodeLensesAsync(Uri, Arg.Any<CancellationToken>()).Returns([Lens(1)]);
        client.CodeLensRefreshRequested += Raise.Event<EventHandler>(client, EventArgs.Empty);
        // The substituted request completes synchronously, so the invalidation has already been applied.
        session.Lenses.Should().ContainSingle().Which.Range.Start.Line.Should().Be(1);
        session.Dispose();
        client.CodeLensRefreshRequested += Raise.Event<EventHandler>(client, EventArgs.Empty);
        await client.Received(2).RequestCodeLensesAsync(Uri, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task FiftyMembersWithTenInViewResolveOnlyTen()
    {
        var client = Client(Enumerable.Range(0, 50).Select(i => Lens(i)).ToArray());
        using var session = new CodeLensSession(client, Uri);
        await session.SetViewportAsync(10, 19);
        await session.RefreshAsync(TestContext.Current.CancellationToken);

        session.Lenses.Should().HaveCount(50);
        await client.Received(10).ResolveCodeLensAsync(Arg.Any<CodeLens>(), Arg.Any<CancellationToken>());
        session.Lenses.Count(l => l.Command is not null).Should().Be(10);
        await session.SetViewportAsync(15, 24);
        await client.Received(15).ResolveCodeLensAsync(Arg.Any<CodeLens>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ScrollingOutCancelsAndDiscardsALateResolve()
    {
        var lens = Lens(0);
        var client = Client([lens]);
        var reply = new TaskCompletionSource<CodeLens?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken resolving = default;
        client.ResolveCodeLensAsync(lens, Arg.Any<CancellationToken>()).Returns(ci =>
        {
            resolving = ci.Arg<CancellationToken>();
            return reply.Task;
        });
        using var session = new CodeLensSession(client, Uri);
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        var visible = session.SetViewportAsync(0, 0);
        await session.SetViewportAsync(1, 10);
        resolving.IsCancellationRequested.Should().BeTrue();
        reply.SetResult(Lens(0, "stale"));
        await visible;
        session.Lenses.Single().Command.Should().BeNull();
    }

    [Fact]
    public async Task RefreshDiscardsOldLensesAndResolvesTheNewVisibleSet()
    {
        var client = Client([Lens(0, "old")]);
        using var session = new CodeLensSession(client, Uri);
        await session.SetViewportAsync(0, 2);
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        client.RequestCodeLensesAsync(Uri, Arg.Any<CancellationToken>()).Returns([Lens(1)]);
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        session.Lenses.Should().ContainSingle().Which.Range.Start.Line.Should().Be(1);
        session.Lenses[0].Command!.Title.Should().Be("1 reference");
    }

    [Fact]
    public async Task OlderDiscoveryCannotReplaceANewerRefresh()
    {
        var client = Client([]);
        var old = new TaskCompletionSource<CodeLens[]>(TaskCreationOptions.RunContinuationsAsynchronously);
        client.RequestCodeLensesAsync(Uri, Arg.Any<CancellationToken>()).Returns(old.Task, Task.FromResult(new[] { Lens(5) }));
        using var session = new CodeLensSession(client, Uri);
        var first = session.RefreshAsync(TestContext.Current.CancellationToken);
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        old.SetResult([Lens(1)]);
        await first;
        session.Lenses.Should().ContainSingle().Which.Range.Start.Line.Should().Be(5);
    }

    [Fact]
    public async Task DisposeCancelsPendingWorkAndClearsLenses()
    {
        var client = Client([Lens(0)]);
        var reply = new TaskCompletionSource<CodeLens?>(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken resolving = default;
        client.ResolveCodeLensAsync(Arg.Any<CodeLens>(), Arg.Any<CancellationToken>()).Returns(ci =>
        {
            resolving = ci.Arg<CancellationToken>();
            return reply.Task;
        });
        var session = new CodeLensSession(client, Uri);
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        var visible = session.SetViewportAsync(0, 0);
        session.Dispose();
        resolving.IsCancellationRequested.Should().BeTrue();
        reply.SetResult(Lens(0, "late"));
        await visible;
        session.Lenses.Should().BeEmpty();
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        await client.Received(1).RequestCodeLensesAsync(Uri, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ResolvingNotifiesConsumersWithoutResolvingCompleteLensesAgain()
    {
        var client = Client([Lens(0), Lens(1, "complete")]);
        using var session = new CodeLensSession(client, Uri);
        var changes = 0;
        session.Changed += (_, _) => changes++;
        await session.RefreshAsync(TestContext.Current.CancellationToken);
        await session.SetViewportAsync(0, 1);
        changes.Should().BeGreaterThanOrEqualTo(2, "discovery and resolution both change the displayed set");
        await session.SetViewportAsync(0, 1);
        await client.Received(1).ResolveCodeLensAsync(Arg.Any<CodeLens>(), Arg.Any<CancellationToken>());
        session.Lenses.Select(l => l.Command!.Title).Should().Equal("1 reference", "complete");
    }
}
