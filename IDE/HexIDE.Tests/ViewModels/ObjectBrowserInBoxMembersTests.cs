using System.Runtime.CompilerServices;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.TypeLibrary;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// Every member the Object Browser lists for an in-box class or library, against a listing generated from the code before
/// move slice 2.4d (#5610) took those members out of the runtime's in-box types. The members it lists, their kinds and their
/// HexIDE-only notes must not change while they move.
/// </summary>
public class ObjectBrowserInBoxMembersTests
{
    private const string ListingName = "ObjectBrowserInBoxMembers.txt";

    private static ObjectBrowserToolViewModel Browser(ProjectDefinition project, ITypeLibraryService types)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns([project]);
        var focused = Substitute.For<IFocusedProjectUtil>();
        focused.FocusedOrStartupProject.Returns(project);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => "⟦" + call.Arg<string>() + "⟧");
        return new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(), Substitute.For<IEditorService>(),
            InBoxRegistry.Create(), types, focused, localization, Substitute.For<ILanguageConnectionRegistry>());
    }

    // A member with no note ends in '-', so no line ends in whitespace.
    private static IEnumerable<string> Lines(string library, OBLibraryViewModel lib) =>
        from cls in lib.Classes
        from member in cls.Members
        select string.Join('\t', library, cls.Name, member.Name, member.Kind, member.Signature, member.Description ?? "-");

    // The VB library's classes, then each in-box library when no type library answers, then each when one answers with its
    // controls' names and no members, which leaves only the members HexIDE adds.
    private static List<string> Listing()
    {
        var lines = new List<string>();
        var none = Substitute.For<ITypeLibraryService>();
        var vb = Browser(new ProjectDefinition(VBProjectType.EXE, "P"), none);
        lines.AddRange(Lines("VB", vb.Libraries.Single(l => l.Name == "VB")));

        foreach (var library in ControlLibraries.All)
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            project.AddObjectLine(library.ProjectLine);
            var browser = Browser(project, none);
            var lib = browser.Libraries.Single(l => l.Name == library.Name);
            browser.SelectedLibrary = lib;
            lines.AddRange(Lines(library.Name + " (no type library)", lib));
        }

        var registry = InBoxRegistry.Create();
        foreach (var library in ControlLibraries.All)
        {
            var project = new ProjectDefinition(VBProjectType.EXE, "P");
            project.AddObjectLine(library.ProjectLine);
            var answered = Substitute.For<ITypeLibraryService>();
            List<TypeInfo> controls =
            [
                .. registry.LibraryControls.Where(control => ReferenceEquals(control.Library, library))
                    .Select(control => new TypeInfo(control.ControlName, TypeKind.Class, null, [])),
            ];
            answered.GetTypeLibInfoAsync(Arg.Any<VbReference>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult<TypeLibInfo?>(new(library.Name, null, controls)));
            var browser = Browser(project, answered);
            var lib = browser.Libraries.Single(l => l.Name == library.Name);
            browser.SelectedLibrary = lib;
            lines.AddRange(Lines(library.Name + " (type library)", lib));
        }
        return lines;
    }

    private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

    // The Object Browser finds a table through the interface, so a class that keeps one without saying so loses its members.
    [Fact]
    public void Every_class_that_keeps_a_member_table_declares_it()
    {
        var keeping = typeof(ComponentBaseClass).Assembly.GetTypes()
            .Where(type => typeof(ComponentBaseClass).IsAssignableFrom(type)
                && type.GetProperty(nameof(ComponentBaseClass.Members))!.DeclaringType != typeof(ComponentBaseClass))
            .ToList();

        keeping.Should().NotBeEmpty();
        keeping.Where(type => !typeof(IComponentMemberTable).IsAssignableFrom(type)).Should().BeEmpty();
    }

    private sealed class EmptyTable : IComponentClass, IComponentMemberTable
    {
        public string Name => "Gauge";
        public string VBTypeName => "VB.Gauge";
        public string ClassName => "Gauge";
        public bool IsVisual => true;
        public IReadOnlyList<PropertyClass> Properties => [VBProperties.NameProperty];
        public IReadOnlyDictionary<string, PropertyClass> PropertiesByName => new Dictionary<string, PropertyClass>();
        public IReadOnlyList<EventClass> Events => [];
        public IReadOnlyList<PropertyChange> ApplyDesignerConsequences(ComponentInstance instance, PropertyClass property) => [];
        public IReadOnlyList<ComponentMember> MembersIn(IControlLibrary? library) => [];
    }

    // As before #5610: the VB library describes a class whose table is empty by its properties and events.
    [Fact]
    public void A_registered_class_with_an_empty_table_is_listed_by_its_properties()
    {
        var registry = InBoxRegistry.Create();
        registry.Register(new EmptyTable(), InBoxRegistry.Provider);
        var projects = Substitute.For<IProjectManager>();
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        projects.LoadedProjects.Returns([project]);
        var focused = Substitute.For<IFocusedProjectUtil>();
        focused.FocusedOrStartupProject.Returns(project);
        var browser = new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(), Substitute.For<IEditorService>(),
            registry, Substitute.For<ITypeLibraryService>(), focused, Substitute.For<ILocalizationService>(),
            Substitute.For<ILanguageConnectionRegistry>());

        browser.Libraries.Single(l => l.Name == "VB").Classes.Single(c => c.Name == "Gauge").Members
            .Select(m => (m.Name, m.Kind)).Should().Equal([(VBProperties.NameProperty.Name, OBMemberKind.Property)]);
    }

    [Fact]
    public void Every_in_box_member_the_Object_Browser_lists_matches_the_listing()
    {
        var actual = Listing();
        var root = Path.GetFullPath(Path.Combine(Here(), "..", "..", ".."));
        var written = Path.Combine(root, "artifacts", "object-browser-members", ListingName);
        Directory.CreateDirectory(Path.GetDirectoryName(written)!);
        File.WriteAllLines(written, actual);

        var listing = Path.Combine(Here(), ListingName);
        File.Exists(listing).Should().BeTrue($"{listing} holds the members to compare; copy {written} there to make it");
        var expected = File.ReadAllLines(listing);

        actual.Should().HaveCountGreaterThan(500, "every in-box class and library lists its members");
        actual.Should().Equal(expected, $"the Object Browser's members must not change; this run's listing is at {written}");
    }
}
