using HexIDE.Events;
using System.Text.Json;
using HexIDE.Conversations;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Tools.LanguageServers;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The view over <c>ILanguageConnectionRegistry</c> — which, until this landed, had exactly two references
/// in the whole tree: the DI binding and the class implementing it. Every failure mode on the language-server
/// seam presented identically, as nothing happening and nothing saying why (hexide-io/HexIDE#259).
/// </summary>
public class LanguageServersToolViewModelTests : IAsyncDisposable
{
    /// <summary>
    /// A real capture, not a substitute.
    /// </summary>
    /// <remarks>
    /// Arming is the one thing on this window a person changes, and the row reads it straight back off the
    /// capture rather than holding a copy. A mock would let the row be wrong about what is actually being
    /// recorded while every assertion here passed.
    /// </remarks>
    private readonly ConversationLog _capture = new();
    private readonly IEventBus _events = Substitute.For<IEventBus>();

    public async ValueTask DisposeAsync()
    {
        await _capture.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    private const string FullCapabilities =
        """{"textDocumentSync":{"openClose":true,"change":1},"hoverProvider":true}""";

    private static ILocalizationService Loc()
    {
        var loc = Substitute.For<ILocalizationService>();
        // The view model asks for state words and the duration format; echo the key so an assertion can
        // tell them apart without depending on English.
        loc.GetString(Arg.Any<string>()).Returns(ci => ci.Arg<string>());
        return loc;
    }

    private static LanguageServerConnection Conn(
        string id, string language, LanguageConnectionState state = LanguageConnectionState.Running,
        string? capabilitiesJson = FullCapabilities, int priority = 0,
        LanguageConnectionTransport transport = LanguageConnectionTransport.Stdio,
        string? endpoint = null, ServerIdentity? identity = null) =>
        new(id, id, LanguageConnectionKind.LanguageServer, state, [".x"], language,
            capabilitiesJson is null ? null : JsonDocument.Parse(capabilitiesJson).RootElement.Clone(),
            transport, endpoint, priority, DateTimeOffset.UtcNow, identity);

    private (LanguageServersToolViewModel Vm, ILanguageConnectionRegistry Registry) Sut(
        params LanguageServerConnection[] connections)
    {
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns(connections);
        registry.ConfigurationProblems.Returns([]);
        return (new LanguageServersToolViewModel(registry, Loc(), _capture, _events), registry);
    }

    [Fact]
    public void ServersAreGroupedByLanguageRatherThanByProtocol()
    {
        // The question people arrive with is "what have I got for VB6", not "which of these speak LSP".
        var (vm, _) = Sut(
            Conn("bundled", "vb6"),
            Conn("rumdl", "markdown"),
            Conn("vba-lsp", "vb6"));

        vm.Groups.Select(g => g.Language).Should().Equal(["markdown", "vb6"]);
        vm.Groups.Single(g => g.Language == "vb6").Rows.Should().HaveCount(2);
    }

    [Fact]
    public void TwoServersClaimingOneLanguageAppearTogetherInPriorityOrder()
    {
        // #259 names "two servers claiming the same language, where the user cannot tell which one
        // answered" as a failure mode. Grouping makes the collision structural, and priority order makes
        // the answer readable: the registry picks the top one for anything that cannot merge two replies.
        var (vm, _) = Sut(
            Conn("bundled", "vb6", priority: -1000),
            Conn("mine", "vb6", priority: 10));

        vm.Groups.Single().Rows.Select(r => r.Id).Should().Equal(["mine", "bundled"],
            "the higher priority wins the features that cannot merge, so it reads first");
    }

    [Fact]
    public void AServerThatAnsweredAndAdvertisedNothingSaysSo()
    {
        // The state that looks healthiest and is least useful: the handshake completed, so a bare "Running"
        // badge reports it as success. This is the row that has to say more than its status word.
        var (vm, _) = Sut(Conn("quiet", "vba", capabilitiesJson: "{}"));

        var row = vm.Groups.Single().Rows.Single();
        row.IsRunning.Should().BeTrue();
        row.AdvertisedNothing.Should().BeTrue();
        row.SendingNothing.Should().BeTrue("no document sync was advertised, so nothing is being sent");
    }

    [Fact]
    public void AServerAdvertisingSyncIsNotReportedAsSilent()
    {
        var (vm, _) = Sut(Conn("healthy", "vb6"));

        var row = vm.Groups.Single().Rows.Single();
        row.AdvertisedNothing.Should().BeFalse();
        row.SendingNothing.Should().BeFalse();
    }

    [Fact]
    public void AServerThatHasNotStartedIsNotAccusedOfBeingSilent()
    {
        // Lazy start is normal: a server for a language nothing has opened is quiet on purpose, and saying
        // "advertised nothing" about it would be the same category error the whole window exists to fix.
        var (vm, _) = Sut(Conn("idle", "latex",
            state: LanguageConnectionState.NotStarted, capabilitiesJson: null));

        var row = vm.Groups.Single().Rows.Single();
        row.AdvertisedNothing.Should().BeFalse();
        row.SendingNothing.Should().BeFalse();
    }

    [Fact]
    public void TheEndpointAndTransportAreShownVerbatim()
    {
        // The first failure anyone hits is a command that does not exist or is not on PATH, and the command
        // string is the thing they need in front of them — comparable to their own file, so not translated.
        var (vm, _) = Sut(Conn("md", "markdown",
            transport: LanguageConnectionTransport.Pipe, endpoint: "vba-lsp.pipe (connect)"));

        var row = vm.Groups.Single().Rows.Single();
        row.Transport.Should().Be("pipe");
        row.Endpoint.Should().Be("vba-lsp.pipe (connect)");
        row.HasEndpoint.Should().BeTrue();
    }

    [Fact]
    public void BothHalvesOfTheClaimAreShown()
    {
        // Routing accepts a server because its language id matches OR its extensions do, so showing one
        // reproduces #277: a server that receives documents it does not appear to claim.
        var (vm, _) = Sut(Conn("s", "vba"));

        vm.Groups.Single().Rows.Single().Claims.Should().Contain(".x").And.Contain("vba");
    }

    [Fact]
    public void TheServersOwnNameIsShownWhenItGaveOne()
    {
        // The only field here that is not HexIDE's configuration reflected back at the user.
        var (vm, _) = Sut(Conn("s", "vba", identity: new ServerIdentity("ExampleVbaServer", "1.0.0")));

        var row = vm.Groups.Single().Rows.Single();
        row.HasReportedIdentity.Should().BeTrue();
        row.ReportedBy.Should().Be("ExampleVbaServer 1.0.0");
    }

    [Fact]
    public void AServerThatNamedItselfWithoutAVersionStillReportsItsName()
    {
        var (vm, _) = Sut(Conn("s", "vba", identity: new ServerIdentity("nameless-version", null)));

        vm.Groups.Single().Rows.Single().ReportedBy.Should().Be("nameless-version");
    }

    [Fact]
    public void ConfigurationProblemsAreShownEvenThoughTheyBecameNoConnection()
    {
        // An entry that failed to parse produces no row anywhere, so without this section it and an entry
        // that was never written are the same observable state: an empty list.
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([]);
        registry.ConfigurationProblems.Returns([
            new LanguageServerConfigProblem(null, "lsp-servers.json is not valid JSON", true),
        ]);

        var vm = new LanguageServersToolViewModel(registry, Loc(), _capture, _events);

        vm.HasProblems.Should().BeTrue();
        vm.HasNoServers.Should().BeTrue("a file that failed to parse contributes no servers");
        vm.Problems.Single().Message.Should().Contain("not valid JSON");
    }

    // The registry's own refresh is marshalled too — it raises from transport and RPC callbacks — so its
    // test lives beside the arming one in HexIDE.Integration.Tests, where a dispatcher is bound. It sat
    // here for a while and passed on most runs (hexide-io/HexIDE#286).

    [Fact]
    public void TheReportIsPlainTextSomeoneCanSendToWhoeverWroteTheServer()
    {
        var (vm, _) = Sut(Conn("vba-lsp", "vba", capabilitiesJson: "{}",
            transport: LanguageConnectionTransport.Pipe, endpoint: "vba-lsp.pipe (connect)",
            identity: new ServerIdentity("ExampleVbaServer", "1.0.0")));

        var report = vm.ToReportText();

        report.Should().Contain("vba-lsp").And.Contain("pipe").And.Contain("vba-lsp.pipe (connect)");
        report.Should().Contain("ExampleVbaServer 1.0.0");
    }

    // ── Arming ───────────────────────────────────────────────────────────────

    [Fact]
    public void AServerStartsUnarmed()
    {
        // Off by default is the whole privacy position: envelopes always, content only when asked for.
        var (vm, _) = Sut(Conn("bundled", "vb6"));

        vm.Groups.Single().Rows.Single().IsArmed.Should().BeFalse();
    }

    [Fact]
    public void TickingTheRowArmsThatConnectionAndNoOther()
    {
        // Per server, because several can be attached and a developer is nearly always chasing one. Arming
        // everything would multiply the only expensive part of this for no gain.
        var (vm, _) = Sut(Conn("bundled", "vb6"), Conn("latex", "tex"));

        var rows = vm.Groups.SelectMany(g => g.Rows).ToList();
        rows.Single(r => r.Id == "bundled").IsArmed = true;

        _capture.IsArmed("bundled").Should().BeTrue();
        _capture.IsArmed("latex").Should().BeFalse();
    }

    [Fact]
    public void TheRowReadsBackWhateverArmedTheConnection()
    {
        // Three things arm the same connection: this window, the automation surface and the launch flag. A
        // toggle that showed only its own last click would be a control that lies about its own state.
        var (vm, _) = Sut(Conn("bundled", "vb6"));
        var row = vm.Groups.Single().Rows.Single();

        _capture.Arm("bundled", true);

        row.IsArmed.Should().BeTrue();
    }

    // Arming raised from elsewhere has to reach the checkbox, and the window marshals it because the
    // automation surface arms from its own thread. That path needs a real dispatcher, so its test lives in
    // HexIDE.Integration.Tests where one is bound: see ArmingNotificationTests.

    [Fact]
    public void ArmingIsNotDisturbedByTheWindowRebuilding()
    {
        // The window rebuilds every row wholesale on every registry event, deliberately. Arming has to
        // survive that by construction rather than by being patched back in.
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([Conn("bundled", "vb6", state: LanguageConnectionState.Starting)]);
        registry.ConfigurationProblems.Returns([]);

        var vm = new LanguageServersToolViewModel(registry, Loc(), _capture, _events);
        vm.Groups.Single().Rows.Single().IsArmed = true;

        registry.Connections.Returns([Conn("bundled", "vb6", state: LanguageConnectionState.Running)]);
        registry.ConnectionsChanged += Raise.Event<EventHandler>(registry, EventArgs.Empty);

        vm.Groups.Single().Rows.Single().IsArmed.Should().BeTrue();
    }

    [Fact]
    public void AServerThatHasNotStartedCanBeArmedInAdvance()
    {
        // The one arming question a per-row toggle cannot answer: a server starts on the first document of
        // a language it claims, so the connection somebody most wants to arm has no row to tick.
        var (vm, _) = Sut(Conn("bundled", "vb6"));

        vm.ArmsFutureServers.Should().BeFalse();
        vm.ArmsFutureServers = true;

        _capture.ArmsEveryConnection.Should().BeTrue();
    }

    [Fact]
    public void ArmingFutureServersLeavesTheRecordedPastAlone()
    {
        // Turning it on is not a claim about what has already been recorded, and turning it off must not
        // disarm a connection somebody armed deliberately.
        var (vm, _) = Sut(Conn("bundled", "vb6"));
        _capture.Arm("bundled", true);

        vm.ArmsFutureServers = true;
        vm.ArmsFutureServers = false;

        _capture.IsArmed("bundled").Should().BeTrue();
    }

    [Fact]
    public void TheLaunchFlagShowsAsTicked()
    {
        // --capture-lsp sets the same default this checkbox does, so a session started with it must not
        // present an unticked box beside a capture that is on.
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([]);
        registry.ConfigurationProblems.Returns([]);

        _capture.ArmsEveryConnection = true;
        var vm = new LanguageServersToolViewModel(registry, Loc(), _capture, _events);

        vm.ArmsFutureServers.Should().BeTrue();
    }

    // ── Reaching the inspector ───────────────────────────────────────────────

    [Fact]
    public void TheHeaderAsksForEverything()
    {
        // The inspector never opens itself, so it has to be one action from where trouble is reported.
        var (vm, _) = Sut(Conn("bundled", "vb6"));

        vm.ShowAllMessagesCommand.Execute(null);

        _events.Received(1).Publish(Arg.Is<OpenProtocolInspectorEvent>(e => e.ConnectionId == null));
    }

    [Fact]
    public void ARowAsksForItsOwnServer()
    {
        // Arriving from one server's row and landing on the merged timeline would make the reader do the
        // filtering the link was for.
        var (vm, _) = Sut(Conn("bundled", "vb6"), Conn("latex", "tex"));

        vm.Groups.SelectMany(g => g.Rows).Single(r => r.Id == "latex").ShowMessagesCommand.Execute(null);

        _events.Received(1).Publish(Arg.Is<OpenProtocolInspectorEvent>(e => e.ConnectionId == "latex"));
    }

    [Fact]
    public void TheRowLinkIsOfferedWhetherOrNotTheServerIsArmed()
    {
        // A deliberate departure from the plan, which said to show it only when armed. Envelopes are
        // recorded unconditionally, so an unarmed connection still answers "was it even sent" — and the
        // moment somebody most wants that is a server that has failed, which is exactly when they will not
        // have armed it.
        var (vm, _) = Sut(Conn("bundled", "vb6"));
        var row = vm.Groups.Single().Rows.Single();

        row.IsArmed.Should().BeFalse();
        row.ShowMessagesCommand.CanExecute(null).Should().BeTrue();
    }
}
