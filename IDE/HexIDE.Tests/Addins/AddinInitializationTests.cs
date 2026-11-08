using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;

namespace HexIDE.Tests.Addins;

public sealed class AddinInitializationTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-initialize-" + Guid.NewGuid().ToString("N"));
    private readonly AddinMenuService _menus = new();
    private readonly AddinToolWindowService _tools = new();
    private readonly AddinCommandService _commands = new();
    private readonly AddinOptionsService _options = new();
    private readonly AddinProjectTemplateService _templates = new();
    private readonly ComponentRegistry _components = new();
    private readonly EventBus _bus = new();
    private readonly IProjectRunnerService _runner = Substitute.For<IProjectRunnerService>();
    private readonly IProjectManager _projects = Substitute.For<IProjectManager>();
    private readonly IHexIdeHost _host = Substitute.For<IHexIdeHost>();
    private readonly AddinEventService _events;
    private readonly AddinRegistry _registry;

    public AddinInitializationTests()
    {
        Directory.CreateDirectory(_root);
        _events = new AddinEventService(_projects, _runner, _bus);
        _host.Menus.Returns(_menus);
        _host.ToolWindows.Returns(_tools);
        _host.Options.Returns(_options);
        _host.Events.Returns(_events);
        _host.RegisterCommand(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<Action>(), null, null)
            .Returns(c => _commands.Register(c.ArgAt<string>(0), c.ArgAt<string>(1), c.ArgAt<Action>(2), null, null));
        _host.When(h => h.RegisterComponent(Arg.Any<IComponentClass>()))
            .Do(c => _components.Register(c.Arg<IComponentClass>()));
        _host.When(h => h.RegisterProjectTemplate(Arg.Any<IAddinProjectTemplate>()))
            .Do(c => _templates.Register(c.Arg<IAddinProjectTemplate>()));
        _registry = new AddinRegistry(Substitute.For<IPackageVerifier>(), Substitute.For<ISettingsService>(),
            Substitute.For<IDeveloperModeService>(), Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>(),
            new ConsentStore(Path.Combine(_root, "consent.json")),
            new RevocationStore(null, Path.Combine(_root, "revocations")), Path.Combine(_root, "state.json"), _root);
    }

    public void Dispose()
    {
        _registry.Dispose();
        _events.Dispose();
        Directory.Delete(_root, true);
    }

    [Theory]
    [InlineData("menus")]
    [InlineData("tools")]
    [InlineData("commands")]
    [InlineData("options")]
    [InlineData("components")]
    [InlineData("templates")]
    [InlineData("events")]
    [InlineData("dispose")]
    public void Throwing_eager_factory_removes_every_earlier_contribution(string surface)
    {
        var component = Substitute.For<IComponentClass>();
        var template = Substitute.For<IAddinProjectTemplate>();
        var notifications = 0;
        var reachedAfterFactory = false;
        var addin = Substitute.For<IAddin>();
        addin.When(a => a.Initialize(Arg.Any<IHexIdeHost>())).Do(_ =>
        {
            _host.Menus.Add("AddIns/Failed", "Earlier menu", null, () => { });
            _host.ToolWindows.Register("earlier", "Earlier tool", null, () => new object()).Show();
            _host.RegisterCommand("earlier", "Earlier command", () => { });
            _host.Options.RegisterOptionsPage(() => new object());
            _host.RegisterComponent(component);
            _host.RegisterProjectTemplate(template);
            _host.Events.ProjectLoaded += _ => notifications++;
            _host.Events.ProjectUnloaded += _ => notifications++;
            _host.Events.FileOpened += _ => notifications++;
            _host.Events.FileClosed += _ => notifications++;
            _host.Events.RunStarted += () => notifications++;
            _host.Events.RunStopped += () => notifications++;
            _host.ToolWindows.Register("broken", "Broken tool", null, () => throw new InvalidOperationException("factory failed"));
            reachedAfterFactory = true;
        });
        var ctx = new AddinContext(new AddinInfo("failed", "Failed", "", "", ""));
        _registry.InitializeAddin(ctx, addin, AddinStatus.Loaded, _host);

        ctx.Status.Should().Be(AddinStatus.Error);
        ctx.ErrorMessage.Should().Be("factory failed");
        reachedAfterFactory.Should().BeFalse("factories remain eager");
        switch (surface)
        {
            case "menus": _menus.AddInsItems.Should().BeEmpty(); break;
            case "tools": _tools.Tools.Should().BeEmpty(); break;
            case "commands": _commands.Commands.Should().BeEmpty(); break;
            case "options": _options.GetFactoryFor(typeof(AddinInitializationTests).Assembly.Location).Should().BeNull(); break;
            case "components": _components.Components.Should().NotContain(component); break;
            case "templates": _templates.Templates.Should().BeEmpty(); break;
            case "events":
                _projects.ProjectLoaded += Raise.Event<Action<HexIDE.Runtime.ProjectElements.ProjectDefinition>>(
                    new HexIDE.Runtime.ProjectElements.ProjectDefinition(VBProjectType.EXE, "Project1"));
                _projects.ProjectUnloaded += Raise.Event<Action<HexIDE.Runtime.ProjectElements.ProjectDefinition>>(
                    new HexIDE.Runtime.ProjectElements.ProjectDefinition(VBProjectType.EXE, "Project1"));
                _bus.Publish(new HexIDE.Events.FileOpenedEvent("File"));
                _bus.Publish(new HexIDE.Events.FileClosedEvent("File"));
                _runner.IsRunning.Returns(true);
                _runner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                    _runner, new PropertyChangedEventArgs(nameof(IProjectRunnerService.IsRunning)));
                _runner.IsRunning.Returns(false);
                _runner.PropertyChanged += Raise.Event<PropertyChangedEventHandler>(
                    _runner, new PropertyChangedEventArgs(nameof(IProjectRunnerService.IsRunning)));
                notifications.Should().Be(0);
                break;
            case "dispose": addin.Received(1).Dispose(); break;
        }
    }
    [Fact]
    public void Cleanup_failures_preserve_the_factory_error_and_do_not_stop_rollback_or_the_next_addin()
    {
        var failed = Substitute.For<IAddin>();
        failed.When(a => a.Initialize(Arg.Any<IHexIdeHost>())).Do(_ =>
        {
            _host.Menus.Add("Tools", "Failed", null, () => { });
            _host.ToolWindows.Register("failed", "Failed", null, () => new object());
            _host.ToolWindows.Register("broken", "Broken", null, () => throw new InvalidOperationException("factory failed"));
        });
        failed.When(a => a.Dispose()).Do(_ => throw new InvalidOperationException("dispose failed"));
        _tools.ToolUnregistered += _ => throw new InvalidOperationException("observer failed");
        var ctx = new AddinContext(new AddinInfo("failed", "Failed", "", "", ""));
        _registry.InitializeAddin(ctx, failed, AddinStatus.Loaded, _host);

        var healthy = Substitute.For<IAddin>();
        healthy.When(a => a.Initialize(Arg.Any<IHexIdeHost>())).Do(_ => _host.Menus.Add("Tools", "Healthy", null, () => { }));
        var next = new AddinContext(new AddinInfo("healthy", "Healthy", "", "", ""));
        _registry.InitializeAddin(next, healthy, AddinStatus.Loaded, _host);

        ctx.ErrorMessage.Should().Be("factory failed");
        ctx.Instance.Should().BeNull();
        _tools.Tools.Should().BeEmpty();
        _menus.ToolsItems.Should().ContainSingle().Which.Header.Should().Be("Healthy");
        next.Status.Should().Be(AddinStatus.Loaded);
        healthy.DidNotReceive().Dispose();
    }

    [AvaloniaFact]
    public void Rollback_removes_the_toolbox_entry_and_preserves_a_previously_loaded_addin()
    {
        var toolbox = new HexIDE.VisualDesigner.ToolBoxToolViewModel(
            Substitute.For<IDocumentDockService>(), _components, Substitute.For<ILocalizationService>());
        var previous = _tools.Register("previous", "Previous", null, () => new object());
        var component = HexIDE.Runtime.Components.PictureBoxComponentClass.Instance;
        var originalCount = toolbox.Components.Count;
        var removed = 0;
        _tools.ToolUnregistered += _ => removed++;
        var failed = Substitute.For<IAddin>();
        failed.When(a => a.Initialize(Arg.Any<IHexIdeHost>())).Do(_ =>
        {
            _host.RegisterComponent(component);
            toolbox.SelectedComponent = toolbox.Components.Last();
            _host.ToolWindows.Register("failed", "Failed", null, () => new object());
            _host.ToolWindows.Register("broken", "Broken", null, () => throw new InvalidOperationException("factory failed"));
        });
        _registry.InitializeAddin(new AddinContext(new AddinInfo("failed", "Failed", "", "", "")),
            failed, AddinStatus.Loaded, _host);

        toolbox.Components.Count.Should().Be(originalCount);
        toolbox.SelectedComponent.Should().BeSameAs(toolbox.Arrow);
        _components.Components.Count(c => ReferenceEquals(c, component)).Should().Be(1);
        _tools.Tools.Should().ContainSingle().Which.Id.Should().Be("previous");
        removed.Should().Be(1);
        previous.Dispose();
    }

    [Fact]
    public void ToolWindowOwnership_SurvivesInitialize_AndAllowsSameIdAcrossAddins()
    {
        IHexIdeHost? firstHost = null;
        IHexIdeHost? secondHost = null;
        var first = Substitute.For<IAddin>();
        var second = Substitute.For<IAddin>();
        first.When(a => a.Initialize(Arg.Any<IHexIdeHost>())).Do(c => firstHost = c.Arg<IHexIdeHost>());
        second.When(a => a.Initialize(Arg.Any<IHexIdeHost>())).Do(c => secondHost = c.Arg<IHexIdeHost>());
        _registry.InitializeAddin(new AddinContext(new AddinInfo("first", "First", "", "", "", Id: "first")),
            first, AddinStatus.Loaded, _host);
        _registry.InitializeAddin(new AddinContext(new AddinInfo("second", "Second", "", "", "", Id: "second")),
            second, AddinStatus.Loaded, _host);

        using var one = firstHost!.ToolWindows.Register("output", "First", null, () => new object());
        using var two = secondHost!.ToolWindows.Register("output", "Second", null, () => new object());
        _tools.Tools.Select(t => t.Id).Should().OnlyHaveUniqueItems();
        var duplicate = () => firstHost.ToolWindows.Register("output", "Duplicate", null, () => new object());
        duplicate.Should().Throw<InvalidOperationException>();
        one.Dispose();
        using var replacement = firstHost.ToolWindows.Register("output", "Replacement", null, () => new object());
        _tools.Tools.Should().HaveCount(2);
    }

}
