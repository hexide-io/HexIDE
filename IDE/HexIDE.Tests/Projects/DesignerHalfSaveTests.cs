using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// Ctrl+S from a UserControl's or PropertyPage's designer, or from its code window, saves the <c>.ctl</c> or
/// <c>.pag</c> (hexide-io/HexIDE#474).
/// </summary>
/// <remarks>
/// Both doors save the designer half, a <see cref="FormDefinition"/>. A new one has no file until the project is saved
/// (#500), so the form's own save asked for a <c>.frm</c> and wrote the edit there, beside a module whose file the
/// project names and which was never written. A save that starts from a designer half is its module's save.
/// </remarks>
public class DesignerHalfSaveTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-designerhalf-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();
    private readonly List<Avalonia.Platform.Storage.FilePickerSaveOptions> asked = new();

    public DesignerHalfSaveTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private ProjectService MakeService(IEventBus? bus = null, IHeaderRefresher? refresher = null)
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
            .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));
        // Every picker is answered with the name it suggests, in this test's folder, and recorded.
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>(), Arg.Any<string?>())
            .Returns(ci =>
            {
                var options = ci.Arg<Avalonia.Platform.Storage.FilePickerSaveOptions>();
                asked.Add(options);
                return Task.FromResult<string?>(Path.Join(dir, options.SuggestedFileName));
            });
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            bus ?? Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(),
            refresher ?? Substitute.For<IHeaderRefresher>(), new ComponentRegistry());
    }

    private ProjectDefinition SavedProject()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1") { AbsolutePath = Path.Join(dir, "Project1.vbp") };
        loaded.Add(project);
        return project;
    }

    private static Task<ModuleDefinition> AddNew(ProjectService svc, ProjectDefinition project, string kind) =>
        kind == "UserControl"
            ? svc.AddNewUserControl(project, "UserControl1", interactive: false)
            : svc.AddNewPropertyPage(project, "PropertyPage1", interactive: false);

    // A new UserControl's or PropertyPage's root is its kind's class, whose Properties window lists VB6's properties for
    // it, not a form's (#1585).
    [Theory]
    [InlineData("UserControl", "VB.UserControl")]
    [InlineData("PropertyPage", "VB.PropertyPage")]
    public async Task A_new_designer_halfs_root_is_its_kinds(string kind, string typeName)
    {
        var module = await AddNew(MakeService(), SavedProject(), kind);

        var root = module.FormPart!.Components.Single(c => c.BaseClass is HexIDE.Runtime.Components.FormComponentClass);
        root.BaseClass.VBTypeName.Should().Be(typeName);
        root.BaseClass.PropertiesByName.Should().NotContainKeys("Icon", "Left", "Top", "Visible");
    }

    [Theory]
    [InlineData("UserControl", "UserControl1.ctl", "Begin VB.UserControl UserControl1 ")]
    [InlineData("PropertyPage", "PropertyPage1.pag", "Begin VB.PropertyPage PropertyPage1 ")]
    public async Task Saving_a_new_designer_half_asks_for_its_module_file_and_writes_it(string kind, string file, string begin)
    {
        var svc = MakeService();
        var module = await AddNew(svc, SavedProject(), kind);

        (await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false)).Should().BeTrue();

        asked.Should().ContainSingle().Which.SuggestedFileName.Should().Be(file, "the file the project names is the module's");
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().BeEquivalentTo([file], "no .frm beside it");
        File.ReadAllText(Path.Join(dir, file)).Should().Contain(begin);
        module.AbsolutePath.Should().Be(Path.Join(dir, file));
        module.FormPart!.AbsolutePath.Should().Be(Path.Join(dir, file), "both halves are one file, as a load gives them");
    }

    [Theory]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task The_next_save_from_the_designer_half_writes_the_same_file_without_asking(string kind)
    {
        var svc = MakeService();
        var module = await AddNew(svc, SavedProject(), kind);
        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);
        var written = module.AbsolutePath!;
        module.UpdateCode(module.Code + "Public Sub Added()\r\nEnd Sub\r\n");

        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);

        asked.Should().ContainSingle("the second save has the file the first chose");
        File.ReadAllText(written).Should().Contain("Public Sub Added()");
        Directory.GetFiles(dir).Should().ContainSingle();
    }

    // #1661: Control rows measured in docs/vb6-fidelity-oracle/a-usercontrols-and-a-propertypages-own-events.md.
    // EXE and already-True rows guard unchanged behaviour; the EXE row is not a VB6 measurement.
    [Theory]
    [InlineData("Control", "PropertyPage", "False", "True", false)]
    [InlineData("Control", "PropertyPage", "False", "True", true)]
    [InlineData("Control", "UserControl", "False", "False", false)]
    [InlineData("Exe", "PropertyPage", "False", "False", false)]
    [InlineData("Control", "PropertyPage", "True", "True", false)]
    public async Task Saving_a_designer_half_reconciles_only_a_Control_PropertyPages_exposure(
        string projectType, string kind, string exposed, string expected, bool checkModel)
    {
        var svc = MakeService();
        var name = kind + "1";
        var file = Path.Join(dir, name + (kind == "PropertyPage" ? ".pag" : ".ctl"));
        var original = $"VERSION 5.00\r\nBegin VB.{kind} {name} \r\nEnd\r\n"
            + $"Attribute VB_Name = \"{name}\"\r\n"
            + "Attribute VB_GlobalNameSpace = False\r\nAttribute VB_Creatable = True\r\n"
            + $"Attribute VB_PredeclaredId = False\r\nAttribute VB_Exposed = {exposed}\r\n"
            + "Option Explicit\r\n";
        File.WriteAllText(file, original);
        var vbp = Path.Join(dir, "Project1.vbp");
        File.WriteAllText(vbp, $"Type={projectType}\r\n{kind}={Path.GetFileName(file)}\r\nName=\"Project1\"\r\n");
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        var codeBefore = module.Code;

        (await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false)).Should().BeTrue();

        if (checkModel)
            module.Code.Should().Be(codeBefore.Replace("VB_Exposed = " + exposed, "VB_Exposed = " + expected));
        else
            File.ReadAllBytes(file).Should().Equal(System.Text.Encoding.UTF8.GetBytes(
                original.Replace("VB_Exposed = " + exposed, "VB_Exposed = " + expected)));
    }

    // #1661 decides the first-save rule; Add Property Page itself remains unmeasured in a Control project.
    [Fact]
    public async Task A_new_Control_PropertyPage_keeps_its_default_until_its_first_save()
    {
        var svc = MakeService();
        var project = new ProjectDefinition(VBProjectType.Control, "Project1") { AbsolutePath = Path.Join(dir, "Project1.vbp") };
        loaded.Add(project);
        var module = await AddNew(svc, project, "PropertyPage");
        module.Code.Should().Contain("Attribute VB_Exposed = False");

        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);

        module.Code.Should().Contain("Attribute VB_Exposed = True");
        File.ReadAllText(module.AbsolutePath!).Should().Contain("Attribute VB_Exposed = True");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Saved_exposure_is_adopted_by_an_open_code_window(bool withMemberRewrite)
    {
        var bus = new EventBus();
        var dock = Substitute.For<IDocumentDockService>();
        using var refresher = new HeaderRefresher(bus, dock, new BreakpointService(), new BookmarkService());
        var svc = MakeService(bus, refresher);
        var project = new ProjectDefinition(VBProjectType.Control, "Project1") { AbsolutePath = Path.Join(dir, "Project1.vbp") };
        loaded.Add(project);
        var module = await AddNew(svc, project, "PropertyPage");
        if (withMemberRewrite)
            module.UpdateCode(module.Code + "Public Sub Current()\r\nAttribute Old.VB_Description = \"kept\"\r\nEnd Sub\r\n");
        using var editor = new CodeEditorViewModel(windowManager, Substitute.For<IEditorService>(), svc, bus,
            Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(), Substitute.For<IRunScope>(),
            Substitute.For<ILocalizationService>()).Initialize(module);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });

        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);

        editor.BufferBody.Should().Be(module.Code);
        editor.Document.Text.Should().Be(File.ReadAllText(module.AbsolutePath!));
        editor.BufferBody.Should().Contain("Attribute VB_Exposed = True");
        if (withMemberRewrite)
            editor.BufferBody.Should().Contain("Attribute Current.VB_Description");
        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);
        editor.Document.Text.Should().Be(File.ReadAllText(module.AbsolutePath!));
    }

    [Fact]
    public async Task Save_As_from_a_designer_half_asks_for_a_ctl()
    {
        // Save As selects the existing .ctl; agree so the test reaches the module writer (#4505).
        windowManager.MessageBox(Arg.Any<string>(), buttons: MessageBoxButtons.YesNo, icon: MessageBoxIcon.Warning,
            layout: MessageBoxLayout.SecondButtonDefault).Returns(MessageBoxResult.Yes);
        var svc = MakeService();
        var module = await AddNew(svc, SavedProject(), "UserControl");
        await svc.SaveForm(module.FormPart!, saveAs: false, interactive: false);

        await svc.SaveForm(module.FormPart!, saveAs: true, interactive: false);

        asked.Should().HaveCount(2);
        asked[1].SuggestedFileName.Should().EndWith(".ctl");
        Directory.GetFiles(dir).Select(Path.GetFileName).Should().NotContain(f => f!.EndsWith(".frm"));
    }
}
