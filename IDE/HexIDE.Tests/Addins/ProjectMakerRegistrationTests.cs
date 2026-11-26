using Avalonia.Threading;
using HexIDE.Addins;
using HexIDE.IDE;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Making;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.Addins;

/// <summary>
/// Who may register what makes a project type's programs, what the IDE asks it, and how a registration is withdrawn
/// (#3411 slice 2.8, #5621). A maker's code makes files on the developer's machine and starts VB6's compiler, so this is
/// deny by default, one per type, contained, and withdrawn without waiting on the UI thread, as the run engine's
/// registration is (#5616). Each test reads what the IDE then holds, not only what the call returned.
/// </summary>
/// <remarks>
/// The seam is internal to HexIDE.Core for phase 2, which NSubstitute can't proxy, so the makers here are written out.
/// </remarks>
public sealed class ProjectMakerRegistrationTests : CompositionTestBase
{
    private const string Vb6 = "hexide.vb6";

    private static AddinOrigin Owner(string id) => new(id, "Owner " + id) { IsComposed = true };

    private static RegistrationGrant Grant(string id, params string[] types) => new(id, Composed: true) { MakesProjectTypes = types };

    private static ProjectMakers Makers(params RegistrationGrant[] grants) => new() { Grants = grants };

    private static HexIdeHost Ide(ProjectMakers makers) =>
        new(null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!, null!,
            new ContributedProjects { Makers = makers }, new AddinResourceService(), new AddinLanguagePackService(),
            new AddinLanguageEditingService());

    private static ProjectDefinition Project(string? type = Vb6) => new(VBProjectType.EXE, "P") { ContributedBy = type };

    // A maker that records what it's asked, and throws where a test says.
    private sealed class FakeMaker(string type = Vb6) : IProjectMaker
    {
        public int TypeReads { get; private set; }
        public int ProgramPaths { get; private set; }
        public Exception? TypeThrows { get; init; }
        public Exception? UnavailableThrows { get; init; }
        public Exception? MakeThrows { get; init; }
        public Func<IProjectToolchain?> ToolchainRead { get; set; } = () => null;

        public string ProjectTypeId
        {
            get
            {
                TypeReads++;
                return TypeThrows is null ? type : throw TypeThrows;
            }
        }

        public (string Field, string Value)? RefusedProgramName(ProjectDefinition project) => null;

        public string ProgramPath(ProjectDefinition project)
        {
            ProgramPaths++;
            return "P.exe";
        }

        public MakeRefusal? Unavailable(string program) => UnavailableThrows is null ? null : throw UnavailableThrows;

        public IReadOnlyList<string> WrittenBeside(string program) => [];

        public void Make(ProjectDefinition project, string sources, string projectFile, string program)
        {
            if (MakeThrows is not null)
                throw MakeThrows;
        }

        public IProjectToolchain? Toolchain => ToolchainRead();
    }

    private sealed class FakeToolchain(Func<bool> installed) : IProjectToolchain
    {
        public bool IsInstalled => installed();

        public Task<ToolchainBuild?> BuildAsync(ProjectDefinition project, string program, Action<string> keepRecovery) =>
            Task.FromResult<ToolchainBuild?>(new ToolchainBuild(0, ""));

        public IReadOnlyList<(string Uri, Diagnostic Diagnostic)> Diagnostics(string output, ProjectDefinition project) => [];
    }

    [AvaloniaFact]
    public void An_owner_with_no_grant_is_refused_even_through_the_internal_member()
    {
        var makers = Makers(Grant("granted", Vb6), Grant("themes-only"));
        var maker = new FakeMaker();

        // The member is internal, which reflection reaches, so the grant is what refuses it.
        var stranger = (IProjectMakerRegistration)new AddinOwnedHost(Ide(makers), null!, Owner("stranger"));
        FluentActions.Invoking(() => stranger.RegisterProjectMaker(maker))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner stranger isn't one HexIDE lets make projects.");
        FluentActions.Invoking(() => makers.Register(maker, Owner("themes-only")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner themes-only isn't one HexIDE lets make projects.");
        // A package naming itself as the composed provider holds no composed grant.
        FluentActions.Invoking(() => makers.Register(maker, new AddinOrigin("granted", "Impostor")))
            .Should().Throw<InvalidOperationException>().WithMessage("Impostor isn't one HexIDE lets make projects.");

        maker.TypeReads.Should().Be(0, "a refused owner's code never runs");
        makers.Makes(Project()).Should().BeFalse("nothing makes VB6's projects");
    }

    [AvaloniaFact]
    public void A_granted_owner_may_make_only_the_types_its_grant_names()
    {
        var makers = Makers(Grant("granted", Vb6));

        FluentActions.Invoking(() => makers.Register(new FakeMaker("rdcore.project"), Owner("granted")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner granted isn't one HexIDE lets make rdcore.project projects.");
        makers.Makes(Project("rdcore.project")).Should().BeFalse();

        makers.Register(new FakeMaker(), Owner("granted"));
        makers.Makes(Project()).Should().BeTrue();
        makers.Makes(Project(type: null)).Should().BeTrue("a project naming no type is one HexIDE made, which it reads as VB6's");
        makers.Makes(Project("rdcore.project")).Should().BeFalse("a maker is offered only for its own type");
    }

    [AvaloniaFact]
    public void The_IDEs_own_host_refuses_it_with_no_owner_to_check()
    {
        var makers = Makers(Grant("granted", Vb6));
        var maker = new FakeMaker();

        FluentActions.Invoking(() => ((IProjectMakerRegistration)Ide(makers)).RegisterProjectMaker(maker))
            .Should().Throw<InvalidOperationException>().WithMessage("*only through the host HexIDE gives an add-in.");
        maker.TypeReads.Should().Be(0);
        makers.Makes(Project()).Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_second_maker_for_a_type_is_refused_naming_both_owners_and_the_first_stands()
    {
        var makers = Makers(Grant("a", Vb6), Grant("b", Vb6));
        var first = new FakeMaker();
        var second = new FakeMaker();
        makers.Register(first, Owner("a"));

        FluentActions.Invoking(() => makers.Register(second, Owner("b")))
            .Should().Throw<InvalidOperationException>()
            .WithMessage("Owner b can't make hexide.vb6 projects: Owner a (a) makes them already.");

        makers.For(Project())!.ProgramPath(Project());
        first.ProgramPaths.Should().Be(1, "the first maker still serves the type");
        second.ProgramPaths.Should().Be(0);
    }

    [AvaloniaFact]
    public void A_maker_whose_type_cant_be_read_registers_nothing()
    {
        var makers = Makers(Grant("a", Vb6));

        FluentActions.Invoking(() => makers.Register(new FakeMaker { TypeThrows = new InvalidOperationException("boom") }, Owner("a")))
            .Should().Throw<InvalidOperationException>().WithMessage("Owner a's maker didn't say which projects it makes.");
        makers.Makes(Project()).Should().BeFalse();
    }

    [AvaloniaFact]
    public void A_maker_that_throws_when_asked_answers_as_no_toolchain_and_stays_registered()
    {
        var makers = Makers(Grant("a", Vb6));
        var maker = new FakeMaker { ToolchainRead = () => throw new InvalidOperationException("boom") };
        makers.Register(maker, Owner("a"));

        makers.InstalledToolchainFor(Project()).Should().BeNull();

        maker.ToolchainRead = () => new FakeToolchain(() => throw new InvalidOperationException("boom"));
        makers.InstalledToolchainFor(Project()).Should().BeNull();

        maker.ToolchainRead = () => new FakeToolchain(() => true);
        makers.InstalledToolchainFor(Project()).Should().NotBeNull();
        makers.Makes(Project()).Should().BeTrue("a throw is contained, and the maker stays");
    }

    [AvaloniaFact]
    public void What_a_make_needs_from_a_maker_that_throws_goes_on_to_the_make_which_fails()
    {
        var makers = Makers(Grant("a", Vb6));
        makers.Register(new FakeMaker { UnavailableThrows = new InvalidOperationException("no runner") }, Owner("a"));

        FluentActions.Invoking(() => makers.For(Project())!.Unavailable("P.exe"))
            .Should().Throw<InvalidOperationException>().WithMessage("no runner");
        makers.Makes(Project()).Should().BeTrue();
    }

    [AvaloniaFact]
    public void Disposing_from_another_thread_never_waits_and_the_maker_is_asked_nothing_more()
    {
        var makers = Makers(Grant("a", Vb6));
        var changed = 0;
        makers.Changed += () => changed++;
        var registration = makers.Register(new FakeMaker(), Owner("a"));
        Dispatcher.UIThread.RunJobs();
        changed.Should().Be(1, "registering is announced, on the UI thread");

        // The UI thread is held in Wait here, so a dispose that waited on it would never return.
        Task.Run(registration.Dispose).Wait(TimeSpan.FromSeconds(10)).Should().BeTrue("a dispose that waited on the UI thread would hang");

        makers.Makes(Project()).Should().BeFalse("a withdrawn maker is asked nothing from the moment its handle is disposed");
        makers.For(Project()).Should().BeNull();
        changed.Should().Be(1, "the rest is posted, not run under the caller's lock");
        Dispatcher.UIThread.RunJobs();

        changed.Should().Be(2);
        makers.Register(new FakeMaker(), Owner("a")).Should().NotBeNull("once let go, its type is free again");
    }

    [AvaloniaFact]
    public void A_handle_disposed_late_never_takes_out_a_maker_registered_after_it()
    {
        var makers = Makers(Grant("a", Vb6));
        var first = makers.Register(new FakeMaker(), Owner("a"));
        first.Dispose();
        Dispatcher.UIThread.RunJobs();
        makers.Register(new FakeMaker(), Owner("a"));

        first.Dispose();
        Dispatcher.UIThread.RunJobs();

        makers.Makes(Project()).Should().BeTrue();
    }

    // The provider's other registrations go to a substitute: several of them reach the application's own resources and
    // text, which other tests in the run also change. Its maker goes through the owned host, under HexIDE's real grants.
    [AvaloniaFact]
    public void The_provider_registers_VB6s_maker_through_the_owned_host_and_withdraws_it_when_disposed()
    {
        var makers = new ProjectMakers();
        var owned = (IProjectMakerRegistration)new AddinOwnedHost(Ide(makers), null!, HexIDE.Tests.Projects.TestMakers.BasicProvider);
        var provider = new HexIDE.Runtime.BasicProvider();

        provider.Initialize(new MakerHost(Substitute.For<IHexIdeHost>(), owned));

        makers.Makes(Project()).Should().BeTrue();
        makers.For(Project())!.ProjectTypeId.Should().Be(Vb6);
        FluentActions.Invoking(() => owned.RegisterProjectMaker(new FakeMaker()))
            .Should().Throw<InvalidOperationException>().WithMessage("*HexIDE.Basic (hexide.basic) makes them already.");

        provider.Dispose();
        Dispatcher.UIThread.RunJobs();

        makers.Makes(Project()).Should().BeFalse("disabling the provider withdraws its maker");
    }

    [AvaloniaFact]
    public void A_host_that_isnt_one_HexIDE_gives_an_add_in_gets_no_maker()
    {
        using var provider = new HexIDE.Runtime.BasicProvider();

        FluentActions.Invoking(() => provider.Initialize(Substitute.For<IHexIdeHost>())).Should().NotThrow();
    }

    // A host that takes everything else the provider registers, as a substitute, and forwards its maker.
    private sealed class MakerHost(IHexIdeHost inner, IProjectMakerRegistration makers) : IHexIdeHost, IProjectMakerRegistration
    {
        public IMenuContributor Menus => inner.Menus;
        public IToolWindowContributor ToolWindows => inner.ToolWindows;
        public IAddinEvents Events => inner.Events;
        public IAddinRegistry Registry => inner.Registry;
        public IEditorAccess Editor => inner.Editor;
        public IProjectAccess Project => inner.Project;
        public IDiagnosticsAccess Diagnostics => inner.Diagnostics;
        public IOptionsPageContributor Options => inner.Options;
        public IServerChatTools ServerChatTools => inner.ServerChatTools;
        public IDisposable RegisterCommand(string id, string label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
            inner.RegisterCommand(id, label, onInvoke, gesture, canExecute);
        public IDisposable RegisterCommand(string id, AddinTitle label, Action onInvoke, string? gesture = null, Func<bool>? canExecute = null) =>
            inner.RegisterCommand(id, label, onInvoke, gesture, canExecute);
        public IDisposable RegisterComponent(IComponentClass component) => inner.RegisterComponent(component);
        public IDisposable RegisterToolboxItem(IComponentClass component, string? iconKey = null) =>
            inner.RegisterToolboxItem(component, iconKey);
        public IDisposable RegisterControlLibrary(IControlLibrary library, Func<IReadOnlyList<IComponentClass>> controls) =>
            inner.RegisterControlLibrary(library, controls);
        public IDisposable RegisterProjectTemplate(IAddinProjectTemplate template) => inner.RegisterProjectTemplate(template);
        public bool RunProject() => inner.RunProject();
        public bool StopProject() => inner.StopProject();
        public IAddinServerConnection RegisterLanguageServer(AddinLanguageServer server) => inner.RegisterLanguageServer(server);
        public IDisposable RegisterDebugLaunch(AddinDebugLaunch launch) => inner.RegisterDebugLaunch(launch);
        public IDisposable RegisterProjectType(AddinProjectType type) => inner.RegisterProjectType(type);
        public IDisposable RegisterResources(Uri dictionary) => inner.RegisterResources(dictionary);
        public IDisposable RegisterLanguagePacks(Uri folder) => inner.RegisterLanguagePacks(folder);
        public IDisposable RegisterDesignerPictures(Func<IDesignerDrawingSurface, IDesignerPictures> pictures) =>
            inner.RegisterDesignerPictures(pictures);
        public IDisposable RegisterDesignPositions(IDesignPositions positions) => inner.RegisterDesignPositions(positions);
        public IDisposable RegisterProjectMaker(IProjectMaker maker) => makers.RegisterProjectMaker(maker);
    }

    private static (ProjectService Service, IWindowManager Windows) Service(ProjectMakers makers, string exe)
    {
        var windows = Substitute.For<IWindowManager>();
        windows.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>()).Returns(exe);
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>() + " {0}");
        // VB6's type, registered as the composition registers it, with these makers.
        var types = new ContributedProjects { Makers = makers };
        HexIDE.Tests.Infrastructure.Vb6Types.Register(types);
        var service = new ProjectService(() => throw new InvalidOperationException(), windows, Substitute.For<IEventBus>(),
            Substitute.For<IProjectManager>(), Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
            Substitute.For<HexIDE.Sidecar.IUserSidecarService>(), new FileBaselineStore(), localization,
            Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(), contributed: types);
        return (service, windows);
    }

    private static ProjectDefinition MadeProject()
    {
        var project = Project();
        var module = new ModuleDefinition(project, "Module1", ModuleKind.StandardModule);
        module.UpdateCode("Sub Main()\nEnd Sub");
        project.AddModule(module);
        return project;
    }

    [AvaloniaFact]
    public async Task A_maker_that_throws_while_making_fails_the_Make_which_is_never_reported_as_made()
    {
        var folder = Directory.CreateTempSubdirectory("hexide-maker-throws-").FullName;
        try
        {
            var makers = Makers(Grant(RegistrationGrants.BasicProvider, Vb6));
            makers.Register(new FakeMaker { MakeThrows = new IOException("disk full") }, HexIDE.Tests.Projects.TestMakers.BasicProvider);
            var exe = Path.Join(folder, "Made.exe");
            var (service, windows) = Service(makers, exe);
            var project = MadeProject();

            var make = () => service.MakeProject(project);

            await make.Should().ThrowAsync<IOException>();
            var outcome = await MakeOutcomes.Latest!.Ended.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            outcome.ProgramPath.Should().BeNull();
            outcome.WhyNot.Should().Be("Make failed: disk full");
            await windows.Received(1).MessageBox("Str.MakeProject.Failed disk full", icon: MessageBoxIcon.Error);
            project.Modules[0].AbsolutePath.Should().BeNull("the paths Make repointed are put back");
            File.Exists(exe).Should().BeFalse();
            makers.Makes(project).Should().BeTrue("the IDE stays usable, and so does the maker's registration");
        }
        finally
        {
            Directory.Delete(folder, true);
        }
    }

    [AvaloniaFact]
    public async Task With_no_maker_a_Make_reached_directly_is_refused_and_logged_with_no_box()
    {
        var (service, windows) = Service(Makers(), Path.Join(Path.GetTempPath(), "never.exe"));

        var make = () => service.MakeProject(MadeProject());

        await make.Should().ThrowAsync<OperationCanceledException>();
        var outcome = await MakeOutcomes.Latest!.Ended.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        outcome.WhyNot.Should().Be("nothing loaded makes hexide.vb6 projects, so P wasn't made");
        await windows.DidNotReceive().SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>());
        await windows.DidNotReceiveWithAnyArgs().MessageBox(default!);
    }

    // The compiler comes from the user's machine, never from a project (#5621, condition 4): VB6_EXE, then VB6's install
    // folders, read once when the toolchain is made. The project reaches the start only as the path VB6 is told to build, so a
    // project file naming other programs changes nothing, and the start below is the only one the toolchain makes.
    [Fact]
    public void Nothing_in_a_project_names_the_program_Make_with_VB6_starts()
    {
        var previous = Environment.GetEnvironmentVariable("VB6_EXE");
        Environment.SetEnvironmentVariable("VB6_EXE", Environment.ProcessPath);
        Vb6Toolchain toolchain;
        try { toolchain = new Vb6Toolchain(); }
        finally { Environment.SetEnvironmentVariable("VB6_EXE", previous); }
        var folder = Directory.CreateTempSubdirectory("hexide-compiler-").FullName;
        try
        {
            var vbp = Path.Join(folder, "P.vbp");
            File.WriteAllText(vbp, "Type=Exe\r\nExeName32=\"C:\\Windows\\System32\\calc.exe\"\r\nVB6_EXE=\"C:\\evil\\VB6.EXE\"\r\n");

            var start = toolchain.StartInfo(vbp, null, "log.txt");

            start.FileName.Should().Be(Environment.ProcessPath);
            start.UseShellExecute.Should().BeFalse();
            start.Arguments.Should().Be($"/make \"{vbp}\" /out \"log.txt\"");
        }
        finally
        {
            Directory.Delete(folder, true);
        }

        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Join(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        var source = File.ReadAllText(Path.Join(dir!.FullName, "IDE/HexIDE.Runtime/Making/Vb6Toolchain.cs"));
        System.Text.RegularExpressions.Regex.Matches(source, @"Process\.Start\(|new ProcessStartInfo|new Process\(").Select(m => m.Value)
            .Should().Equal(["Process.Start("], "the toolchain starts the compiler in one place");
        source.Should().Contain("Process.Start(StartInfo(projectPath, outputDirectory, logPath))");
    }
}
