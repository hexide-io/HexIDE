using System.Text.Json;
using Avalonia.Headless.XUnit;
using HexIDE.Addins;
using HexIDE.Bookmarks;
using HexIDE.Conversations;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;
using Microsoft.Extensions.Logging.Abstractions;

namespace HexIDE.Tests.Addins;

/// <summary>
/// An add-in's debug launch and project type follow its trust as its language server does: taken out when it is disabled or
/// its consent is revoked, and a launch starts its adapter only while the add-in may start what it contributed, checked
/// before it is asked for its configuration and again before what it made is started.
/// </summary>
public sealed class AddinContributionTrustTests : IAsyncDisposable
{
    private const string Key = "probe/Probe";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-contribution-trust-" + Guid.NewGuid().ToString("N"));
    private readonly ConsentStore _consent;
    private readonly AddinRegistry _registry;
    private readonly RevocationStore _revocations;
    private readonly LspClientRegistry _router = new([], NullLogger<LspClientRegistry>.Instance);
    private readonly ConversationLog _log = new();
    private readonly ExternalDebugService _debugger;
    private readonly DebugLaunchService _launches;
    private readonly ContributedProjects _types = new();
    private readonly RunChoiceStore _choices = new();
    private readonly HexIdeHost _host;
    private readonly AddinEventService _events;
    private readonly AddinDiagnosticsService _diagnostics;
    private readonly AddinOrigin _origin = new("probe", "Probe Add-in");
    private readonly ProjectDefinition _project = TestHelpers.CreateProject("Invoices");
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly ProjectRunnerService _runner;
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly List<string> _failures = [];
    private readonly List<string> _interpreterStarts = [];
    private int _configurations;
    private int _opens;

    public AddinContributionTrustTests()
    {
        Directory.CreateDirectory(_root);
        _consent = new ConsentStore(Path.Combine(_root, "consent.json"));
        _consent.Record("probe", "publisher", "hash", ConsentDecisions.Allow);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.Addins.DebugLaunchRefused" => "Debug adapter launch refused for add-in '{0}'.",
            "Str.ContributedProject.NoReader" => "Str.ContributedProject.NoReader {0}",
            "Str.Debug.RunWith.Unavailable" => "{0} isn't available",
            var key => key + " {0} {1}",
        });
        _revocations = new RevocationStore(null, _root);
        _registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), localization, _consent,
            _revocations, Path.Combine(_root, "state.json"), _root);
        var info = new AddinInfo(_root, "Probe Add-in", "", "1", "", Id: "probe", ManifestHash: "hash",
            Chain: new TrustChain { BuildVersion = "1", BuildHash = "hash", IsFirstParty = false,
                Publisher = new TrustLink("Publisher", "publisher", "key"),
                Intermediate = new TrustLink("Marketplace", "intermediate", "key"),
                Root = new TrustLink("Root", "root", "key") });
        // Seeded after package verification and initialization, as AddinServerTrustTests does: these are session changes.
        var contexts = (List<AddinContext>)typeof(AddinRegistry).GetField("_contexts",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_registry)!;
        contexts.Add(new AddinContext(info) { Status = AddinStatus.Loaded });

        _debugger = new ExternalDebugService(_log);
        _debugger.Failed += _failures.Add;
        _launches = new DebugLaunchService(_debugger);
        _projects.StartupProject.Returns(_project);
        _runner = new ProjectRunnerService(Substitute.For<IEventBus>(), Substitute.For<IWindowManager>(), _projects, localization,
            Substitute.For<IDebugController>(), Substitute.For<IBreakpointService>(), new WatchService(), new RunScope(), _debugger,
            debugLaunches: _launches, runChoices: _choices, statusBar: Substitute.For<IStatusBarService>());
        // The test project has no startup form, so F5 that goes to the interpreter fails at once and says so.
        _runner.StartFailed += _interpreterStarts.Add;

        var runner = Substitute.For<IProjectRunnerService>();
        _events = new AddinEventService(_projects, runner, new EventBus());
        _diagnostics = new AddinDiagnosticsService(_router, _projects);
        _host = new HexIdeHost(new AddinMenuService(), new AddinToolWindowService(), _events, _registry,
            new AddinEditorService(Substitute.For<IDocumentDockService>(), Substitute.For<IEditorService>(), _projects),
            new AddinProjectService(_projects), _diagnostics, new AddinCommandService(), new AddinProjectTemplateService(),
            new AddinOptionsService(), new ComponentRegistry(), runner,
            new LanguageServerContributions(_router, new LanguageServerRegistrationFactory(NullLoggerFactory.Instance),
                new LanguageServerConfigLoader(NullLogger<LanguageServerConfigLoader>.Instance), [],
                NullLogger<LanguageServerContributions>.Instance),
            _launches, _types);
    }

    public async ValueTask DisposeAsync()
    {
        _registry.Dispose();
        _diagnostics.Dispose();
        _events.Dispose();
        await _log.DisposeAsync();
        Directory.Delete(_root, true);
    }

    // A launch whose configuration names a relative executable: one that gets as far as being started fails there, on
    // "not a full path", which is the step before the adapter's process is started; one refused fails on the refusal.
    private IDisposable ContributeLaunch(Action? whileMaking = null) => _host.RegisterDebugLaunch(new AddinDebugLaunch("Probe")
    {
        CreateConfigurationAsync = (_, _) =>
        {
            _configurations++;
            whileMaking?.Invoke();
            return Task.FromResult(new AddinLaunchConfiguration
            {
                Executable = "probe-adapter.exe",
                AdapterId = "probe",
                Launch = JsonDocument.Parse("{}").RootElement,
            });
        },
    }, _origin);

    private void Disable(string how)
    {
        if (how == "disabled") _registry.SetEnabled(_root, false);
        else _registry.RevokeConsent("hash");
    }

    [AvaloniaFact]
    public async Task An_enabled_consented_add_in_s_launch_is_made_and_gets_as_far_as_its_start()
    {
        using var launch = ContributeLaunch();

        await _launches.StartAsync(_launches.Find(Key)!, _project);

        _configurations.Should().Be(1);
        _failures.Should().ContainSingle().Which.Should().Contain("not a full path", "the start got past both trust checks");
    }

    [AvaloniaTheory]
    [InlineData("disabled", false)]
    [InlineData("revoked", false)]
    [InlineData("disabled", true)]
    [InlineData("revoked", true)]
    public void Disabling_or_revoking_consent_takes_the_add_in_s_launch_out_and_F5_makes_and_starts_nothing(string how, bool registerAfter)
    {
        using var before = registerAfter ? null : ContributeLaunch();
        _choices.Set(_project, Key);
        Disable(how);
        using var after = registerAfter ? ContributeLaunch() : null;

        _runner.StartDefaultProject();

        _launches.Launches.Should().BeEmpty("the launch goes with the add-in, as its language servers do");
        _configurations.Should().Be(0, "the add-in is not asked for a configuration");
        _interpreterStarts.Should().ContainSingle("F5 falls back to the interpreter, as for a launch that is gone");
        _choices.Get(_project).Should().Be(Key, "the choice stays, for when the add-in is back");
    }

    [AvaloniaTheory]
    [InlineData("disabled")]
    [InlineData("revoked")]
    public async Task A_launch_whose_add_in_lost_its_trust_is_refused_before_it_is_asked_for_its_configuration(string how)
    {
        using var launch = ContributeLaunch();
        var contributed = _launches.Find(Key)!;
        // The launch as a start already holds it: the check at the start refuses it whatever took it out.
        if (how == "disabled")
            ((HashSet<string>)typeof(AddinRegistry).GetField("_disabled",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.GetValue(_registry)!).Add(_root);
        else
            _consent.Revoke("hash");

        await _launches.StartAsync(contributed, _project);

        _configurations.Should().Be(0);
        _failures.Should().ContainSingle().Which.Should().Be("Debug adapter launch refused for add-in 'Probe Add-in'.");
    }

    [AvaloniaTheory]
    [InlineData("disabled")]
    [InlineData("revoked")]
    public async Task A_launch_whose_add_in_lost_its_trust_while_making_its_configuration_starts_nothing(string how)
    {
        using var launch = ContributeLaunch(whileMaking: () => Disable(how));

        await _launches.StartAsync(_launches.Find(Key)!, _project);

        _configurations.Should().Be(1, "the add-in was trusted when it was asked");
        _failures.Should().ContainSingle().Which.Should().Be("Debug adapter launch refused for add-in 'Probe Add-in'.",
            "what it made is refused before it is started");
    }

    [AvaloniaTheory]
    [InlineData("disabled")]
    [InlineData("revoked")]
    public async Task Disabling_or_revoking_consent_takes_the_add_in_s_project_type_out_and_its_file_is_refused_unread(string how)
    {
        var projectFile = Path.Combine(_root, ".rdproj");
        File.WriteAllText(projectFile, "{}");
        using var type = _host.RegisterProjectType(new AddinProjectType("RDCore projects")
        {
            FileNames = [".rdproj"],
            OpenAsync = (_, _, _) =>
            {
                _opens++;
                return Task.FromResult(new AddinProjectContent("Ledger"));
            },
        }, _origin);
        Disable(how);

        await ProjectService().OpenProject(projectFile);

        _types.Types.Should().BeEmpty();
        _opens.Should().Be(0, "the add-in is not asked to read it");
        await _windows.Received(1).MessageBox("Str.ContributedProject.NoReader .rdproj",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>());
    }

    private ProjectService ProjectService()
    {
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() switch
        {
            "Str.ContributedProject.NoReader" => "Str.ContributedProject.NoReader {0}",
            var key => key + " {0} {1}",
        });
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<HexIDE.Forms.ViewModels.BaseEditorWindowViewModel>());
        _projects.LoadedProjects.Returns([]);
        return new ProjectService(() => throw new InvalidOperationException("no new-project dialog"), _windows,
            Substitute.For<IEventBus>(), _projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), strings,
            new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService()),
            contributed: _types);
    }
}
