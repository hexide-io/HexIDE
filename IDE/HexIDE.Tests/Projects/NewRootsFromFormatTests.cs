using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Addins;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Sidecar;
using HexIDE.Tests.Infrastructure;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A new form, UserControl, PropertyPage or menu item takes its class from the project's format (#3411 slice 2.4f-2), and
/// a new project's Form1 from the format its template is told it's made for. A format that can't make one, or answers
/// with a class in another role, refuses the add and changes nothing.
/// </summary>
public class NewRootsFromFormatTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-newroots-" + Guid.NewGuid().ToString("N"));
    private readonly List<ProjectDefinition> loaded = new();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public NewRootsFromFormatTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    // A form class of its own, so a test can tell the format's answer from VB6's.
    private sealed class TaggedFormClass : FormComponentClass;

    // A menu class of its own, for the same reason.
    private sealed class TaggedMenuClass : MenuComponentClass;

    // A template written before 2.4f-2, which makes its project by name only.
    private sealed class NameOnlyTemplate : IAddinProjectTemplate
    {
        public string Name => "Name only";
        public bool Supported => true;
        public List<string> Made { get; } = new();

        public ProjectDefinition Create(string name)
        {
            Made.Add(name);
            return new ProjectDefinition(VBProjectType.EXE, name);
        }
    }

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(() => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            new LocalizationService(),
            Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create());
    }

    private ProjectDefinition NewProject(IProjectFileFormat format)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Project1")
            { AbsolutePath = Path.Join(dir, "Project1.vbp"), Format = format };
        loaded.Add(project);
        return project;
    }

    private static async Task<object> Add(ProjectService service, ProjectDefinition project, string typeName, bool interactive) =>
        typeName switch
        {
            "VB.Form" => await service.AddNewForm(project, "Thing1", interactive),
            "VB.UserControl" => await service.AddNewUserControl(project, "Thing1", interactive),
            _ => (object)await service.AddNewPropertyPage(project, "Thing1", interactive),
        };

    private static IComponentClass RootOf(object added) =>
        added is FormDefinition form ? form.Components[0].BaseClass : ((ModuleDefinition)added).FormPart!.Components[0].BaseClass;

    // ── VB6's format ─────────────────────────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VB.Form")]
    [InlineData("VB.UserControl")]
    [InlineData("VB.PropertyPage")]
    public void Vb6s_format_gives_the_class_of_each_of_its_three_roots(string typeName) =>
        new Vb6ProjectFileFormat().NewRootClass(typeName).Should().BeSameAs(FormComponentClass.ForRoot(typeName));

    // FormComponentClass.ForRoot reads any other name as a form, which the format must not: the IDE would then add a form
    // in place of a root VB6's format doesn't make.
    [Theory]
    [InlineData("VB.MDIForm")]
    [InlineData("VB.Frame")]
    [InlineData("Form")]
    [InlineData("")]
    public void Vb6s_format_gives_no_class_for_any_other_root(string typeName) =>
        new Vb6ProjectFileFormat().NewRootClass(typeName).Should().BeNull();

    [Fact]
    public void Vb6s_format_gives_the_menu_class() =>
        new Vb6ProjectFileFormat().NewMenuClass.Should().BeSameAs(MenuComponentClass.Instance);

    // ── Add Form, Add User Control, Add Property Page ────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("VB.Form")]
    [InlineData("VB.UserControl")]
    [InlineData("VB.PropertyPage")]
    public async Task A_new_root_takes_the_class_the_projects_format_gives(string typeName)
    {
        var asked = new List<string>();
        var tagged = new TaggedFormClass();
        var format = new RegionStandInFormat
        {
            RootClassAnswer = name =>
            {
                asked.Add(name);
                return name == "VB.Form" ? tagged : FormComponentClass.ForRoot(name);
            },
        };
        var project = NewProject(format);

        var added = await Add(MakeService(), project, typeName, interactive: false);

        asked.Should().Equal(typeName);
        RootOf(added).Should().BeSameAs(typeName == "VB.Form" ? tagged : FormComponentClass.ForRoot(typeName));
    }

    public static TheoryData<string, bool> Refusals => new()
    {
        { "VB.Form", false }, { "VB.Form", true },
        { "VB.UserControl", false }, { "VB.UserControl", true },
        { "VB.PropertyPage", false }, { "VB.PropertyPage", true },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public async Task A_format_that_cant_make_the_root_refuses_the_add_and_changes_nothing(string typeName, bool wrongRole)
    {
        // A class in another role is refused as no class is: a format can't make a form of a CommandButton.
        var format = new RegionStandInFormat
        {
            RootClassAnswer = _ => wrongRole ? CommandButtonComponentClass.Instance : null,
        };
        var project = NewProject(format);
        var service = MakeService();

        var refusal = await Record.ExceptionAsync(() => Add(service, project, typeName, interactive: false));

        refusal.Should().BeOfType<InvalidOperationException>().Which.Message.Should()
            .Be($"The file format of Project1 can't make a new {typeName}, so nothing was changed.");
        project.Forms.Should().BeEmpty();
        project.Modules.Should().BeEmpty();
    }

    [Fact]
    public async Task An_interactive_add_is_told_why_it_was_refused()
    {
        var project = NewProject(new RegionStandInFormat { RootClassAnswer = _ => null });

        var refusal = await Record.ExceptionAsync(() => MakeService().AddNewForm(project, "Form1"));

        refusal.Should().BeOfType<OperationCanceledException>();
        await windowManager.Received(1).MessageBox("The file format of Project1 can't make a new VB.Form, so nothing was changed.",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), MessageBoxIcon.Information, Arg.Any<Avalonia.Controls.Window?>(),
            Arg.Any<MessageBoxLayout>());
        project.Forms.Should().BeEmpty();
    }

    // ── A new project's template ─────────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void The_Standard_EXE_template_makes_Form1_from_the_format_it_is_made_for()
    {
        var tagged = new TaggedFormClass();
        var format = new RegionStandInFormat { RootClassAnswer = name => name == "VB.Form" ? tagged : null };

        var project = new ProjectManager(Substitute.For<IEditorService>(), Substitute.For<IEventBus>())
            .NewProject(Vb6ProjectType.StandardExe, "Project1", format: format);

        project.Forms.Single().Components[0].BaseClass.Should().BeSameAs(tagged);
    }

    [Fact]
    public void The_Standard_EXE_template_refuses_to_make_Form1_for_no_format()
    {
        var made = () => Vb6ProjectType.StandardExe.Create("Project1");

        made.Should().Throw<InvalidOperationException>().WithMessage("*given none*");
    }

    [Fact]
    public void The_Standard_EXE_template_refuses_a_format_that_gives_no_form_class()
    {
        var made = () => Vb6ProjectType.StandardExe.Create("Project1", new RegionStandInFormat { RootClassAnswer = _ => null });

        made.Should().Throw<InvalidOperationException>().WithMessage("*gives no form class*");
    }

    // The default member keeps a template written before 2.4f-2 working: told a format, it makes the project it always made.
    [Fact]
    public void A_template_that_makes_a_project_by_name_only_still_makes_one_for_a_format()
    {
        var template = new NameOnlyTemplate();

        var project = new ProjectManager(Substitute.For<IEditorService>(), Substitute.For<IEventBus>())
            .NewProject(template, "Project1", format: new Vb6ProjectFileFormat());

        template.Made.Should().Equal("Project1");
        project.Name.Should().Be("Project1");
    }

    // ── The Menu Editor ──────────────────────────────────────────────────────────────────────────────────────────

    private async Task<(FormDefinition Form, IWindowManager Windows)> AddMenuItem(Func<IComponentClass?> menuClass)
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        project.Format = new RegionStandInFormat { MenuClassAnswer = menuClass };
        var form = project.Forms.First();
        var windows = Substitute.For<IWindowManager>();
        using var designer = new FormEditViewModel(null!, new EventBus(), Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), windows, Substitute.For<ISettingsService>(),
            new LocalizationService()).Initialize(form);
        windows.ShowDialog(Arg.Any<IDialog>()).Returns(call =>
        {
            var editor = (MenuEditorViewModel)call[0];
            editor.SelectedMenu!.Caption = "File";
            editor.SelectedMenu.Name = "mnuFile";
            editor.Next();
            editor.Accept();
            return Task.FromResult(true);
        });
        await designer.EditMenu();
        return (form, windows);
    }

    [AvaloniaFact]
    public async Task A_new_menu_item_takes_the_class_the_forms_format_gives()
    {
        var tagged = new TaggedMenuClass();

        var (form, _) = await AddMenuItem(() => tagged);

        form.Components.Should().ContainSingle(c => c.BaseClass.Role == ComponentRole.Menu)
            .Which.BaseClass.Should().BeSameAs(tagged);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task A_format_that_makes_no_menus_refuses_the_Menu_Editors_changes(bool wrongRole)
    {
        var (form, windows) = await AddMenuItem(() => wrongRole ? CommandButtonComponentClass.Instance : null);

        // Only the form itself: no menu, and not the CommandButton a wrong answer named.
        form.Components.Should().ContainSingle().Which.BaseClass.Role.Should().Be(ComponentRole.Form);
        await windows.Received(1).MessageBox("The file format of P can't make a new VB.Menu, so nothing was changed.",
            Arg.Any<string?>(), Arg.Any<MessageBoxButtons>(), MessageBoxIcon.Information, Arg.Any<Avalonia.Controls.Window?>(),
            Arg.Any<MessageBoxLayout>());
    }
}
