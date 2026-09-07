using System.Text.Json;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Tools.LanguageServers;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The view over <c>ILanguageConnectionRegistry</c> — which, until this landed, had exactly two references
/// in the whole tree: the DI binding and the class implementing it. Every failure mode on the language-server
/// seam presented identically, as nothing happening and nothing saying why (hexide-io/HexIDE#259).
/// </summary>
public class LanguageServersToolViewModelTests
{
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

    private static (LanguageServersToolViewModel Vm, ILanguageConnectionRegistry Registry) Sut(
        params LanguageServerConnection[] connections)
    {
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns(connections);
        registry.ConfigurationProblems.Returns([]);
        return (new LanguageServersToolViewModel(registry, Loc()), registry);
    }

    [Fact]
    public void ServersAreGroupedByLanguageRatherThanByProtocol()
    {
        // The question people arrive with is "what have I got for VB6", not "which of these speak LSP".
        var (vm, _) = Sut(
            Conn("bundled", "vb6"),
            Conn("rumdl", "markdown"),
            Conn("rdcore", "vb6"));

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
            transport: LanguageConnectionTransport.Pipe, endpoint: "hexide.rdcore (connect)"));

        var row = vm.Groups.Single().Rows.Single();
        row.Transport.Should().Be("pipe");
        row.Endpoint.Should().Be("hexide.rdcore (connect)");
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
        var (vm, _) = Sut(Conn("s", "vba", identity: new ServerIdentity("RDCore.LanguageServer", "1.0.0")));

        var row = vm.Groups.Single().Rows.Single();
        row.HasReportedIdentity.Should().BeTrue();
        row.ReportedBy.Should().Be("RDCore.LanguageServer 1.0.0");
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

        var vm = new LanguageServersToolViewModel(registry, Loc());

        vm.HasProblems.Should().BeTrue();
        vm.HasNoServers.Should().BeTrue("a file that failed to parse contributes no servers");
        vm.Problems.Single().Message.Should().Contain("not valid JSON");
    }

    [Fact]
    public void TheViewRebuildsWhenTheRegistrySaysSomethingChanged()
    {
        // The reason ILspClient gained StateChanged. A connection's death is otherwise observable only by
        // asking, so a view would report the past until something else happened to refresh it.
        var registry = Substitute.For<ILanguageConnectionRegistry>();
        registry.Connections.Returns([Conn("s", "vb6", state: LanguageConnectionState.Starting)]);
        registry.ConfigurationProblems.Returns([]);

        var vm = new LanguageServersToolViewModel(registry, Loc());
        vm.Groups.Single().Rows.Single().IsRunning.Should().BeFalse();

        registry.Connections.Returns([Conn("s", "vb6", state: LanguageConnectionState.Running)]);
        registry.ConnectionsChanged += Raise.Event<EventHandler>(registry, EventArgs.Empty);

        vm.Groups.Single().Rows.Single().IsRunning.Should().BeTrue(
            "the view must refresh when the registry says a connection changed, not when something else "
          + "happens to ask");
    }

    [Fact]
    public void TheReportIsPlainTextSomeoneCanSendToWhoeverWroteTheServer()
    {
        var (vm, _) = Sut(Conn("rdcore", "vba", capabilitiesJson: "{}",
            transport: LanguageConnectionTransport.Pipe, endpoint: "hexide.rdcore (connect)",
            identity: new ServerIdentity("RDCore.LanguageServer", "1.0.0")));

        var report = vm.ToReportText();

        report.Should().Contain("rdcore").And.Contain("pipe").And.Contain("hexide.rdcore (connect)");
        report.Should().Contain("RDCore.LanguageServer 1.0.0");
    }
}
