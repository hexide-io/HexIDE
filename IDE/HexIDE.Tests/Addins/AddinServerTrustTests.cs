using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.Addins;

public sealed class AddinServerTrustTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-server-trust-" + Guid.NewGuid().ToString("N"));
    private readonly ConsentStore _consent;
    private readonly AddinRegistry _registry;
    private readonly RevocationStore _revocations;
    private readonly LspClientRegistry _router = new([], NullLogger<LspClientRegistry>.Instance);
    private readonly HexIdeHost _host;
    private readonly AddinEventService _events;
    private readonly AddinDiagnosticsService _diagnostics;
    private readonly AddinInfo _info;
    private readonly AddinContext _context;
    private int _launches;
    private string Marker => Path.Combine(_root, "started.txt");

    public AddinServerTrustTests()
    {
        Directory.CreateDirectory(_root);
        _consent = new ConsentStore(Path.Combine(_root, "consent.json"));
        _consent.Record("probe", "publisher", "hash", ConsentDecisions.Allow);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.Addins.ServerLaunchRefused").Returns("Language server launch refused for add-in '{0}'.");
        _revocations = new RevocationStore(null, _root);
        _registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), localization, _consent,
            _revocations, Path.Combine(_root, "state.json"), _root);
        _info = new AddinInfo(_root, "Probe Add-in", "", "1", "", Id: "probe", ManifestHash: "hash",
            Chain: new TrustChain { BuildVersion = "1", BuildHash = "hash", IsFirstParty = false,
                Publisher = new TrustLink("Publisher", "publisher", "key"),
                Intermediate = new TrustLink("Marketplace", "intermediate", "key"),
                Root = new TrustLink("Root", "root", "key") });
        // Seed the state after package verification/initialization; these tests exercise session changes, not loading.
        var contexts = (List<AddinContext>)typeof(AddinRegistry).GetField("_contexts",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_registry)!;
        _context = new AddinContext(_info) { Status = AddinStatus.Loaded };
        contexts.Add(_context);
        var projects = Substitute.For<IProjectManager>();
        var runner = Substitute.For<IProjectRunnerService>();
        _events = new AddinEventService(projects, runner, new EventBus());
        _diagnostics = new AddinDiagnosticsService(_router, projects);
        _host = new HexIdeHost(new AddinMenuService(), new AddinToolWindowService(), _events, _registry,
            new AddinEditorService(Substitute.For<IDocumentDockService>(), Substitute.For<IEditorService>(), projects),
            new AddinProjectService(projects), _diagnostics, new AddinCommandService(), new AddinProjectTemplateService(),
            new AddinOptionsService(), new ComponentRegistry(), runner,
            new LanguageServerContributions(_router, new LanguageServerRegistrationFactory(NullLoggerFactory.Instance),
                new LanguageServerConfigLoader(NullLogger<LanguageServerConfigLoader>.Instance), [],
                NullLogger<LanguageServerContributions>.Instance));
    }

    private IAddinServerConnection Register(Func<CancellationToken, Task>? gate = null) =>
        _host.RegisterLanguageServer(new AddinLanguageServer(new LanguageServerEntry
        {
            Id = "probe-server", DisplayName = "Probe", Extensions = [".md"], LanguageId = "markdown",
            Transport = "stdio",
            Command = OperatingSystem.IsWindows() ? Path.Combine(System.Environment.SystemDirectory, "cmd.exe") : "/bin/sh",
            WorkingDirectory = _root,
            Arguments = new ServerArguments(null, OperatingSystem.IsWindows()
                ? ["/c", "echo started > started.txt"]
                : ["-c", "printf started > started.txt"]),
        }) { BeforeLaunch = gate ?? (_ => { Interlocked.Increment(ref _launches); return Task.CompletedTask; }) },
            new AddinOrigin(_info.Id, _info.Title));

    [Theory]
    [InlineData(AddinStatus.Loaded)]
    [InlineData(AddinStatus.AwaitingConsent)]
    public async Task AConsentedEnabledAddin_CanRunTheProbeCommand(AddinStatus status)
    {
        // Late initialization keeps AwaitingConsent until Initialize returns, even after Allow was recorded.
        _context.Status = status;
        await using var connection = (AddinServerConnection)Register();

        await _router.OpenDocumentAsync("file:///probe.md", "# probe", TestContext.Current.CancellationToken);

        _launches.Should().Be(1);
        File.Exists(Marker).Should().BeTrue(string.Join("; ",
            _router.Connections.SelectMany(c => c.Attempt?.Steps ?? []).Select(s => s.Detail)));
        File.ReadAllText(Marker).Trim().Should().Be("started");
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task DisablingOrRevokingConsent_RemovesServersBeforeTheNextDocument(bool revoke, bool registerAfter)
    {
        using var connection = registerAfter ? null : Register();
        if (!registerAfter) _router.Connections.Should().ContainSingle();
        if (revoke) _registry.RevokeConsent("hash");
        else _registry.SetEnabled(_root, false);
        using var late = registerAfter ? Register() : null;

        await _router.OpenDocumentAsync("file:///probe.md", "# probe", TestContext.Current.CancellationToken);

        _router.Connections.Should().BeEmpty();
        _launches.Should().Be(0);
        File.Exists(Marker).Should().BeFalse("the contributed command must not run");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LaunchGate_RechecksLiveConsent_BeforeAndAfterTheAddinsGate(bool duringGate)
    {
        using var connection = Register(_ =>
        {
            Interlocked.Increment(ref _launches);
            if (duringGate) _consent.Revoke("hash");
            return Task.CompletedTask;
        });
        if (!duringGate) _consent.Revoke("hash");

        await _router.OpenDocumentAsync("file:///probe.md", "# probe", TestContext.Current.CancellationToken);

        _router.Connections.Should().ContainSingle().Which.Attempt!.Steps.Should()
            .Contain(s => s.Detail != null && s.Detail.Contains("Probe Add-in"));
        _launches.Should().Be(duringGate ? 1 : 0);
        File.Exists(Marker).Should().BeFalse("the contributed command must not run");
    }

    [Theory]
    [InlineData("disabled")]
    [InlineData("publisher")]
    [InlineData("intermediate")]
    [InlineData("build")]
    public async Task LaunchGate_RefusesCurrentDisabledOrRevokedState(string reason)
    {
        using var connection = Register();
        if (reason == "disabled")
        {
            // Exercise the launch backstop independently of the disable handler's eager removal.
            var disabled = (HashSet<string>)typeof(AddinRegistry).GetField("_disabled",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_registry)!;
            disabled.Add(_root);
        }
        if (reason != "disabled")
        {
            var revoked = new RevokedSets
            {
                PublisherIds = reason == "publisher" ? ["publisher"] : [],
                IntermediateIds = reason == "intermediate" ? ["intermediate"] : [],
                ManifestHashes = reason == "build" ? ["hash"] : [],
            };
            // The store's signature/refresh path has its own tests; replace only the active list here.
            typeof(RevocationStore).GetField("_active",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                .SetValue(_revocations, new RevocationList { Revoked = revoked });
        }

        await _router.OpenDocumentAsync("file:///probe.md", "# probe", TestContext.Current.CancellationToken);

        _router.Connections.Should().ContainSingle().Which.Attempt!.Steps.Should()
            .Contain(s => s.Detail != null && s.Detail.Contains("Probe Add-in"));
        _launches.Should().Be(0);
        File.Exists(Marker).Should().BeFalse();
    }

    public void Dispose()
    {
        _registry.Dispose();
        _diagnostics.Dispose();
        _events.Dispose();
        Directory.Delete(_root, true);
    }
}
