using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.Addins;

/// <summary>
/// <see cref="IHexIdeHost.RegisterLanguageServer"/> on the real host (#2760): the server is listed under the add-in that
/// contributed it, and goes with the add-in's other contributions when its initialisation fails.
/// </summary>
public sealed class AddinLanguageServerTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-addin-server-" + Guid.NewGuid().ToString("N"));
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly IProjectRunnerService _runner = Substitute.For<IProjectRunnerService>();
    private readonly EventBus _bus = new();
    private readonly AddinEventService _events;
    private readonly LspClientRegistry _router = new([], NullLogger<LspClientRegistry>.Instance);
    private readonly AddinDiagnosticsService _diagnostics;
    private readonly IAddinRegistry _addins = Substitute.For<IAddinRegistry>();
    private readonly HexIdeHost _host;

    public AddinLanguageServerTests()
    {
        Directory.CreateDirectory(_root);
        _events = new AddinEventService(_projects, _runner, _bus);
        _diagnostics = new AddinDiagnosticsService(_router, _projects);
        // The add-in is this test assembly, as the add-in manager would list it.
        _addins.GetAll().Returns([new AddinInfo(typeof(AddinLanguageServerTests).Assembly.Location, "Probe Add-in", "", "1.0", "")]);
        _host = new HexIdeHost(new AddinMenuService(), new AddinToolWindowService(), _events, _addins,
            new AddinEditorService(Substitute.For<IDocumentDockService>(), Substitute.For<IEditorService>(), _projects),
            new AddinProjectService(_projects), _diagnostics, new AddinCommandService(), new AddinProjectTemplateService(),
            new AddinOptionsService(), new ComponentRegistry(), _runner,
            new LanguageServerContributions(_router, new LanguageServerRegistrationFactory(NullLoggerFactory.Instance),
                new LanguageServerConfigLoader(NullLogger<LanguageServerConfigLoader>.Instance),
                new HashSet<string>(StringComparer.Ordinal), NullLogger<LanguageServerContributions>.Instance));
    }

    public void Dispose()
    {
        _diagnostics.Dispose();
        _events.Dispose();
        Directory.Delete(_root, true);
    }

    private static AddinLanguageServer Probe() => new(new LanguageServerEntry
    {
        Id = "probe",
        DisplayName = "Probe",
        Extensions = [".md"],
        LanguageId = "markdown",
        Transport = "pipe",
        PipeName = "hexide-unused-" + Guid.NewGuid().ToString("N"),
    });

    [Fact]
    public void AContributedServer_IsListedUnderTheTitleOfTheAddinThatContributedIt()
    {
        using var connection = _host.RegisterLanguageServer(Probe());

        _router.Connections.Should().ContainSingle().Which.Origin.Should().Be("Probe Add-in");
    }

    [Fact]
    public void AnAddinWhoseInitialisationFails_HasTheServerItRegisteredTakenOut()
    {
        using var registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(),
            new ConsentStore(Path.Combine(_root, "consent.json")),
            new RevocationStore(null, Path.Combine(_root, "revocations")), Path.Combine(_root, "state.json"), _root);
        var addin = Substitute.For<IAddin>();
        addin.When(a => a.Initialize(_host)).Do(_ =>
        {
            _host.RegisterLanguageServer(Probe());
            throw new InvalidOperationException("initialisation failed");
        });
        var ctx = new AddinContext(new AddinInfo("failed", "Failed", "", "", ""));

        registry.InitializeAddin(ctx, addin, AddinStatus.Loaded, _host);

        ctx.Status.Should().Be(AddinStatus.Error);
        _router.Connections.Should().BeEmpty("the server goes with the add-in's other contributions");
    }
}
