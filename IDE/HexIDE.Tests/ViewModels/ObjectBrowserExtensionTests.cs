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

public class ObjectBrowserExtensionTests
{
    [Theory]
    [InlineData("ListBox")]
    [InlineData("ComboBox")]
    public void Intrinsic_search_members_are_listed_with_the_shared_extension_description(string control)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([]);
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.MemberOrigin.HexIde").Returns("HexIDE extension; not available in VB6.");
        var vm = new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), InBoxRegistry.Create(), Substitute.For<ITypeLibraryService>(),
            Substitute.For<IFocusedProjectUtil>(), loc, Substitute.For<ILanguageConnectionRegistry>());
        var cls = vm.Libraries.Single(l => l.Name == "VB").Classes.Single(c => c.Name == control);
        foreach (var name in new[] { "FindString", "FindStringExact" })
        {
            var member = cls.Members.Should().ContainSingle(m => m.Name == name).Which;
            member.Kind.Should().Be(OBMemberKind.Method);
            member.Description.Should().Be("HexIDE extension; not available in VB6.");
        }
        cls.Members.Select(m => m.Name).Should().OnlyHaveUniqueItems();
    }

    // Extension visibility and shared origin description: #1116's first-slice decision.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ProgressBar_Increment_is_listed_as_a_HexIDE_extension(bool version5)
    {
        var library = version5 ? ControlLibraries.ComctlLib : ControlLibraries.MSComctlLib;
        var reference = new VbReference(library.Guid, library.Version, 0, null, library.Name);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        project.AddReference(reference);
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var types = Substitute.For<ITypeLibraryService>();
        types.GetTypeLibInfoAsync(reference, Arg.Any<CancellationToken>()).Returns(Task.FromResult<TypeLibInfo?>(
            new(library.Name, null, [new("ProgressBar", TypeKind.Class, null, [])])));
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.MemberOrigin.HexIde").Returns("HexIDE extension; not available in VB6.");
        var vm = new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), InBoxRegistry.Create(), types,
            Substitute.For<IFocusedProjectUtil>(), loc, Substitute.For<ILanguageConnectionRegistry>());
        var browserLibrary = vm.Libraries.Single(l => l.Name == library.Name);
        vm.SelectedLibrary = browserLibrary;
        var member = browserLibrary.Classes.Single(c => c.Name == "ProgressBar")
            .Members.Should().ContainSingle(m => m.Name == "Increment").Which;
        member.Kind.Should().Be(OBMemberKind.Method);
        member.Description.Should().Be("HexIDE extension; not available in VB6.");
    }
}
