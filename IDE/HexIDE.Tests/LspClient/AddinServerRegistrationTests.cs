using System.IO.Pipes;
using Avalonia.Headless.XUnit;
using System.Text.Json;
using HexIDE.Addins;
using HexIDE.Conversations;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using Microsoft.Extensions.Logging;
using StreamJsonRpc;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// A language server an add-in contributes (#457, #2760): added to the router while the IDE runs, checked as an entry
/// of the configuration file is, gated before it launches, and given a channel of its own on HexIDE's connection.
/// </summary>
public class AddinServerRegistrationTests : IDisposable
{
    private readonly ILoggerFactory _loggerFactory = LoggerFactory.Create(b => { });

    public void Dispose()
    {
        _loggerFactory.Dispose();
        GC.SuppressFinalize(this);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private LspClientRegistry Router(params LanguageServerRegistration[] registrations) =>
        new(registrations, _loggerFactory.CreateLogger<LspClientRegistry>());

    private static LanguageServerRegistration Markdown(Func<ILspClient> client, string id = "md") =>
        new(id, "Markdown", [".md"], "markdown", client);

    private static (Func<ILspClient> Factory, List<ILspClient> Made) FakeClients()
    {
        var made = new List<ILspClient>();
        return (() =>
        {
            var client = Substitute.For<ILspClient>();
            client.IsRunning.Returns(true);
            made.Add(client);
            return client;
        }, made);
    }

    private LanguageServerContributions Contributions(LspClientRegistry router, params string[] configured) =>
        new(Substitute.For<HexIDE.IDE.ISettingsService>(), router, new LanguageServerRegistrationFactory(_loggerFactory),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            configured.ToHashSet(StringComparer.Ordinal),
            _loggerFactory.CreateLogger<LanguageServerContributions>(), null);

    /// <summary>
    /// A macOS runner's temp directory: a session's own folder under <c>/var/folders/</c>, 49 characters as the CI
    /// runner's is. Its length is what decides a pipe name's budget there (<see cref="PipeNameLimit"/>): 43.
    /// </summary>
    private const string MacTempPath = "/var/folders/zz/zyxvpxvq6csfxvn_n0000000000000/T/";

    /// <summary>A pipe of a test's own, named to fit every host's budget.</summary>
    internal static string PipeName() => "hexide-addin-" + Guid.NewGuid().ToString("N")[..8];

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    [Trait("Issue", "4307")]
    public async Task ContributedSettingsAreStoredAnsweredIsolatedAndResetInOptions(bool profileDefault, bool stored)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-addin-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            HexIDE.Profiles.ProfileSnapshot? profile = null;
            if (profileDefault)
            {
                File.WriteAllText(Path.Combine(directory, "profile.json"),
                    """{"schemaVersion":1,"id":"settings","settingsDefaults":"defaults.json"}""");
                File.WriteAllText(Path.Combine(directory, "defaults.json"),
                    """{"languageServerSettings":{"one":{"rdcore":{"enabled":true}}}}""");
                profile = new HexIDE.Profiles.ProfileResolver(directory, directory).Resolve(directory);
            }
            var settings = new HexIDE.IDE.SettingsService(Path.Combine(directory, "settings.json"), profile);
            if (stored) settings.SetLanguageServerSettings("one", JsonDocument.Parse("""{"rdcore":{"enabled":false}}""").RootElement);
            var name = PipeName();
            var otherName = PipeName();
            var server = new ProbeServer();
            var otherServer = new ProbeServer();
            await using var pipe = new ProbePipe(name, () => server);
            await using var otherPipe = new ProbePipe(otherName, () => otherServer);
            await using var router = Router();
            var contributions = new LanguageServerContributions(settings, router,
                new LanguageServerRegistrationFactory(_loggerFactory),
                new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
                new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null);
            await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("one", name)), "Same title");
            await using var other = contributions.Register(new AddinLanguageServer(PipeEntry("two", otherName)), "Same title");
            var options = new HexIDE.Addins.AddinOptionsService();
            options.TrackServer("owner", "First server", connection);
            options.TrackServer("another-owner", "Other server", other);
            using var page = new HexIDE.Forms.ViewModels.Options.AddinOptionsPageViewModel(
                Substitute.For<IAddinRegistry>(), new AddinInfo("/addin", "Same title", "", "1", "", Id: "owner"),
                settings: settings, servers: options.ServersFor("owner"));
            page.Servers.Should().ContainSingle().Which.Name.Should().Be("First server");
            var row = page.Servers[0];
            row.ResetCommand.CanExecute(null).Should().Be(stored);
            row.IsDefault.Should().Be(profileDefault && !stored);
            row.IsNone.Should().Be(!profileDefault && !stored);
            await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
            await Task.WhenAll(server.Initialized, otherServer.Initialized).WaitAsync(TimeSpan.FromSeconds(30), Token);
            async Task<JsonElement> Ask(ProbePipe target) => await target.Latest.InvokeWithParameterObjectAsync<JsonElement>(
                "workspace/configuration", JsonDocument.Parse("""{"items":[{"section":"rdcore"},{"section":"rdcore.enabled"},{"section":"rdcore.missing"}]}""").RootElement, Token);
            var initial = await Ask(pipe);
            if (stored) initial[0].GetProperty("enabled").GetBoolean().Should().BeFalse();
            else if (profileDefault) initial[1].GetBoolean().Should().BeTrue();
            else initial[0].ValueKind.Should().Be(JsonValueKind.Null);
            using (var value = JsonDocument.Parse("""{"rdcore":{"enabled":false,"written":true}}"""))
                await connection.SetSettingsAsync(value.RootElement, Token);
            var notification = await server.Configuration.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token);
            notification.GetProperty("settings").GetProperty("rdcore").GetProperty("enabled").GetBoolean().Should().BeFalse();
            var answer = await Ask(pipe);
            answer[0].GetProperty("enabled").GetBoolean().Should().BeFalse();
            answer[1].GetBoolean().Should().BeFalse();
            answer[2].ValueKind.Should().Be(JsonValueKind.Null);
            await connection.SetSettingsAsync(connection.GetSettings()!.Value, Token);
            await Ask(pipe);
            server.Configuration.Reader.TryRead(out _).Should().BeFalse("an unchanged object sends no notification");
            connection.GetSettings()!.Value.GetProperty("rdcore").GetProperty("enabled").GetBoolean().Should().BeFalse();
            new HexIDE.IDE.SettingsService(Path.Combine(directory, "settings.json")).HasLanguageServerSettings("one").Should().BeTrue();
            row.Json.ReplaceLineEndings("\n").Should().Be("{\n  \"rdcore\": {\n    \"enabled\": false,\n    \"written\": true\n  }\n}");
            row.ResetCommand.CanExecute(null).Should().BeTrue();
            (await Ask(otherPipe))[0].ValueKind.Should().Be(JsonValueKind.Null);
            otherServer.Configuration.Reader.TryRead(out _).Should().BeFalse();
            await row.ResetCommand.ExecuteAsync(null);
            var reset = (await server.Configuration.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token)).GetProperty("settings");
            if (profileDefault) reset.GetProperty("rdcore").GetProperty("enabled").GetBoolean().Should().BeTrue();
            else reset.EnumerateObject().Should().BeEmpty();
            settings.HasLanguageServerSettings("one").Should().BeFalse();
            row.ResetCommand.CanExecute(null).Should().BeFalse();
            var fallback = await Ask(pipe);
            if (profileDefault) fallback[1].GetBoolean().Should().BeTrue();
            else fallback[0].ValueKind.Should().Be(JsonValueKind.Null);
            if (profileDefault)
            {
                // Reset notifies even when removing the user object leaves the resolved value unchanged.
                await connection.SetSettingsAsync(connection.GetSettings()!.Value, Token);
                await Ask(pipe);
                server.Configuration.Reader.TryRead(out _).Should().BeFalse();
                await row.ResetCommand.ExecuteAsync(null);
                (await server.Configuration.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token))
                    .GetProperty("settings").GetProperty("rdcore").GetProperty("enabled").GetBoolean().Should().BeTrue();
            }
        }
        finally { Directory.Delete(directory, true); }
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("null")]
    [InlineData("4")]
    [Trait("Issue", "4307")]
    public async Task InvalidOrDisposedSettingsWritesNeverPersistOrNotify(string invalid)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-addin-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "settings.json");
            var settings = new HexIDE.IDE.SettingsService(path);
            var server = new ProbeServer();
            var name = PipeName();
            await using var pipe = new ProbePipe(name, () => server);
            await using var router = Router();
            var contributions = new LanguageServerContributions(settings, router,
                new LanguageServerRegistrationFactory(_loggerFactory),
                new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
                new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null);
            var connection = contributions.Register(new AddinLanguageServer(PipeEntry("one", name)), "Owner");
            await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
            await server.Initialized.WaitAsync(TimeSpan.FromSeconds(30), Token);
            var write = () => connection.SetSettingsAsync(JsonDocument.Parse(invalid).RootElement, Token);
            await write.Should().ThrowAsync<ArgumentException>();
            // The reply is a wire barrier: no configuration notification reached the server before it.
            await pipe.Latest.InvokeWithParameterObjectAsync<JsonElement>("workspace/configuration",
                JsonDocument.Parse("""{"items":[{}]}""").RootElement, Token);
            server.Configuration.Reader.TryRead(out _).Should().BeFalse();
            File.Exists(path).Should().BeFalse();
            await connection.DisposeAsync();
            var disposed = () => connection.SetSettingsAsync(JsonDocument.Parse("{}").RootElement, Token);
            await disposed.Should().ThrowAsync<ObjectDisposedException>();
            File.Exists(path).Should().BeFalse();
        }
        finally { Directory.Delete(directory, true); }
    }

    [Fact]
    public void TheseTestsPipeNames_FitTheBudgetTheGuardAllowsOnMacOS_AndHere()
    {
        var name = PipeName();

        name.Length.Should().BeLessThanOrEqualTo(PipeNameLimit.UnixMaxFor(MacTempPath),
            "on macOS a named pipe is a socket under a long temp path, and the guard refuses a name past its budget");
        PipeNameLimit.IsTooLong(name).Should().BeFalse();
    }

    [Fact]
    [Trait("Issue", "4307")]
    public async Task TheAddinsOwnEntrySettingsAreTheLastFallback()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-addin-settings-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var settings = new HexIDE.IDE.SettingsService(Path.Combine(directory, "settings.json"));
            var name = PipeName();
            var server = new ProbeServer();
            await using var pipe = new ProbePipe(name, () => server);
            await using var router = Router();
            var contributions = new LanguageServerContributions(settings, router,
                new LanguageServerRegistrationFactory(_loggerFactory),
                new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
                new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null);
            var entry = PipeEntry("one", name);
            entry.Settings = JsonDocument.Parse("""{"rdcore":{"fromEntry":true}}""").RootElement.Clone();
            await using var connection = contributions.Register(new AddinLanguageServer(entry), "Owner");
            connection.GetSettings()!.Value.GetProperty("rdcore").GetProperty("fromEntry").GetBoolean().Should().BeTrue();
            await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
            await server.Initialized.WaitAsync(TimeSpan.FromSeconds(30), Token);
            async Task<JsonElement> Ask() => await pipe.Latest.InvokeWithParameterObjectAsync<JsonElement>(
                "workspace/configuration", JsonDocument.Parse("""{"items":[{"section":"rdcore"}]}""").RootElement, Token);
            (await Ask())[0].GetProperty("fromEntry").GetBoolean().Should().BeTrue("the entry's object answers when the user has none");
            using (var value = JsonDocument.Parse("""{"rdcore":{"fromUser":true}}"""))
                await connection.SetSettingsAsync(value.RootElement, Token);
            (await server.Configuration.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token))
                .GetProperty("settings").GetProperty("rdcore").TryGetProperty("fromUser", out _).Should().BeTrue();
            (await Ask())[0].TryGetProperty("fromEntry", out _).Should().BeFalse("the user's object replaces the entry's whole");
            await connection.ResetSettingsAsync();
            (await server.Configuration.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token))
                .GetProperty("settings").GetProperty("rdcore").GetProperty("fromEntry").GetBoolean().Should().BeTrue("a reset falls back to the entry");
            (await Ask())[0].GetProperty("fromEntry").GetBoolean().Should().BeTrue();
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); } catch (IOException) { }
        }
    }

    private static LanguageServerEntry PipeEntry(string id, string pipeName) => new()
    {
        Id = id,
        DisplayName = "Probe",
        Extensions = [".md"],
        LanguageId = "markdown",
        Transport = "pipe",
        PipeName = pipeName,
    };

    // ── the router ──────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task AServerAddedWhileDocumentsAreOpen_IsOpenedOnTheOnesItClaims()
    {
        // An add-in that waited for consent registers after the window opened, with the startup project's documents open.
        var router = Router();
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await router.OpenDocumentAsync("file:///c:/p/Module1.bas", "Sub Main()\nEnd Sub", Token);
        var (factory, made) = FakeClients();

        var added = router.Add(Markdown(factory));
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var client = made.Should().ContainSingle("an open document it claims starts it").Subject;
        await client.Received(1).OpenDocumentAsync("file:///c:/p/README.md", "# hi", Arg.Any<CancellationToken>());
        await client.DidNotReceive().OpenDocumentAsync("file:///c:/p/Module1.bas", Arg.Any<string>(), Arg.Any<CancellationToken>());
        router.Connections.Should().ContainSingle(c => c.Id == "md");
    }

    [Fact]
    public async Task AServerTakenOutAndAddedAgain_KeepsTheNewOnesDiagnostics_HoweverLongTheOldOneTakesToStop()
    {
        // An add-in reloaded: its server's id is free again before the old one has finished shutting down.
        const string uri = "file:///c:/p/README.md";
        var router = Router();
        await router.OpenDocumentAsync(uri, "# hi", Token);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (oldFactory, oldMade) = FakeClients();
        var old = router.Add(Markdown(() =>
        {
            var client = oldFactory();
            client.StopAsync().Returns(stopping.Task);
            return client;
        }));
        await old.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var oldClient = oldMade.Single();
        oldClient.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(oldClient,
            new PublishDiagnosticsParams(uri, [new(new(new(0, 0), new(0, 1)), "old", DiagnosticSeverity.Warning)]));
        router.Diagnostics.Should().ContainSingle(d => d.Diagnostic.Message == "old");

        var removing = old.DisposeAsync().AsTask();
        router.Diagnostics.Should().BeEmpty("what a server said goes with it");
        var (newFactory, newMade) = FakeClients();
        var replacement = router.Add(Markdown(newFactory));
        await replacement.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var newClient = newMade.Single();
        newClient.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(newClient,
            new PublishDiagnosticsParams(uri, [new(new(new(0, 0), new(0, 1)), "new", DiagnosticSeverity.Warning)]));

        stopping.SetResult();
        await removing.WaitAsync(TimeSpan.FromSeconds(30), Token);

        router.Diagnostics.Should().ContainSingle().Which.Diagnostic.Message.Should().Be("new",
            "the old server's removal withdraws what it said, not what the server now under its id says");
        await replacement.DisposeAsync();
    }

    [Fact]
    public async Task WhatAServerSaidJustBeforeItWasTakenOut_IsNotDeliveredAfterIt()
    {
        const string uri = "file:///c:/p/README.md";
        var router = Router();
        await router.OpenDocumentAsync(uri, "# hi", Token);
        var (factory, made) = FakeClients();
        var old = router.Add(Markdown(factory));
        await old.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var client = made.Single();
        static PublishDiagnosticsParams Saying(string message) =>
            new(uri, [new(new(new(0, 0), new(0, 1)), message, DiagnosticSeverity.Warning)]);

        // While its first publication is being delivered, the server says something more and is then taken out: the
        // second one is queued behind the delivery, so it arrived before the removal and reaches the registry after it.
        var events = new List<PublishDiagnosticsParams>();
        Task? removing = null;
        router.DiagnosticsPublished += (_, p) =>
        {
            events.Add(p);
            if (removing is not null) return;
            client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(client, Saying("late"));
            removing = old.DisposeAsync().AsTask();
        };
        client.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(client, Saying("first"));
        await removing!.WaitAsync(TimeSpan.FromSeconds(30), Token);

        router.Diagnostics.Should().BeEmpty("a server taken out has nothing left on the books, under any owner");
        events.Select(p => p.Diagnostics.Select(d => d.Message).ToArray()).Should().BeEquivalentTo(
            new[] { new[] { "first" }, Array.Empty<string>() }, options => options.WithStrictOrdering(),
            "what it said, then its withdrawal, in that order, and nothing after");
    }

    [Fact]
    public async Task AClientMadeAfterItsServerWasTakenOut_IsNeverHeard_AndLetGo()
    {
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, made) = FakeClients();
        var router = Router();
        // A gate deaf to its token: the start goes on to make its client after the removal has begun.
        var added = router.Add(Markdown(factory) with
        {
            BeforeLaunch = async _ => { entered.TrySetResult(); await release.Task; },
        });
        var opening = router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var removing = added.DisposeAsync().AsTask();
        release.SetResult();
        await removing.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await opening.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var late = made.Should().ContainSingle().Subject;
        await late.DidNotReceive().StartAsync(Arg.Any<CancellationToken>());
        await late.Received(1).DisposeAsync();
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task AServerTakenOutAndAddedAgain_KeepsTheNewOnesConfigurationProblems_HoweverLongTheOldOneTakesToStop()
    {
        var router = Router();
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (oldFactory, _) = FakeClients();
        router.ReportContributionProblems("md", [new LanguageServerConfigProblem("md", "the old one's warning", false)]);
        var old = router.Add(Markdown(() =>
        {
            var client = oldFactory();
            client.StopAsync().Returns(stopping.Task);
            return client;
        }));
        await old.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var removing = old.DisposeAsync().AsTask();
        router.ConfigurationProblems.Should().NotContain(p => p.Message == "the old one's warning", "they go with the removal");
        // The add-in tries again while the old server stops, and what its new entry is worth a word about is reported
        // before the server is added, as Register reports it.
        router.ReportContributionProblems("md", [new LanguageServerConfigProblem("md", "the new one's warning", false)]);
        var (newFactory, _) = FakeClients();
        await using var replacement = router.Add(Markdown(newFactory));

        stopping.SetResult();
        await removing.WaitAsync(TimeSpan.FromSeconds(30), Token);

        router.ConfigurationProblems.Should().ContainSingle(p => p.EntryId == "md").Which.Message.Should().Be(
            "the new one's warning", "the old server's removal takes its own problems, not its successor's");
    }

    [Fact]
    public async Task AWorkspaceMoveCalledOffMidReopen_LeavesNoRequestWaitingOnAGatedServersDocuments()
    {
        const string first = "file:///c:/p/README.md";
        var dir = "c:/p";
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns(_ => dir);
        var (gatedFactory, gatedMade) = FakeClients();
        var slowStarts = 0;
        var slowStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (slowFactory, _) = FakeClients();
        // The gated server comes first, so the reopen has set it aside before the slow one's start is called off.
        await using var router = new LspClientRegistry(
        [
            Markdown(gatedFactory, "gated") with { Priority = 10, BeforeLaunch = _ => Task.CompletedTask },
            Markdown(() =>
            {
                var client = slowFactory();
                // Its restart only: the one the move's reopen makes.
                if (Interlocked.Increment(ref slowStarts) == 2)
                    client.StartAsync(Arg.Any<CancellationToken>()).Returns(call =>
                    {
                        slowStarted.TrySetResult();
                        return Task.Delay(Timeout.Infinite, call.Arg<CancellationToken>());
                    });
                return client;
            }, "slow"),
        ], _loggerFactory.CreateLogger<LspClientRegistry>(), workspace);
        await router.OpenDocumentAsync(first, "# hi", Token);

        dir = "c:/q";
        using var calledOff = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var moving = router.WorkspaceChangedAsync(calledOff.Token);
        await slowStarted.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await calledOff.CancelAsync();
        await FluentActions.Awaiting(() => moving).Should().ThrowAsync<OperationCanceledException>();

        // The gated server starts again on another document, and a request about the first must not wait for a reopen
        // that was never begun.
        await router.OpenDocumentAsync("file:///c:/q/Other.md", "# again", Token);
        gatedMade.Last().IsRunning.Should().BeTrue();
        await router.RequestHoverAsync(first, new(0, 0), Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    [Fact]
    public async Task AServerAddedWhileItsGateHoldsItsStart_StallsNoOtherDocumentsOpen()
    {
        // The workspace is what makes an open pass through the router's restart gate, which a replay must not hold.
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns("c:/p");
        var (others, _) = FakeClients();
        var router = new LspClientRegistry([new LanguageServerRegistration("txt", "Text", [".txt"], "plaintext", others)],
            _loggerFactory.CreateLogger<LspClientRegistry>(), workspace);
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, _) = FakeClients();

        var added = router.Add(Markdown(factory) with
        {
            BeforeLaunch = async token => { entered.TrySetResult(); await release.Task.WaitAsync(token); },
        });
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // Another language's document opens while the contributed server is still held at its gate.
        await router.OpenDocumentAsync("file:///c:/p/notes.txt", "notes", Token).WaitAsync(TimeSpan.FromSeconds(30), Token);

        release.SetResult();
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    [Fact]
    public async Task AGateHeldWhenTheWorkspaceMoves_StallsNoOtherOpen_AndItsServerIsReopenedOnceItLetsGo()
    {
        var dir = "c:/p";
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns(_ => dir);
        var (others, _) = FakeClients();
        var router = new LspClientRegistry([new LanguageServerRegistration("txt", "Text", [".txt"], "plaintext", others)],
            _loggerFactory.CreateLogger<LspClientRegistry>(), workspace);
        var starts = 0;
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var reopened = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var made = new List<ILspClient>();
        var added = router.Add(Markdown(() =>
        {
            var client = Substitute.For<ILspClient>();
            client.IsRunning.Returns(true);
            if (made.Count == 1)
                client.OpenDocumentAsync("file:///c:/p/README.md", Arg.Any<string>(), Arg.Any<CancellationToken>())
                    .Returns(_ => { reopened.TrySetResult(); return Task.CompletedTask; });
            made.Add(client);
            return client;
        }) with
        {
            // Through the first start; held on the restart's.
            BeforeLaunch = async token =>
            {
                if (Interlocked.Increment(ref starts) == 1) return;
                held.TrySetResult();
                await release.Task.WaitAsync(token);
            },
        });
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);

        dir = "c:/q";
        // The open that finds the workspace moved, and one after it, both of another language.
        await router.OpenDocumentAsync("file:///c:/q/a.txt", "a", Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
        await held.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        await router.OpenDocumentAsync("file:///c:/q/b.txt", "b", Token).WaitAsync(TimeSpan.FromSeconds(30), Token);

        release.SetResult();
        await reopened.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        made.Should().HaveCount(2, "one server before the move and one after");
    }

    [Fact]
    public async Task TakingAServerOutWhileItsGateHangs_CancelsTheGate_AndEnds()
    {
        var router = Router();
        var entered = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, made) = FakeClients();
        var added = router.Add(Markdown(factory) with
        {
            BeforeLaunch = async token => { entered.TrySetResult(token); await Task.Delay(Timeout.Infinite, token); },
        });
        // The editor's open passes no token, so nothing but the removal can end the gate.
        var opening = router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", CancellationToken.None);
        var gate = await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        await added.DisposeAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token);

        gate.IsCancellationRequested.Should().BeTrue();
        await opening.WaitAsync(TimeSpan.FromSeconds(30), Token);
        made.Should().BeEmpty("the start was called off before a client was made");
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task AnOpenCancelledWhileTheGateWaits_LeavesTheServerToStartOnTheNextOpen()
    {
        var router = Router();
        var calls = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, made) = FakeClients();
        var added = router.Add(Markdown(factory) with
        {
            BeforeLaunch = async token =>
            {
                if (Interlocked.Increment(ref calls) > 1) return;
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            },
        });
        // Its replay done, with nothing open: the open below is the only thing that starts it.
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
        using var cancel = new CancellationTokenSource();
        var opening = router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        cancel.Cancel();
        var cancelled = () => opening;
        await cancelled.Should().ThrowAsync<OperationCanceledException>("the caller cancelled, and sees it");
        router.Connections.Should().ContainSingle().Which.State.Should().Be(LanguageConnectionState.NotStarted,
            "called off is not failed, and failed is never retried");

        await router.OpenDocumentAsync("file:///c:/p/Other.md", "# again", Token);
        made.Should().ContainSingle("the next document it claims starts it");
        router.Connections.Should().ContainSingle().Which.State.Should().Be(LanguageConnectionState.Running);
    }

    [Fact]
    public async Task ARefusedRegistrationTriedAgain_ReportsOnce_AndATakenOutServersProblemsGo()
    {
        var router = Router();
        var contributions = Contributions(router, "probe");
        for (var i = 0; i < 3; i++)
        {
            var registering = () => contributions.Register(new AddinLanguageServer(PipeEntry("probe", "unused")), "My Add-in");
            registering.Should().Throw<InvalidOperationException>();
        }
        router.ConfigurationProblems.Count(p => p.EntryId == "probe").Should().Be(1, "only the latest attempt is reported");

        var (factory, _) = FakeClients();
        var added = router.Add(Markdown(factory));
        router.ReportContributionProblems("md", [new LanguageServerConfigProblem("md", "a field is misspelt", false)]);
        await added.DisposeAsync();
        router.ConfigurationProblems.Should().NotContain(p => p.EntryId == "md");
    }

    [Fact]
    public void AnIdAnotherAddinContributed_IsRefusedAsThatAndNotAsTheConfigurations()
    {
        var router = Router();
        var contributions = Contributions(router);
        using var first = contributions.Register(new AddinLanguageServer(PipeEntry("probe", "unused")), "First Add-in");

        var registering = () => contributions.Register(new AddinLanguageServer(PipeEntry("probe", "unused")), "Second Add-in");

        registering.Should().Throw<InvalidOperationException>().WithMessage("*another add-in has contributed already*");
    }

    [Fact]
    public async Task AServerAddedWithNothingOpenThatItClaims_StartsOnTheFirstDocumentItClaims()
    {
        var router = Router();
        var (factory, made) = FakeClients();

        var added = router.Add(Markdown(factory));
        await added.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);
        made.Should().BeEmpty("nothing it claims is open");

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await made.Should().ContainSingle().Subject.Received(1)
            .OpenDocumentAsync("file:///c:/p/README.md", "# hi", Arg.Any<CancellationToken>());
    }

    [Fact]
    public void AnIdTheRouterKnows_CannotBeAddedTwice()
    {
        var router = Router(Markdown(FakeClients().Factory));

        var adding = () => router.Add(Markdown(FakeClients().Factory));

        adding.Should().Throw<InvalidOperationException>();
        router.Connections.Should().ContainSingle();
    }

    [Fact]
    public async Task TakingAContributionOut_StopsItsServer_AndRemovesItsRow()
    {
        var router = Router();
        var (factory, made) = FakeClients();
        var added = router.Add(Markdown(factory));
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);

        await added.DisposeAsync();

        await made.Should().ContainSingle().Subject.Received(1).StopAsync();
        router.Connections.Should().BeEmpty();
        added.Client.Should().BeNull();

        await router.OpenDocumentAsync("file:///c:/p/Other.md", "# again", Token);
        made.Should().ContainSingle("a server taken out is not started again");
    }

    [Fact]
    public async Task TheGateBeforeLaunch_RunsBeforeTheClientIsMade_AndTheStartWaitsForIt()
    {
        var order = new List<string>();
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var (factory, _) = FakeClients();
        var router = Router(Markdown(() => { order.Add("client"); return factory(); }) with
        {
            BeforeLaunch = async token => { order.Add("gate"); await release.Task.WaitAsync(token); order.Add("gate done"); },
        });

        var opening = router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        SpinWait.SpinUntil(() => order.Count > 0, TimeSpan.FromSeconds(30)).Should().BeTrue();
        order.Should().Equal(["gate"], "nothing is made while the gate has not let it through");

        release.SetResult();
        await opening.WaitAsync(TimeSpan.FromSeconds(30), Token);

        order.Should().Equal("gate", "gate done", "client");
    }

    // A clock that moves only when told: the one timer a wait arms, fired by the test.
    private sealed class ManualClock : TimeProvider
    {
        private readonly TaskCompletionSource _armed = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private (TimerCallback Callback, object? State, TimeSpan Due)? _timer;

        public Task Armed => _armed.Task;
        public TimeSpan Due => _timer!.Value.Due;

        public override ITimer CreateTimer(TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            _timer = (callback, state, dueTime);
            _armed.TrySetResult();
            return Substitute.For<ITimer>();
        }

        public void Fire() => _timer!.Value.Callback(_timer.Value.State);
    }

    [Fact]
    public async Task AGateThatNeverFinishes_FailsTheStartAtItsLimit_AndIsToldToStop()
    {
        var clock = new ManualClock();
        var router = Router();
        var contributions = new LanguageServerContributions(Substitute.For<HexIDE.IDE.ISettingsService>(), router, new LanguageServerRegistrationFactory(_loggerFactory),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null, clock: clock);
        var told = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", PipeName()))
        {
            BeforeLaunchTimeout = TimeSpan.FromSeconds(5),
            // Hung, and deaf to its token: the limit must not depend on the gate's cooperation.
            BeforeLaunch = token =>
            {
                token.Register(() => told.TrySetResult());
                return new TaskCompletionSource().Task;
            },
        }, "My Add-in");

        var opening = router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await clock.Armed.WaitAsync(TimeSpan.FromSeconds(30), Token);
        clock.Due.Should().Be(TimeSpan.FromSeconds(5), "the add-in's own limit");
        opening.IsCompleted.Should().BeFalse("the start waits for the gate until then");

        clock.Fire();
        await opening.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var row = router.Connections.Should().ContainSingle().Subject;
        row.State.Should().Be(LanguageConnectionState.Failed);
        row.Attempt!.Steps.Should().Contain(step => step.Detail == "My Add-in did not let 'probe' start within 5 seconds.");
        await told.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
    }

    // Records what is logged, and says when a warning arrives.
    private sealed class RecordingLogger<T> : ILogger<T>
    {
        private readonly TaskCompletionSource<Exception?> _warned = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task<Exception?> Warned => _warned.Task;
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception,
            Func<TState, Exception?, string> formatter)
        {
            if (logLevel == LogLevel.Warning) _warned.TrySetResult(exception);
        }
    }

    [Fact]
    public async Task AGateThatFailsAfterItsLimit_HasItsFailureLogged_NotLost()
    {
        var clock = new ManualClock();
        var logged = new RecordingLogger<LanguageServerContributions>();
        var router = Router();
        var contributions = new LanguageServerContributions(Substitute.For<HexIDE.IDE.ISettingsService>(), router, new LanguageServerRegistrationFactory(_loggerFactory),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), logged, null, clock: clock);
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", PipeName()))
        {
            BeforeLaunchTimeout = TimeSpan.FromSeconds(5),
            BeforeLaunch = _ => gate.Task,
        }, "My Add-in");
        var opening = router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await clock.Armed.WaitAsync(TimeSpan.FromSeconds(30), Token);
        clock.Fire();
        await opening.WaitAsync(TimeSpan.FromSeconds(30), Token);

        gate.SetException(new IOException("the add-in's share went away"));

        (await logged.Warned.WaitAsync(TimeSpan.FromSeconds(30), Token)).Should().BeOfType<IOException>()
            .Which.Message.Should().Be("the add-in's share went away");
    }

    [Fact]
    public async Task AGateThatTimesOutOnItsOwn_IsReportedInItsOwnWords_NotAsHexIdesLimit()
    {
        var router = Router();
        await using var connection = Contributions(router).Register(new AddinLanguageServer(PipeEntry("probe", PipeName()))
        {
            BeforeLaunch = _ => Task.FromException(new TimeoutException("the project server did not answer")),
        }, "My Add-in");

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);

        var row = router.Connections.Should().ContainSingle().Subject;
        row.State.Should().Be(LanguageConnectionState.Failed);
        row.Attempt!.Steps.Should().Contain(step => step.Detail == "the project server did not answer")
            .And.NotContain(step => step.Detail != null && step.Detail.Contains("did not let"));
    }

    [Fact]
    public async Task TheLongestWaitAllowed_StillLetsTheServerStart()
    {
        var pipeName = PipeName();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer { OnStarted = () => initialized.TrySetResult() });
        var (contributions, router) = WiredContributions();
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            BeforeLaunchTimeout = AddinLanguageServer.MaxBeforeLaunchTimeout,
            BeforeLaunch = _ => Task.CompletedTask,
        }, "My Add-in");

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        connection.IsRunning.Should().BeTrue("a wait the registration accepted is one every start can make");
    }

    public static TheoryData<TimeSpan> UnboundedWaits => new(TimeSpan.Zero, TimeSpan.FromSeconds(-1), Timeout.InfiniteTimeSpan,
        TimeSpan.MaxValue, AddinLanguageServer.MaxBeforeLaunchTimeout + TimeSpan.FromMilliseconds(1));

    [Theory]
    [MemberData(nameof(UnboundedWaits))]
    public void AWaitForTheGateThatIsNotPositiveAndFinite_IsRefused(TimeSpan wait)
    {
        var router = Router();

        var registering = () => Contributions(router).Register(new AddinLanguageServer(PipeEntry("probe", "unused"))
        {
            BeforeLaunch = _ => Task.CompletedTask,
            BeforeLaunchTimeout = wait,
        }, "My Add-in");

        registering.Should().Throw<ArgumentException>();
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task AGateThatThrows_FailsTheStart_WithItsMessage()
    {
        var (factory, made) = FakeClients();
        var router = Router(Markdown(factory) with
        {
            BeforeLaunch = _ => throw new InvalidOperationException("the server's project file could not be written"),
        });

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);

        made.Should().BeEmpty("a gate that refuses launches nothing");
        var row = router.Connections.Should().ContainSingle().Subject;
        row.State.Should().Be(LanguageConnectionState.Failed);
        row.Attempt!.Steps.Should().Contain(step => step.Detail == "the server's project file could not be written");
    }

    // ── the contribution ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void AContributedServer_IsListedUnderTheAddinThatContributedIt()
    {
        var router = Router();

        using var connection = Contributions(router).Register(new AddinLanguageServer(PipeEntry("probe", "unused")), "My Add-in");

        router.Connections.Should().ContainSingle().Which.Origin.Should().Be("My Add-in");
        connection.Id.Should().Be("probe");
        connection.IsRunning.Should().BeFalse("it starts on the first document it claims");
    }

    [Fact]
    public void AnIdTheConfigurationNames_KeepsItsConfiguredEntry_AndTheContributionIsNotAdded()
    {
        // The user's file names it, switched off: the user's entry still wins, and the add-in is told.
        var router = Router();

        var registering = () => Contributions(router, "probe").Register(new AddinLanguageServer(PipeEntry("probe", "unused")), "My Add-in");

        registering.Should().Throw<InvalidOperationException>();
        router.Connections.Should().BeEmpty();
        router.ConfigurationProblems.Should().ContainSingle(p => p.EntryId == "probe" && p.EntryRejected && p.Message.Contains("My Add-in"));
    }

    [Fact]
    public void AnEntryTheChecksReject_IsAProblemInTheWindow_AndNotAdded()
    {
        var router = Router();
        var entry = PipeEntry("probe", "unused");
        entry.Extensions = null;

        var registering = () => Contributions(router).Register(new AddinLanguageServer(entry), "My Add-in");

        registering.Should().Throw<ArgumentException>();
        router.Connections.Should().BeEmpty();
        router.ConfigurationProblems.Should().Contain(p => p.EntryId == "probe" && p.EntryRejected);
    }

    [Theory]
    [InlineData("textDocument/", "a namespace of the protocol's own")]
    [InlineData("workspace/x/", "inside a namespace of the protocol's own")]
    [InlineData("$/", "the protocol's own")]
    [InlineData("probe", "no slash")]
    [InlineData("/", "nothing before the slash")]
    public void APrefixThatIsNotTheAddinsToUse_IsRefused(string prefix, string because)
    {
        var router = Router();

        var registering = () => Contributions(router).Register(
            new AddinLanguageServer(PipeEntry("probe", "unused")) { MessagePrefix = prefix }, "My Add-in");

        registering.Should().Throw<ArgumentException>(because);
        router.Connections.Should().BeEmpty();
    }

    [Theory]
    [InlineData("probe")]
    [InlineData(".")]
    [InlineData("probe .")]
    [InlineData("editor.")]
    [InlineData("EDITOR.private.")]
    [InlineData("vscode.")]
    [InlineData("VSCode.private.")]
    [InlineData("hexide.")]
    [InlineData("HexIDE.private.")]
    public void AnInvalidOrReservedCommandPrefix_IsRefused(string prefix)
    {
        var router = Router();
        var registering = () => Contributions(router).Register(
            new AddinLanguageServer(PipeEntry("probe", "unused")) { CommandPrefix = prefix }, "My Add-in");

        registering.Should().Throw<ArgumentException>();
        router.Connections.Should().BeEmpty();
    }

    [Theory]
    [InlineData(null, "probe.run")]
    [InlineData("probe.", "other.run")]
    [InlineData("probe.", "probe.")]
    [InlineData("probe.", "Probe.run")]
    public void ACommandWithoutItsPrefix_IsRefused(string? prefix, string command)
    {
        var router = Router();
        var registering = () => Contributions(router).Register(new AddinLanguageServer(PipeEntry("probe", "unused"))
        {
            CommandPrefix = prefix,
            ClientCommands = new Dictionary<string, AddinClientCommand> { [command] = (_, _) => Task.CompletedTask },
        }, "My Add-in");

        registering.Should().Throw<ArgumentException>();
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task CommandClaims_AreCopiedAtRegistration_AndBelongToThatConnectionOnly()
    {
        var pipeName = PipeName();
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer());
        var (contributions, router) = WiredContributions();
        var commands = new Dictionary<string, AddinClientCommand> { ["probe.run"] = (_, _) => Task.CompletedTask };
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            CommandPrefix = "probe.",
            ClientCommands = commands,
        }, "My Add-in");

        commands.Clear();
        commands["other.run"] = (_, _) => Task.CompletedTask;
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);

        var client = (VBLspClient)connection.Client!;
        client.HasClaimedCommand("probe.run").Should().BeTrue();
        client.HasClaimedCommand("other.run").Should().BeFalse();
        client.HasClaimedCommand("Probe.run").Should().BeFalse();
        await using var other = new VBLspClient(Substitute.For<ILspTransport>(),
            _loggerFactory.CreateLogger<VBLspClient>(), "markdown");
        other.HasClaimedCommand("probe.run").Should().BeFalse();
    }

    /// <summary>
    /// Every method of the Language Server Protocol 3.17, its requests and its notifications, as its meta-model lists
    /// them. <c>initialize</c>, <c>initialized</c>, <c>shutdown</c> and <c>exit</c> have no namespace, and a prefix,
    /// which ends with <c>/</c>, cannot be one of them.
    /// </summary>
    private static readonly string[] Lsp317Methods =
    [
        "$/cancelRequest", "$/logTrace", "$/progress", "$/setTrace",
        "callHierarchy/incomingCalls", "callHierarchy/outgoingCalls",
        "client/registerCapability", "client/unregisterCapability",
        "codeAction/resolve", "codeLens/resolve", "completionItem/resolve", "documentLink/resolve", "inlayHint/resolve",
        "notebookDocument/didChange", "notebookDocument/didClose", "notebookDocument/didOpen", "notebookDocument/didSave",
        "telemetry/event",
        "textDocument/codeAction", "textDocument/codeLens", "textDocument/colorPresentation", "textDocument/completion",
        "textDocument/declaration", "textDocument/definition", "textDocument/diagnostic", "textDocument/didChange",
        "textDocument/didClose", "textDocument/didOpen", "textDocument/didSave", "textDocument/documentColor",
        "textDocument/documentHighlight", "textDocument/documentLink", "textDocument/documentSymbol",
        "textDocument/foldingRange", "textDocument/formatting", "textDocument/hover", "textDocument/implementation",
        "textDocument/inlayHint", "textDocument/inlineValue", "textDocument/linkedEditingRange", "textDocument/moniker",
        "textDocument/onTypeFormatting", "textDocument/prepareCallHierarchy", "textDocument/prepareRename",
        "textDocument/prepareTypeHierarchy", "textDocument/publishDiagnostics", "textDocument/rangeFormatting",
        "textDocument/references", "textDocument/rename", "textDocument/selectionRange", "textDocument/semanticTokens/full",
        "textDocument/semanticTokens/full/delta", "textDocument/semanticTokens/range", "textDocument/signatureHelp",
        "textDocument/typeDefinition", "textDocument/willSave", "textDocument/willSaveWaitUntil",
        "typeHierarchy/subtypes", "typeHierarchy/supertypes",
        "window/logMessage", "window/showDocument", "window/showMessage", "window/showMessageRequest",
        "window/workDoneProgress/cancel", "window/workDoneProgress/create",
        "workspace/applyEdit", "workspace/codeLens/refresh", "workspace/configuration", "workspace/diagnostic",
        "workspace/diagnostic/refresh", "workspace/didChangeConfiguration", "workspace/didChangeWatchedFiles",
        "workspace/didChangeWorkspaceFolders", "workspace/didCreateFiles", "workspace/didDeleteFiles",
        "workspace/didRenameFiles", "workspace/executeCommand", "workspace/inlayHint/refresh",
        "workspace/inlineValue/refresh", "workspace/semanticTokens/refresh", "workspace/symbol", "workspace/willCreateFiles",
        "workspace/willDeleteFiles", "workspace/willRenameFiles", "workspace/workspaceFolders",
        "workspaceSymbol/resolve",
    ];

    private static string NamespaceOf(string method) => method[..(method.IndexOf('/') + 1)];

    [Fact]
    public void TheReservedNamespaces_AreExactlyTheOnesTheProtocolsMethodsAreIn() =>
        LanguageServerContributions.ProtocolNamespaces.Should().BeEquivalentTo(Lsp317Methods.Select(NamespaceOf).Distinct(),
            "a namespace left out lets an add-in answer for HexIDE's client, and one too many refuses an add-in for nothing");

    public static TheoryData<string> ProtocolMethods => new(Lsp317Methods);

    [Theory]
    [MemberData(nameof(ProtocolMethods))]
    public void APrefixInAnyNamespaceOfTheProtocols_IsRefused(string method)
    {
        var router = Router();

        foreach (var prefix in new[] { NamespaceOf(method), NamespaceOf(method) + "mine/" })
        {
            var registering = () => Contributions(router).Register(
                new AddinLanguageServer(PipeEntry("probe", "unused")) { MessagePrefix = prefix }, "My Add-in");
            registering.Should().Throw<ArgumentException>($"{prefix} is where {method} is");
        }
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public void AMethodClaimedOutsideThePrefix_IsRefused()
    {
        var router = Router();

        var registering = () => Contributions(router).Register(new AddinLanguageServer(PipeEntry("probe", "unused"))
        {
            MessagePrefix = "probe/",
            ServerMethods = new Dictionary<string, AddinServerMethod> { ["window/showMessageRequest"] = (_, _) => Task.FromResult<JsonElement?>(null) },
        }, "My Add-in");

        registering.Should().Throw<ArgumentException>();
        router.Connections.Should().BeEmpty();
    }

    [Fact]
    public async Task ASendOutsideThePrefix_IsRefused()
    {
        var router = Router();
        using var connection = Contributions(router).Register(
            new AddinLanguageServer(PipeEntry("probe", "unused")) { MessagePrefix = "probe/" }, "My Add-in");

        var sending = () => connection.RequestAsync("textDocument/hover", null, cancellationToken: Token);

        await sending.Should().ThrowAsync<ArgumentException>();
    }

    // ── #4258 part D: prefixes bound to the publisher's issued namespaces ───────────────────────

    private static AddinLanguageServer Prefixed(string id, string? messagePrefix, string? commandPrefix) =>
        new(PipeEntry(id, "unused"))
        {
            MessagePrefix = messagePrefix,
            CommandPrefix = commandPrefix,
            ClientCommands = commandPrefix is null
                ? new Dictionary<string, AddinClientCommand>()
                : new Dictionary<string, AddinClientCommand> { [commandPrefix + "go"] = (_, _) => Task.CompletedTask },
        };

    [Theory]
    [InlineData("rdcore/", "rdcore.")]
    [InlineData("rdcore/sub/", "rdcore.sub.")]
    [InlineData("rdcore/", null)]
    [InlineData(null, "rdcore.")]
    [InlineData(null, null)]
    public void PrefixesUnderAnIssuedNamespaceAreAccepted(string? messagePrefix, string? commandPrefix)
    {
        var router = Router();
        using var connection = Contributions(router).Register(Prefixed("probe", messagePrefix, commandPrefix), "My Add-in",
            "com.example.addin", ["rdcore."]);
        connection.Id.Should().Be("probe");
        router.ConfigurationProblems.Should().NotContain(p => p.Message.Contains("names no namespaces"));
    }

    [Theory]
    [InlineData("other/", null, "message prefix 'other/'")]
    [InlineData(null, "other.", "command prefix 'other.'")]
    [InlineData("rdcore/", "other.", "command prefix 'other.'")]
    [InlineData("rdcorex/", null, "message prefix 'rdcorex/'")]
    [InlineData(null, "rdcorex.", "command prefix 'rdcorex.'")]
    public void APrefixOutsideTheIssuedNamespacesIsRefusedNamingBoth(string? messagePrefix, string? commandPrefix, string named)
    {
        var router = Router();
        var registering = () => Contributions(router).Register(Prefixed("probe", messagePrefix, commandPrefix), "My Add-in",
            "com.example.addin", ["rdcore.", "tools."]);
        registering.Should().Throw<ArgumentException>()
            .Which.Message.Should().Contain(named).And.Contain("'rdcore.', 'tools.'");
        router.Connections.Should().NotContain(c => c.Id == "probe", "nothing is added");
    }

    [Fact]
    public void ARecordThatIssuesNothingAllowsNoPrefix()
    {
        var registering = () => Contributions(Router()).Register(Prefixed("probe", "rdcore/", null), "My Add-in",
            "com.example.addin", []);
        registering.Should().Throw<ArgumentException>().Which.Message.Should().Contain("(none)");
    }

    [Theory]
    [InlineData("hexide/", null)]
    [InlineData("HexIDE/tools/", null)]
    [InlineData(null, "hexide.")]
    [InlineData(null, "hexide.chat.")]
    public void HexideStaysReservedWhateverTheRecordSays(string? messagePrefix, string? commandPrefix)
    {
        var registering = () => Contributions(Router()).Register(Prefixed("probe", messagePrefix, commandPrefix), "My Add-in",
            "com.example.addin", ["hexide."]);
        registering.Should().Throw<ArgumentException>().Which.Message.Should().Contain("reserved");
    }

    [Fact]
    public void WithoutIssuedNamespacesTodaysRuleAppliesWithOneNoticePerAddin()
    {
        var router = Router();
        var contributions = Contributions(router);
        using var first = contributions.Register(Prefixed("probe", "rdcore/", "rdcore."), "My Add-in", "com.example.addin");
        router.ConfigurationProblems.Should().ContainSingle(p => p.EntryId == "probe" && p.Message.Contains("names no namespaces"))
            .Which.Message.Should().Contain("'rdcore/' and 'rdcore.'").And.Contain("My Add-in");
        router.ConfigurationProblems.Single(p => p.EntryId == "probe").EntryRejected.Should().BeFalse("it is a notice, never a refusal");

        using var second = contributions.Register(Prefixed("probe2", "other/", null), "My Add-in", "com.example.addin");
        router.ConfigurationProblems.Should().NotContain(p => p.EntryId == "probe2", "one notice for each add-in");

        using var another = contributions.Register(Prefixed("probe3", "third/", null), "Another", "com.example.another");
        router.ConfigurationProblems.Should().ContainSingle(p => p.EntryId == "probe3" && p.Message.Contains("names no namespaces"));
    }

    // ── the channel, on the wire ────────────────────────────────────────────────────────────────

    // A server of the add-in's: it echoes a request of its own, takes a notification of its own, and asks the client a
    // question of its own once it is initialised.
    private sealed class ProbeServer
    {
        private readonly TaskCompletionSource<JsonElement> _noted = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource _initialized = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public System.Threading.Channels.Channel<JsonElement> Configuration { get; } =
            System.Threading.Channels.Channel.CreateUnbounded<JsonElement>();

        [JsonRpcMethod("workspace/didChangeConfiguration", UseSingleObjectParameterDeserialization = true)]
        public void OnConfiguration(JsonElement parameters) => Configuration.Writer.TryWrite(parameters.Clone());

        public Task<JsonElement> Noted => _noted.Task;
        public Task Initialized => _initialized.Task;

        [JsonRpcMethod("initialize", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Initialize(JsonElement _) => JsonDocument.Parse(
            """{"capabilities":{"textDocumentSync":{"openClose":true,"change":1},"codeActionProvider":true,"codeLensProvider":{},"executeCommandProvider":{"commands":["probe.declared"]}}}""").RootElement.Clone();

        [JsonRpcMethod("textDocument/codeAction", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Actions(JsonElement _) => JsonDocument.Parse("""[{"title":"Run","command":"probe.run","arguments":[{"n":42},"text"]},{"title":"Declared","command":"probe.declared"}]""").RootElement.Clone();

        [JsonRpcMethod("textDocument/codeLens", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Lenses(JsonElement _) => JsonDocument.Parse("""[{"range":{"start":{"line":0,"character":0},"end":{"line":0,"character":0}},"command":{"title":"Run","command":"probe.run"}}]""").RootElement.Clone();

        public JsonElement? Executed;
        [JsonRpcMethod("workspace/executeCommand", UseSingleObjectParameterDeserialization = true)]
        public JsonElement? Execute(JsonElement parameters) { Executed = parameters.Clone(); return null; }

        [JsonRpcMethod("initialized", UseSingleObjectParameterDeserialization = true)]
        public void OnInitialized(JsonElement _)
        {
            _initialized.TrySetResult();
            OnStarted?.Invoke();
        }

        /// <summary>Told when the client's <c>initialized</c> arrives.</summary>
        public Action? OnStarted { get; init; }

        /// <summary>What <c>probe/hold</c> waits for before it answers.</summary>
        public Func<Task>? Hold { get; init; }

        [JsonRpcMethod("probe/hold", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> HoldAsync(JsonElement? _)
        {
            if (Hold is { } hold) await hold();
            return JsonDocument.Parse("""{"held":true}""").RootElement.Clone();
        }

        [JsonRpcMethod("probe/echo", UseSingleObjectParameterDeserialization = true)]
        public JsonElement Echo(JsonElement p) => p.Clone();

        [JsonRpcMethod("probe/note", UseSingleObjectParameterDeserialization = true)]
        public void Note(JsonElement p) => _noted.TrySetResult(p.Clone());

        /// <summary>The server's own connection, for a request it answers by asking the client first.</summary>
        public JsonRpc? Rpc { get; set; }

        // As a program that shows a dialog is run: the client's request is answered only after the server's question to the
        // client is.
        [JsonRpcMethod("probe/run", UseSingleObjectParameterDeserialization = true)]
        public async Task<JsonElement> RunAsync(JsonElement _)
        {
            var answer = await Rpc!.InvokeWithParameterObjectAsync<JsonElement>("probe/ask", new { q = "while running" });
            return JsonDocument.Parse($$"""{"ran":{{answer.GetProperty("answer").GetInt32()}}}""").RootElement.Clone();
        }
    }

    [Fact]
    public async Task TheAddinsMessages_GoOverHexIdesConnection_BothWays_AndAreCaptured()
    {
        var pipeName = PipeName();
        var server = new ProbeServer();
        using var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
        var serving = pipe.WaitForConnectionAsync(Token).ContinueWith(_ =>
        {
            var rpc = new JsonRpc(new LspMessageHandler(pipe, pipe, new SystemTextJsonFormatter()), server);
            server.Rpc = rpc;
            rpc.StartListening();
            return rpc;
        }, TaskScheduler.Default);

        await using var capture = new ConversationLog(armEveryConnection: true);
        var router = Router();
        var contributions = new LanguageServerContributions(Substitute.For<HexIDE.IDE.ISettingsService>(), router, new LanguageServerRegistrationFactory(_loggerFactory, capture: capture),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null);
        var asked = new TaskCompletionSource<JsonElement?>(TaskCreationOptions.RunContinuationsAsynchronously);
        var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            MessagePrefix = "probe/",
            ServerMethods = new Dictionary<string, AddinServerMethod>
            {
                ["probe/ask"] = (parameters, _) =>
                {
                    asked.TrySetResult(parameters);
                    return Task.FromResult<JsonElement?>(JsonDocument.Parse("""{"answer":42}""").RootElement.Clone());
                },
            },
        }, "My Add-in");
        await using var _ = connection;

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        var rpc = await serving;
        await server.Initialized.WaitAsync(TimeSpan.FromSeconds(30), Token);
        connection.IsRunning.Should().BeTrue();

        // The add-in's request, and its notification, reach the server as they were sent.
        var echoed = await connection.RequestAsync("probe/echo", JsonDocument.Parse("""{"n":1}""").RootElement, cancellationToken: Token);
        echoed!.Value.GetProperty("n").GetInt32().Should().Be(1);
        await connection.NotifyAsync("probe/note", JsonDocument.Parse("""{"m":"x"}""").RootElement, Token);
        (await server.Noted.WaitAsync(TimeSpan.FromSeconds(30), Token)).GetProperty("m").GetString().Should().Be("x");

        // The server's question is the add-in's to answer; one nobody claimed is not found, as before.
        var answer = await rpc.InvokeWithParameterObjectAsync<JsonElement>("probe/ask", new { q = "?" }, Token);
        answer.GetProperty("answer").GetInt32().Should().Be(42);
        (await asked.Task.WaitAsync(TimeSpan.FromSeconds(30), Token))!.Value.GetProperty("q").GetString().Should().Be("?");
        var unclaimed = () => rpc.InvokeWithParameterObjectAsync<JsonElement>("probe/unclaimed", new { }, Token);
        await unclaimed.Should().ThrowAsync<RemoteMethodNotFoundException>();

        // The server asks while the add-in's own request waits for its answer, as a program that shows a dialog does.
        var ran = await connection.RequestAsync("probe/run", JsonDocument.Parse("{}").RootElement, cancellationToken: Token).WaitAsync(TimeSpan.FromSeconds(30), Token);
        ran!.Value.GetProperty("ran").GetInt32().Should().Be(42);

        // A request of the add-in's own the server does not know is refused by the server, and the connection survives it.
        var unknown = () => connection.RequestAsync("probe/unknown", JsonDocument.Parse("{}").RootElement, cancellationToken: Token);
        await unknown.Should().ThrowAsync<RemoteMethodNotFoundException>();
        connection.IsRunning.Should().BeTrue("a server's refusal of an add-in's request is the add-in's to handle, not the connection's end");

        // Over HexIDE's own connection, so the Protocol Inspector has them.
        await capture.DrainAsync();
        var record = capture.Snapshot("probe");
        record.Select(e => e.Method).Should().Contain(["probe/echo", "probe/note", "probe/ask"]);
        record.Where(e => e.Method == "probe/ask" && e.Direction == ConversationDirection.Received).Should()
            .HaveCount(2).And.OnlyContain(e => e.Outcome == ConversationOutcome.Answered,
                "the add-in's answer goes out over the same connection and completes the server's request in the record");

        rpc.Dispose();
    }

    [Fact]
    public async Task APseudonymisedExport_ScrubsTheFilesAnAddinsOwnMessagesName_BothWays()
    {
        var pipeName = PipeName();
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer());
        await using var capture = new ConversationLog(armEveryConnection: true);
        var router = Router();
        var contributions = new LanguageServerContributions(Substitute.For<HexIDE.IDE.ISettingsService>(), router, new LanguageServerRegistrationFactory(_loggerFactory, capture: capture),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null);
        await using var connection = contributions.Register(
            new AddinLanguageServer(PipeEntry("probe", pipeName)) { MessagePrefix = "probe/" }, "My Add-in");
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await connection.Opened.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // The add-in's request names a file, as RDCore's add-in names the module it runs; the server echoes it back.
        var named = JsonDocument.Parse("""
            {"module":"file:///C:/Fixtures/quintana/InvoiceLedger/Module1.bas","workingFolder":"C:\\Fixtures\\quintana\\InvoiceLedger"}
            """).RootElement;
        await connection.RequestAsync("probe/echo", named, cancellationToken: Token);

        Task<ConversationExport> Export(bool pseudonymise) => ConversationExporter.ExportAsync(capture,
            new HexIDE.Redaction.ConversationRedactor(new HexIDE.Redaction.Pseudonymiser(new Random(7)), pseudonymise), "probe");

        // The control: unredacted, the export holds the path in both directions, so its absence below is the redaction's.
        var plain = await Export(pseudonymise: false);
        plain.Messages.Split('\n').Count(line => line.Contains("quintana/InvoiceLedger/Module1.bas"))
            .Should().Be(2, "the request names it, and the server's answer echoes it back");

        var export = await Export(pseudonymise: true);
        export.Messages.Should().Contain("probe/echo", "the add-in's own messages are in the export");
        export.Messages.Should().NotContain("quintana").And.NotContain("InvoiceLedger").And.NotContain("Fixtures",
            "a file an add-in's message names is the user's, as one in textDocument/didOpen is");
    }

    [Fact]
    public async Task ClaimedActionsRunWithOriginalArgumentsOnPool_DeclaredActionsStayOnWire()
    {
        var pipeName = PipeName();
        var server = new ProbeServer();
        await using var pipe = new ProbePipe(pipeName, () => server);
        var (contributions, router) = WiredContributions();
        JsonElement[]? received = null;
        SynchronizationContext? context = new SynchronizationContext();
        var declaredCalls = 0;
        IAddinServerConnection? added = null;
        ApplyWorkspaceEditResult? during = null;
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            CommandPrefix = "probe.",
            ClientCommands = new Dictionary<string, AddinClientCommand>
            {
                ["probe.run"] = async (args, ct) =>
                {
                    received = args;
                    context = SynchronizationContext.Current;
                    during = await added!.ApplyEditAsync(new WorkspaceEdit(), ct);
                },
                ["probe.declared"] = (_, _) => { declaredCalls++; return Task.CompletedTask; },
            },
        }, "My Add-in");
        added = connection;
        const string uri = "file:///c:/p/README.md";
        await router.OpenDocumentAsync(uri, "# hi", Token);
        var actions = await connection.Client!.RequestCodeActionsAsync(uri, new(new(0, 0), new(0, 1)), [], Token);
        var action = actions[0];
        action.CanExecute!(action.Command!).Should().BeTrue();
        await action.Execute!(action.Command!, Token);
        received.Should().HaveCount(2);
        received![0].GetProperty("n").GetInt32().Should().Be(42);
        received[1].GetString().Should().Be("text");
        context.Should().BeNull();
        during!.FailureReason.Should().Be("No workspace editor is available.", "the handler was authorized and reached the shared applier check");
        var outside = await connection.ApplyEditAsync(new WorkspaceEdit(), Token);
        outside.FailureReason.Should().Contain("command the user started");
        await actions[1].Execute!(actions[1].Command!, Token);
        server.Executed!.Value.GetProperty("command").GetString().Should().Be("probe.declared");
        declaredCalls.Should().Be(0);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LensCommandsStayWithTheirProducingConnection_AndRemovalCancelsHandler(bool stopClient)
    {
        var firstPipe = PipeName();
        var secondPipe = PipeName();
        await using var pipe = new ProbePipe(firstPipe, () => new ProbeServer());
        await using var otherPipe = new ProbePipe(secondPipe, () => new ProbeServer());
        var (contributions, router) = WiredContributions();
        var started = new TaskCompletionSource<CancellationToken>(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", firstPipe))
        {
            CommandPrefix = "probe.",
            ClientCommands = new Dictionary<string, AddinClientCommand>
            {
                ["probe.run"] = async (_, token) =>
                {
                    Interlocked.Increment(ref calls);
                    started.TrySetResult(token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                },
            },
        }, "My Add-in");
        await using var cleanup = connection;
        await using var other = contributions.Register(new AddinLanguageServer(PipeEntry("other", secondPipe)), "Other Add-in");
        const string uri = "file:///c:/p/README.md";
        await router.OpenDocumentAsync(uri, "# hi", Token);
        var lenses = await router.RequestCodeLensesAsync(uri, Token);
        lenses.Should().HaveCount(2);
        router.IsClaimedCodeLensCommand(lenses[0].Command!).Should().BeTrue();
        router.IsClaimedCodeLensCommand(lenses[1].Command!).Should().BeFalse();
        var otherActions = await other.Client!.RequestCodeActionsAsync(uri, new(new(0, 0), new(0, 1)), [], Token);
        otherActions[0].CanExecute!(otherActions[0].Command!).Should().BeFalse();
        await otherActions[0].Execute!(otherActions[0].Command!, Token);
        calls.Should().Be(0);
        await router.ExecuteCodeLensCommandAsync(lenses[1].Command!, Token);
        calls.Should().Be(0);
        var running = router.ExecuteCodeLensCommandAsync(lenses[0].Command!, Token);
        var token = await started.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        if (stopClient) await connection.Client!.StopAsync();
        else await connection.DisposeAsync();
        token.IsCancellationRequested.Should().BeTrue();
        await FluentActions.Awaiting(() => running).Should().ThrowAsync<OperationCanceledException>();
        calls.Should().Be(1);
        var refused = await connection.ApplyEditAsync(new WorkspaceEdit(), Token);
        refused.Applied.Should().BeFalse();
        refused.FailureReason.Should().Contain("command the user started");
    }

    // Accepts each connection HexIDE makes to the pipe, a restart's included, each with a server of its own.
    private sealed class ProbePipe : IAsyncDisposable
    {
        private readonly List<(NamedPipeServerStream Pipe, JsonRpc Rpc)> _connections = [];
        private readonly CancellationTokenSource _stop = new();
        private readonly Task _accepting;

        public ProbePipe(string pipeName, Func<ProbeServer> server)
        {
            _accepting = Task.Run(async () =>
            {
                while (!_stop.IsCancellationRequested)
                {
                    var pipe = new NamedPipeServerStream(pipeName, PipeDirection.InOut, 4, PipeTransmissionMode.Byte, PipeOptions.Asynchronous);
                    try { await pipe.WaitForConnectionAsync(_stop.Token); }
                    catch (OperationCanceledException) { await pipe.DisposeAsync(); return; }
                    var probe = server();
                    var rpc = new JsonRpc(new LspMessageHandler(pipe, pipe, new SystemTextJsonFormatter()), probe);
                    probe.Rpc = rpc;
                    rpc.StartListening();
                    lock (_connections) _connections.Add((pipe, rpc));
                }
            });
        }

        public JsonRpc Latest { get { lock (_connections) return _connections[^1].Rpc; } }

        public async ValueTask DisposeAsync()
        {
            await _stop.CancelAsync();
            await _accepting;
            foreach (var (pipe, rpc) in _connections) { rpc.Dispose(); await pipe.DisposeAsync(); }
        }
    }

    private (LanguageServerContributions Contributions, LspClientRegistry Router) WiredContributions(ILspWorkspace? workspace = null)
    {
        var router = new LspClientRegistry([], _loggerFactory.CreateLogger<LspClientRegistry>(), workspace);
        return (new LanguageServerContributions(Substitute.For<HexIDE.IDE.ISettingsService>(), router, new LanguageServerRegistrationFactory(_loggerFactory),
            new LanguageServerConfigLoader(_loggerFactory.CreateLogger<LanguageServerConfigLoader>()),
            new HashSet<string>(StringComparer.Ordinal), _loggerFactory.CreateLogger<LanguageServerContributions>(), null), router);
    }

    [Fact]
    public async Task AMethodAddedToTheAddinsDictionaryAfterRegistering_IsNotClaimed()
    {
        var pipeName = PipeName();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer { OnStarted = () => initialized.TrySetResult() });
        var (contributions, router) = WiredContributions();
        var methods = new Dictionary<string, AddinServerMethod>
        {
            ["probe/ask"] = (_, _) => Task.FromResult<JsonElement?>(JsonDocument.Parse("""{"answer":1}""").RootElement.Clone()),
        };
        await using var connection = contributions.Register(
            new AddinLanguageServer(PipeEntry("probe", pipeName)) { MessagePrefix = "probe/", ServerMethods = methods }, "My Add-in");

        // After the checks: the add-in still holds its own dictionary.
        var used = 0;
        Task<JsonElement?> Mine(JsonElement? _, CancellationToken __)
        {
            Interlocked.Increment(ref used);
            return Task.FromResult<JsonElement?>(JsonDocument.Parse("""{"title":"mine"}""").RootElement.Clone());
        }
        methods["window/showMessageRequest"] = Mine;
        methods["elsewhere/ask"] = Mine;
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        // HexIDE answers the first itself (#3069); a method outside the prefix that HexIDE doesn't answer is not found,
        // as any unknown method is. Either way the add-in's handler is never asked, however many methods HexIDE handles.
        await pipe.Latest.InvokeWithParameterObjectAsync<JsonElement?>("window/showMessageRequest", new { type = 3, message = "?", actions = new[] { new { title = "mine" } } }, Token);
        var hijack = () => pipe.Latest.InvokeWithParameterObjectAsync<JsonElement>("elsewhere/ask", new { }, Token);
        await hijack.Should().ThrowAsync<RemoteMethodNotFoundException>();
        used.Should().Be(0, "a method outside the prefix is never the add-in's to answer");
    }

    [Fact]
    public async Task AHandlerThatThrows_IsAnsweredWithItsMessageOnly_AndTheConnectionStaysUp()
    {
        var pipeName = PipeName();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer { OnStarted = () => initialized.TrySetResult() });
        var (contributions, router) = WiredContributions();
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            MessagePrefix = "probe/",
            ServerMethods = new Dictionary<string, AddinServerMethod>
            {
                ["probe/boom"] = (_, _) => throw new InvalidOperationException("the add-in fell over reading its settings"),
            },
        }, "My Add-in");
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        var boom = () => pipe.Latest.InvokeWithParameterObjectAsync<JsonElement>("probe/boom", new { }, Token);

        var thrown = (await boom.Should().ThrowAsync<RemoteInvocationException>()).Which;
        thrown.Message.Should().Be("the add-in fell over reading its settings", "the message is the add-in's to word");
        thrown.ErrorData.Should().BeNull("no type, stack or file of the add-in's goes to the server");
        connection.IsRunning.Should().BeTrue();
    }

    [Fact]
    public async Task ARequestWaitsAsLongAsTheAddinSays_AndHexIdesOwnTimeoutOtherwise()
    {
        var pipeName = PipeName();
        var initialized = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var held = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer
        {
            OnStarted = () => initialized.TrySetResult(),
            Hold = async () => { held.TrySetResult(); await release.Task; },
        });
        var (contributions, router) = WiredContributions();
        await using var connection = contributions.Register(
            new AddinLanguageServer(PipeEntry("probe", pipeName)) { MessagePrefix = "probe/" }, "My Add-in");
        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        await initialized.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        var client = (VBLspClient)connection.Client!;
        client.RequestTimeout = TimeSpan.FromMilliseconds(200);

        var defaulted = () => connection.RequestAsync("probe/hold", JsonDocument.Parse("{}").RootElement, cancellationToken: Token);
        await defaulted.Should().ThrowAsync<TimeoutException>("null is HexIDE's own request timeout");

        var waiting = connection.RequestAsync("probe/hold", JsonDocument.Parse("{}").RootElement, Timeout.InfiniteTimeSpan, Token);
        await held.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);
        // Past HexIDE's own timeout: only the answer can end this one now.
        await Task.Delay(client.RequestTimeout * 3, Token);
        waiting.IsCompleted.Should().BeFalse();
        release.SetResult();
        (await waiting.WaitAsync(TimeSpan.FromSeconds(30), Token))!.Value.GetProperty("held").GetBoolean().Should().BeTrue();
    }

    // The UI thread's stand-in: one thread, running in turn what is posted to it, as a dispatcher does.
    private sealed class OneThreadContext : SynchronizationContext, IDisposable
    {
        private readonly System.Collections.Concurrent.BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public OneThreadContext()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable()) callback(state);
            }) { IsBackground = true, Name = "test dispatcher" };
            _thread.Start();
        }

        public int ThreadId => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (_queue.IsAddingCompleted) return;
            _queue.Add((d, state));
        }

        public Task Run(Func<Task> work)
        {
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            Post(async _ =>
            {
                try
                {
                    await work();
                    done.SetResult();
                }
                catch (Exception ex)
                {
                    done.SetException(ex);
                }
            }, null);
            return done.Task;
        }

        public void Dispose() => _queue.CompleteAdding();
    }

    [Fact]
    public async Task WhatRunsAfterAStart_RunsOnThePool_NotOnTheThreadThatOpenedTheDocument()
    {
        var pipeName = PipeName();
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer());
        var (contributions, router) = WiredContributions();
        using var ui = new OneThreadContext();
        var ran = new TaskCompletionSource<(int Thread, SynchronizationContext? Context)>(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            MessagePrefix = "probe/",
            AfterStart = (_, _) =>
            {
                ran.TrySetResult((Environment.CurrentManagedThreadId, SynchronizationContext.Current));
                return Task.CompletedTask;
            },
        }, "My Add-in");

        // The code window opens its documents on the UI thread, and the start's awaits keep to it.
        await ui.Run(() => router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token)).WaitAsync(TimeSpan.FromSeconds(30), Token);
        var (thread, context) = await ran.Task.WaitAsync(TimeSpan.FromSeconds(30), Token);

        thread.Should().NotBe(ui.ThreadId, "an add-in's work after a start must never hold the editor");
        context.Should().BeNull("it runs on the thread pool, which has no context to come back to");
    }

    [Fact]
    public async Task WhatRunsAfterAStart_RunsAfterEachOne_ARestartIncluded_OverTheConnection()
    {
        var pipeName = PipeName();
        await using var pipe = new ProbePipe(pipeName, () => new ProbeServer());
        var dir = "c:/p";
        var workspace = Substitute.For<ILspWorkspace>();
        workspace.Directory.Returns(_ => dir);
        var (contributions, router) = WiredContributions(workspace);
        var answers = System.Threading.Channels.Channel.CreateUnbounded<int>();
        await using var connection = contributions.Register(new AddinLanguageServer(PipeEntry("probe", pipeName))
        {
            MessagePrefix = "probe/",
            // The add-in's own handshake, as RDCore's add-in sends its platform's after every start.
            AfterStart = async (started, token) =>
            {
                var echoed = await started.RequestAsync("probe/echo", JsonDocument.Parse("""{"n":7}""").RootElement, cancellationToken: token);
                await answers.Writer.WriteAsync(echoed!.Value.GetProperty("n").GetInt32(), token);
            },
        }, "My Add-in");

        await router.OpenDocumentAsync("file:///c:/p/README.md", "# hi", Token);
        (await answers.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token)).Should().Be(7);

        dir = "c:/q";   // the project moves, and the server is a new process at the new root
        await router.OpenDocumentAsync("file:///c:/q/Other.md", "# again", Token);
        (await answers.Reader.ReadAsync(Token).AsTask().WaitAsync(TimeSpan.FromSeconds(30), Token)).Should().Be(7,
            "the restart is a new server, which needs the add-in's handshake again");
        answers.Reader.TryRead(out _).Should().BeFalse("once per start");
    }
}
