using System;
using System.Linq;
using HexIDE.Runtime.Components;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.TypeLibrary;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Tests.ViewModels;

public class ObjectBrowserComponentLibraryTests
{
    private static (ObjectBrowserToolViewModel Browser, IProjectManager Projects) Create(ProjectDefinition project,
        ITypeLibraryService? types = null)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        types ??= Substitute.For<ITypeLibraryService>();
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.MemberOrigin.HexIde").Returns("HexIDE extension");
        return (new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), new ComponentRegistry(), types,
            Substitute.For<IFocusedProjectUtil>(), loc, Substitute.For<ILanguageConnectionRegistry>()), projects);
    }

    [Fact]
    public void Component_only_library_supplies_managed_members_without_an_OCX()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        var (browser, _) = Create(project);
        var library = browser.Libraries.Should().ContainSingle(l => l.Name == "MSComctlLib").Which;
        browser.SelectedLibrary = library;
        var progress = library.Classes.Should().ContainSingle(c => c.Name == "ProgressBar").Which;
        progress.Members.Should().ContainSingle(m => m.Name == "Increment")
            .Which.Description.Should().Be("HexIDE extension");
        progress.Members.Should().Contain(m => m.Name == "Value");
    }

    [Fact]
    public void Both_reference_kinds_share_one_library_and_type_library_loading()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var lib = ControlLibraries.MSComctlLib;
        project.AddObjectLine(lib.ProjectLine);
        project.AddReference(new(lib.Guid, lib.Version, 0, lib.File, lib.Name));
        var types = Substitute.For<ITypeLibraryService>();
        types.GetTypeLibInfoAsync(Arg.Any<VbReference>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TypeLibInfo?>(new(lib.Name, null, [new("NativeClass", TypeKind.Class, null, [])])));
        var (browser, _) = Create(project, types);
        browser.SelectedLibrary = browser.Libraries.Should().ContainSingle(l => l.Name == lib.Name).Which;
        browser.SelectedLibrary.Classes.Should().Contain(c => c.Name == "NativeClass");
        project.RemoveReference(project.References.Single());
        browser.Libraries.Should().ContainSingle(l => l.Name == lib.Name);
    }

    [Fact]
    public void Component_changes_and_project_unload_remove_owned_libraries()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var (browser, projects) = Create(project);
        var original = browser.Libraries.Select(l => l.Name).ToArray();
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        browser.Libraries.Should().Contain(l => l.Name == "MSComctlLib");
        project.RemoveObjectReference(ControlLibraries.MSComctlLib.Guid);
        browser.Libraries.Select(l => l.Name).Should().Equal(original);
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        projects.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);
        browser.Libraries.Should().NotContain(l => l.Name == "MSComctlLib");
    }

    [Fact]
    public void Added_form_header_adds_library_and_deleting_form_removes_it()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var (browser, _) = Create(project);
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        form.HeaderLines.Add(ControlLibraries.MSComctlLib.FormLine);
        project.AddForm(form);
        browser.Libraries.Should().ContainSingle(l => l.Name == "MSComctlLib");
        project.DeleteForm(form);
        browser.Libraries.Should().NotContain(l => l.Name == "MSComctlLib");
    }

    [Fact]
    public void Component_only_library_uses_available_type_metadata()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var lib = ControlLibraries.MSComctlLib;
        project.AddObjectLine(lib.ProjectLine);
        var types = Substitute.For<ITypeLibraryService>();
        types.GetTypeLibInfoAsync(Arg.Any<VbReference>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TypeLibInfo?>(new(lib.Name, null, [new("ProgressBar", TypeKind.Class, null, [])])));
        var (browser, _) = Create(project, types);
        browser.SelectedLibrary = browser.Libraries.Should().ContainSingle(l => l.Name == lib.Name).Which;
        browser.SelectedLibrary.Classes.Should().ContainSingle(c => c.Name == "ProgressBar")
            .Which.Members.Should().ContainSingle(m => m.Name == "Increment")
            .Which.Description.Should().Be("HexIDE extension");
    }

    [Theory]
    [InlineData("MSComctlLib")]
    [InlineData("ComctlLib")]
    [InlineData("TabDlg")]
    [InlineData("MSComDlg")]
    [InlineData("MSComCtl2")]
    [InlineData("MSMask")]
    [InlineData("RichTextLib")]
    public void Every_component_library_uses_the_registered_managed_catalogue(string name)
    {
        var runtimeLibrary = ControlLibraries.All.Single(l => l.Name == name);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(runtimeLibrary.ProjectLine);
        var (browser, _) = Create(project);
        var library = browser.Libraries.Should().ContainSingle(l => l.Name == name).Which;
        browser.SelectedLibrary = library;
        var supplied = new ComponentRegistry().LibraryControls.Where(c => c.Library == runtimeLibrary)
            .Select(c => c.ControlName).ToArray();
        if (supplied.Length > 0)
            library.Classes.Select(c => c.Name).Should().BeEquivalentTo(supplied);
        else
            library.Classes.Should().ContainSingle().Which.Members.Should().BeEmpty();
    }

    [Fact]
    public void No_components_adds_no_control_library()
    {
        var (browser, _) = Create(new(VBProjectType.EXE, "P"));
        browser.Libraries.Should().NotContain(l => ControlLibraries.All.Any(c => c.Name == l.Name));
    }
}
