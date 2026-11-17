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
        loc.GetString("Str.Tool.ObjectBrowser.MembersUnavailable").Returns("Localized unavailable members");
        return (new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), new ComponentRegistry(), types,
            FocusedProject(project), loc, Substitute.For<ILanguageConnectionRegistry>()), projects);
    }

    private static IFocusedProjectUtil FocusedProject(ProjectDefinition project)
    {
        var focused = Substitute.For<IFocusedProjectUtil>();
        focused.FocusedOrStartupProject.Returns(project);
        return focused;
    }

    // VB6 SP6 measurement: https://github.com/hexide-io/HexIDE/issues/4113 (#4202).
    [Theory]
    [InlineData("ObProbe")]
    [InlineData("obProbe")]
    public void Project_library_sorts_with_every_other_library_ignoring_case(string projectName)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, projectName);
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        project.AddObjectLine(ControlLibraries.RichTextLib.ProjectLine);
        var (browser, _) = Create(project);

        browser.Libraries.Skip(1).Select(l => l.Name).Should().Equal(
            "MSComctlLib", projectName, "RichTextLib", "VB", "VBA");
        browser.SelectedLibrary.Should().BeSameAs(browser.Libraries[0]);
    }

    [Fact]
    public void Reordering_libraries_preserves_the_selected_library()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "ObProbe");
        var (browser, _) = Create(project);
        var selected = browser.Libraries.Single(l => l.Name == project.Name);
        browser.SelectedLibrary = selected;

        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);

        browser.SelectedLibrary.Should().BeSameAs(selected);
    }

    private const string ThirdPartyGuid = "{A1234567-1234-1234-1234-123456789ABC}";
    private const string ThirdPartyLine = "Object=" + ThirdPartyGuid + "#1.2#1033; VendorGrid.ocx";

    [Fact]
    public void Third_party_component_loads_by_identity_and_uses_metadata_name_and_members()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ThirdPartyLine);
        var types = Substitute.For<ITypeLibraryService>();
        types.GetTypeLibInfoAsync(Arg.Any<VbReference>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<TypeLibInfo?>(new("VendorControls", null,
                [new("Grid", TypeKind.Class, null, [new("Refresh", HexIDE.TypeLibrary.MemberKind.Method, "Sub Refresh()", null, false)])])));
        var (browser, _) = Create(project, types);
        var library = browser.Libraries.Should().ContainSingle(l => l.Name == "VendorControls").Which;
        library.Classes.Should().ContainSingle().Which.Members.Should().ContainSingle(m => m.Name == "Refresh");
        types.Received(1).GetTypeLibInfoAsync(Arg.Is<VbReference>(r => r.Guid == ThirdPartyGuid
            && r.Version == "1.2" && r.Lcid == 1033 && r.LibPath == null && r.Name == "VendorGrid"),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Unreadable_third_party_component_remains_visible_with_placeholder()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ThirdPartyLine);
        var (browser, _) = Create(project);
        var library = browser.Libraries.Should().ContainSingle(l => l.Name == "VendorGrid").Which;
        browser.SelectedLibrary = library;
        library.Classes.Should().ContainSingle().Which.Name.Should().Be("(Type metadata unavailable)");
    }

    [Fact]
    public void Third_party_reference_and_component_share_entry_after_reference_removal()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ThirdPartyLine);
        var reference = new VbReference(ThirdPartyGuid.Trim('{', '}').ToLowerInvariant(), "1.2", 1033, null, "VendorControls");
        project.AddReference(reference);
        var (browser, _) = Create(project);
        browser.Libraries.Should().ContainSingle(l => l.Reference != null);
        project.RemoveReference(reference);
        browser.Libraries.Should().ContainSingle(l => l.Reference != null).Which.Name.Should().Be("VendorGrid");
    }

    [Fact]
    public void Removing_third_party_component_line_removes_library()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ThirdPartyLine);
        var (browser, _) = Create(project);
        browser.Libraries.Should().ContainSingle(l => l.Name == "VendorGrid");
        project.RemoveObjectReference(ThirdPartyGuid);
        browser.Libraries.Should().NotContain(l => l.Name == "VendorGrid");
    }

    [Fact]
    public void Third_party_form_header_adds_library_until_form_or_project_is_removed()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var (browser, projects) = Create(project);
        var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
        form.HeaderLines.Add("Object = \"" + ThirdPartyGuid + "#1.2#1033\"; \"VendorGrid.ocx\"");
        project.AddForm(form);
        browser.Libraries.Should().ContainSingle(l => l.Name == "VendorGrid");
        project.DeleteForm(form);
        browser.Libraries.Should().NotContain(l => l.Name == "VendorGrid");
        project.AddForm(form);
        projects.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);
        browser.Libraries.Should().NotContain(l => l.Name == "VendorGrid");
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
    public void Every_declared_class_appears_exactly_once_without_metadata(string name)
    {
        var runtimeLibrary = ControlLibraries.All.Single(l => l.Name == name);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(runtimeLibrary.ProjectLine);
        var (browser, _) = Create(project);
        var library = browser.Libraries.Single(l => l.Name == name);
        browser.SelectedLibrary = library;
        library.Classes.Select(c => c.Name).Should().BeEquivalentTo(
            runtimeLibrary.ControlClasses.Concat(runtimeLibrary.Controls.Keys).Distinct(StringComparer.OrdinalIgnoreCase));
        library.Classes.Select(c => c.Name).Should().OnlyHaveUniqueItems()
            .And.BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        library.Classes.Should().OnlyContain(c => c.Kind == OBClassKind.ClassModule);
    }

    [Theory]
    [InlineData("TreeView")]
    [InlineData("TabStrip")]
    [InlineData("Toolbar")]
    [InlineData("Node")]
    [InlineData("ListItem")]
    public void Common_controls_include_controls_and_object_classes(string name)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        var (browser, _) = Create(project);
        browser.SelectedLibrary = browser.Libraries.Single(l => l.Name == "MSComctlLib");
        browser.SelectedLibrary.Classes.Should().ContainSingle(c => c.Name == name);
    }

    [Fact]
    public void TreeView_supplies_its_component_properties_and_events()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        var (browser, _) = Create(project);
        browser.SelectedLibrary = browser.Libraries.Single(l => l.Name == "MSComctlLib");
        browser.SelectedClass = browser.SelectedLibrary.Classes.SingleOrDefault(c => c.Name == "TreeView");
        browser.FilteredMembers.Should().Contain(m => m.Name == "TabStop" && m.Kind == OBMemberKind.Property)
            .And.Contain(m => m.Name == "NodeClick" && m.Kind == OBMemberKind.Event);
    }

    [Fact]
    public void TreeView_properties_use_code_names_without_designer_rows()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        var (browser, _) = Create(project);
        browser.SelectedLibrary = browser.Libraries.Single(l => l.Name == "MSComctlLib");
        browser.SelectedClass = browser.SelectedLibrary.Classes.Single(c => c.Name == "TreeView");
        browser.FilteredMembers.Should().ContainSingle(m => m.Name == "Name" && m.Signature == "Property Name");
        browser.FilteredMembers.Should().NotContain(m => m.Name.Contains('(') || m.Name.Contains(')'));
    }

    [Fact]
    public void Every_declared_control_has_a_component_model_or_an_explicit_gap()
    {
        string[] notYetModelled = ["ImageCombo", "Animation"];
        foreach (var library in ControlLibraries.All)
        foreach (var name in library.Controls.Keys)
        {
            var component = library.FindComponentClass(name);
            if (notYetModelled.Contains(name))
                component.Should().BeNull($"{library.Name}.{name} has no component model yet");
            else
                component.Should().NotBeNull($"{library.Name}.{name} needs a component model or an explicit gap");
        }
    }

    [Theory]
    [InlineData("TabStrip", "Tabs")]
    [InlineData("Toolbar", "Buttons")]
    public void Controls_outside_the_toolbox_supply_their_member_tables(string name, string member)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        var (browser, _) = Create(project);
        browser.SelectedLibrary = browser.Libraries.Single(l => l.Name == "MSComctlLib");
        browser.SelectedClass = browser.SelectedLibrary.Classes.SingleOrDefault(c => c.Name == name);
        browser.FilteredMembers.Should().Contain(m => m.Name == member && m.Kind == OBMemberKind.Property);
    }

    [Fact]
    public void Class_without_a_member_list_shows_a_localized_members_line()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddObjectLine(ControlLibraries.MSComctlLib.ProjectLine);
        var (browser, _) = Create(project);
        browser.SelectedLibrary = browser.Libraries.Single(l => l.Name == "MSComctlLib");
        browser.SelectedClass = browser.SelectedLibrary.Classes.SingleOrDefault(c => c.Name == "ButtonMenu");
        var line = browser.FilteredMembers.Should().ContainSingle().Which;
        line.Name.Should().Be("Localized unavailable members");
        line.Kind.Should().Be(OBMemberKind.Status);
        line.Signature.Should().BeEmpty();
    }

    [Fact]
    public void No_components_adds_no_control_library()
    {
        var (browser, _) = Create(new(VBProjectType.EXE, "P"));
        browser.Libraries.Should().NotContain(l => ControlLibraries.All.Any(c => c.Name == l.Name));
    }
}
