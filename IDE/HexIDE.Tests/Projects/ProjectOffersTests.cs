using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Recovery;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Projects;

/// <summary>
/// The IDE asks what a project's type offers, not who opened the project (#3598). Each project here says one thing about
/// who opened it and another about what it offers, so a gate that still asked the first would fail.
/// </summary>
public class ProjectOffersTests : IDisposable
{
    private readonly string _dir = Path.Join(Path.GetTempPath(), "hexide-offers-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> _loaded = [];
    private readonly IWindowManager _windows = Substitute.For<IWindowManager>();
    private readonly ProjectService _service;

    public ProjectOffersTests()
    {
        Directory.CreateDirectory(_dir);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(_ => _loaded);
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var strings = Substitute.For<ILocalizationService>();
        strings.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>().StartsWith("Str.ContributedProject.", StringComparison.Ordinal) ? call.Arg<string>() + " {0} {1}" : call.Arg<string>());
        var dock = Substitute.For<IDocumentDockService>();
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());
        _service = new ProjectService(() => throw new InvalidOperationException("no new-project dialog"), _windows,
            Substitute.For<IEventBus>(), projects, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(), strings,
            new HeaderRefresher(new EventBus(), dock, new BreakpointService(), new BookmarkService(), new Vb6ProjectFileFormat()),
            new Vb6ProjectFileFormat(), new ComponentRegistry());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (IOException) { /* best effort */ }
    }

    public static ProjectOffers Without(string offer) => offer switch
    {
        nameof(ProjectOffers.WritesProjectFile) => ProjectOffers.Everything with { WritesProjectFile = false },
        nameof(ProjectOffers.Makes) => ProjectOffers.Everything with { Makes = false },
        nameof(ProjectOffers.Runs) => ProjectOffers.Everything with { Runs = false },
        nameof(ProjectOffers.ChangesMembership) => ProjectOffers.Everything with { ChangesMembership = false },
        nameof(ProjectOffers.SetsProperties) => ProjectOffers.Everything with { SetsProperties = false },
        nameof(ProjectOffers.SetsReferences) => ProjectOffers.Everything with { SetsReferences = false },
        nameof(ProjectOffers.SetsComponents) => ProjectOffers.Everything with { SetsComponents = false },
        _ => throw new ArgumentOutOfRangeException(nameof(offer), offer, null),
    };

    private string ProjectFile => Path.Join(_dir, "Ledger.vbp");

    /// <summary>A project on disk with one module. Opened by an add-in, as far as its name says, only when asked.</summary>
    private ProjectDefinition Project(ProjectOffers offers, bool saysAnAddInOpenedIt = false)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Ledger")
        {
            AbsolutePath = ProjectFile,
            ContributedBy = saysAnAddInOpenedIt ? "rdcore" : null,
            ContributorTitle = saysAnAddInOpenedIt ? "RDCore for HexIDE" : null,
            Offers = offers,
        };
        var module = new ModuleDefinition(project, "Startup", ModuleKind.StandardModule) { AbsolutePath = Path.Join(_dir, "Startup.bas") };
        module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        project.AddModule(module);
        _loaded.Add(project);
        return project;
    }

    private async Task WasTold(string key, int times = 1) =>
        await _windows.Received(times).MessageBox(Arg.Is<string>(text => text.Contains(key)),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Window?>(), Arg.Any<bool>());

    private async Task WasNotTold(string key) =>
        await _windows.DidNotReceive().MessageBox(Arg.Is<string>(text => text.Contains(key)),
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>(), Arg.Any<Window?>(), Arg.Any<bool>());

    [Fact]
    public void A_project_offers_everything_unless_its_type_says_otherwise() =>
        new ProjectDefinition(VBProjectType.EXE, "Ledger").Offers.Should().Be(ProjectOffers.Everything);

    [Fact]
    public void An_add_in_s_type_offers_nothing_yet() =>
        new ContributedProjectType(new HexIDE.Addins.AddinProjectType("Probe") { FileNames = [".probe"], OpenAsync = (_, _, _) => throw new NotSupportedException() }, "probe", "Probe add-in")
            .Offers.Should().Be(ProjectOffers.Nothing);

    [AvaloniaTheory]
    [InlineData(nameof(ProjectOffers.SetsProperties))]
    [InlineData(nameof(ProjectOffers.SetsReferences))]
    [InlineData(nameof(ProjectOffers.SetsComponents))]
    public async Task Each_setting_asks_its_own_question(string denied)
    {
        var project = Project(Without(denied));

        await _service.EditProjectProperties(project);
        await _service.EditProjectReferences(project);
        await _service.EditProjectComponents(project);

        await WasTold("Str.ContributedProject.SettingsReadOnly");
        await _windows.Received(2).ShowDialog(Arg.Any<IDialog>());
    }

    [AvaloniaFact]
    public async Task Settings_are_open_on_a_project_that_says_an_add_in_opened_it_and_offers_them()
    {
        var project = Project(ProjectOffers.Everything, saysAnAddInOpenedIt: true);

        await _service.EditProjectProperties(project);
        await _service.EditProjectReferences(project);
        await _service.EditProjectComponents(project);

        await WasNotTold("Str.ContributedProject.SettingsReadOnly");
        await _windows.Received(3).ShowDialog(Arg.Any<IDialog>());
    }

    [AvaloniaFact]
    public async Task An_add_is_refused_by_what_the_type_offers_not_by_who_opened_it()
    {
        var refused = Project(Without(nameof(ProjectOffers.ChangesMembership)));
        await ((Func<Task>)(() => _service.AddNewModule(refused, "Module1", ModuleKind.StandardModule))).Should().ThrowAsync<OperationCanceledException>();
        refused.Modules.Should().ContainSingle();
        await WasTold("Str.ContributedProject.MembershipReadOnly");
        _loaded.Clear();

        var allowed = Project(ProjectOffers.Everything, saysAnAddInOpenedIt: true);
        await _service.AddNewModule(allowed, "Module1", ModuleKind.StandardModule);
        allowed.Modules.Should().HaveCount(2);
    }

    [AvaloniaFact]
    public async Task Removing_a_member_asks_what_the_type_offers()
    {
        var refused = Project(Without(nameof(ProjectOffers.ChangesMembership)));
        await _service.RemoveDocument(DocumentIdentity.For(refused.Modules[0]));
        refused.Modules.Should().ContainSingle();
        await WasTold("Str.ContributedProject.MembershipReadOnly");
        _windows.ClearReceivedCalls();
        _loaded.Clear();

        var allowed = Project(ProjectOffers.Everything, saysAnAddInOpenedIt: true);
        try { await _service.RemoveDocument(DocumentIdentity.For(allowed.Modules[0])); }
        catch (OperationCanceledException) { /* past the refusal, the save prompt answers nothing here */ }
        await WasNotTold("Str.ContributedProject.MembershipReadOnly");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Crash_recovery_adds_a_member_only_where_the_type_offers_membership(bool offered)
    {
        var project = Project(offered ? ProjectOffers.Everything : Without(nameof(ProjectOffers.ChangesMembership)), saysAnAddInOpenedIt: offered);

        var added = _service.AddRecovered(project, new UnsavedDocument("Lost", nameof(ModuleKind.StandardModule), null,
            Vb6TextEncoding.Utf8WithBom, new UnsavedFile("Lost.bas", "Attribute VB_Name = \"Lost\"\r\n")));

        (added is not null).Should().Be(offered);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_save_writes_the_project_file_only_where_the_type_offers_it(bool offered)
    {
        var project = Project(offered ? ProjectOffers.Everything : Without(nameof(ProjectOffers.WritesProjectFile)), saysAnAddInOpenedIt: offered);

        await _service.SaveProject(project, saveAs: false);

        File.Exists(project.Modules[0].AbsolutePath).Should().BeTrue("a module is saved whatever the type offers");
        File.Exists(ProjectFile).Should().Be(offered);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Make_asks_what_the_type_offers(bool offered)
    {
        var project = Project(offered ? ProjectOffers.Everything : Without(nameof(ProjectOffers.Makes)), saysAnAddInOpenedIt: offered);

        try { await _service.MakeProject(project); }
        catch (OperationCanceledException) { /* a project that goes on stops at the picker, which answers nothing here */ }

        if (offered)
            await WasNotTold("Str.ContributedProject.NotMade");
        else
            await WasTold("Str.ContributedProject.NotMade");
    }

    [Fact]
    public void No_product_code_decides_by_who_opened_a_project() =>
        ProductSource.FilesMatching(new Regex(@"\bContributedBy\s*(is\b|==|!=|:)"))
            .Should().BeEmpty("the IDE asks what a project's type offers, and ContributedBy only names who opened it");
}
