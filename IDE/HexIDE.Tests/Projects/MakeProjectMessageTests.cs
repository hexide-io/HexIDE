using System;
using System.IO;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

public class MakeProjectMessageTests
{
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IEventBus events = Substitute.For<IEventBus>();
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();

    private ProjectService Service() => new(
        () => throw new InvalidOperationException(), windows, events, Substitute.For<IProjectManager>(),
        Substitute.For<IRecentProjectsService>(), Substitute.For<IReferenceLibraryService>(),
        Substitute.For<IUserSidecarService>(), new FileBaselineStore(), localization,
        Substitute.For<IHeaderRefresher>());

    [Fact]
    public async Task No_startup_project_uses_the_localized_message()
    {
        localization.GetString("Str.MakeProject.NoStartup").Returns("localized no startup");
        await Service().MakeProject();
        await windows.Received().MessageBox("localized no startup", icon: MessageBoxIcon.Information);
    }

    [Fact]
    public async Task Failure_reports_the_program_was_not_made_with_an_error_icon_and_keeps_the_detail()
    {
        localization.GetString("Str.MakeProject.Failed").Returns("localized not made: {0}");
        events.When(x => x.Publish(Arg.Any<HexIDE.Events.ApplyAllUnsavedChangesEvent>()))
            .Do(_ => throw new IOException("failure detail"));
        Func<Task> make = () => Service().MakeProject(new ProjectDefinition(VBProjectType.EXE, "Project1"));
        await make.Should().ThrowAsync<IOException>();
        await windows.Received().MessageBox("localized not made: failure detail", icon: MessageBoxIcon.Error);
    }

    // Browser and unsupported-host branches cannot be reached on this test host. Guard all five call sites too.
    [Theory]
    [InlineData("NoStartup")]
    [InlineData("Browser")]
    [InlineData("UnsupportedPlatform")]
    [InlineData("MissingRuntime")]
    [InlineData("Failed")]
    public void Every_make_message_reads_its_localization_key(string name)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Join(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        dir.Should().NotBeNull();
        var source = File.ReadAllText(Path.Join(dir!.FullName, "IDE/HexIDE/Projects/ProjectService.cs"));
        source.Should().Contain($"localization.GetString(\"Str.MakeProject.{name}\")");
    }
}
