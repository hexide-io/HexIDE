using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The read-only project document. These cover the two things that are easy to get wrong and impossible
/// to see in a screenshot: that a project with no file still renders, and that closing the tab stops it
/// listening to a project that outlives it.
/// </summary>
public class ProjectDocumentViewModelTests
{
    /// <summary>
    /// Echoes the key back as its own value, so an assertion names the key that was asked for rather than
    /// a translation that could change underneath it.
    /// </summary>
    private static ILocalizationService Localization()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return localization;
    }

    private static ProjectDocumentViewModel Open(ProjectDefinition project) =>
        new ProjectDocumentViewModel(Localization()).Initialize(project);

    [Fact]
    public void AProjectWithNoFileStillRenders()
    {
        // TestHelpers projects are never saved, so AbsolutePath is null — the case that has no .vbp at
        // all to show, and the reason this document renders the model rather than the file.
        var project = TestHelpers.CreateProject("Unsaved");

        var vm = Open(project);

        vm.HasLocation.Should().BeFalse();
        vm.Location.Should().Be("Str.ProjectDocument.NotSaved");
        vm.ProjectName.Should().Be("Unsaved");
        vm.Title.Should().Be("Unsaved", "with no file to name it, the project's own name is the title");
    }

    [Fact]
    public void AMemberAddedAfterOpeningAppears()
    {
        var project = TestHelpers.CreateProject();
        var vm = Open(project);
        var before = vm.Members.Count;

        project.AddModule(new ModuleDefinition(project, "Added", ModuleKind.StandardModule));

        vm.Members.Should().HaveCount(before + 1, "the model is live, so the document tracks it with no watcher");
        vm.Members.Should().Contain(m => m.Name == "Added");
    }

    [Fact]
    public void ModulesAreNamedByKindRatherThanLumpedTogether()
    {
        var project = TestHelpers.CreateProject();
        project.AddModule(new ModuleDefinition(project, "Plain", ModuleKind.StandardModule));
        project.AddModule(new ModuleDefinition(project, "Klass", ModuleKind.ClassModule));

        var vm = Open(project);

        vm.Members.Should().Contain(m => m.Name == "Plain" && m.Kind == "Str.AddItem.Module");
        vm.Members.Should().Contain(m => m.Name == "Klass" && m.Kind == "Str.AddItem.ClassModule");
    }

    [Fact]
    public void DisposingStopsItListeningToAProjectThatOutlivesIt()
    {
        // A closed tab still subscribed keeps the whole view model alive for as long as the project is
        // loaded. Nothing in a screenshot or a UI test would ever show that, which is why it is asserted.
        var project = TestHelpers.CreateProject();
        var vm = Open(project);
        var afterOpen = vm.Members.Count;

        vm.Dispose();
        project.AddModule(new ModuleDefinition(project, "AfterClose", ModuleKind.StandardModule));

        vm.Members.Should().HaveCount(afterOpen, "a disposed document must not still be tracking the project");
    }
}
