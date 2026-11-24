using HexIDE.Desktop;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Infrastructure;

public class NoProjectTypeLaunchTests : CompositionTestBase
{
    [AvaloniaTheory]
    [InlineData("service", false)]
    [InlineData("service", true)]
    [InlineData("newproject", false)]
    [InlineData("newproject", true)]
    [InlineData("newproject-unsaved", false)]
    [InlineData("newproject-unsaved", true)]
    public async Task No_project_type_reports_once_without_changing_the_workspace(string route, bool groupOpen)
    {
        var oldStartup = Static.StartupProjectHook;
        var oldDesktop = Static.DesktopStartupHook;
        var recovery = Static.IsRecoveryMode;
        var developer = Static.DeveloperMode;
        var capture = Static.CaptureLsp;
        var options = new[] { "NoVb6", "NewProject", "NewProjectUnsaved", "AnsiCodePage" }
            .Select(name => typeof(ServerOptions).GetProperty(name)!).ToArray();
        var values = options.Select(property => property.GetValue(null)).ToArray();
        var withoutVb6 = Static.WithoutVb6;
        try
        {
            Static.WithoutVb6 = true;
            options[0].SetValue(null, true);
            options[3].SetValue(null, null);
            options[1].SetValue(null, route == "newproject");
            options[2].SetValue(null, route == "newproject-unsaved");
            var before = groupOpen ? new[] { TestHelpers.CreateProject("First"), TestHelpers.CreateProject("Second") } : [];
            foreach (var project in before) Composition.ProjectManager.AddProject(project);
            var startup = before.LastOrDefault();
            Composition.ProjectManager.StartupProject = startup;
            var groupPath = Path.Join(Path.GetTempPath(), "hexide-4275", "Saved.vbg");
            var group = groupOpen ? new ProjectGroupDefinition("Saved") { AbsolutePath = groupPath } : null;
            Composition.ProjectManager.CurrentGroup = group;
            var windows = Substitute.For<IWindowManager>();
            var service = new ProjectService(() => throw new InvalidOperationException("no dialog"),
                windows, Composition.EventBus, Composition.ProjectManager, Substitute.For<IRecentProjectsService>(),
                Substitute.For<IReferenceLibraryService>(), Substitute.For<IUserSidecarService>(), new FileBaselineStore(),
                Composition.LocalizationService, Substitute.For<IHeaderRefresher>(), InBoxRegistry.Create(),
                contributed: Composition.ContributedProjects, templates: Composition.AddinProjectTemplateService);
            if (route == "service")
                await service.CreateDefaultProject();
            else
            {
                DesktopStartup.Register();
                await Static.StartupProjectHook!(service, Composition.ProjectManager);
            }

            Composition.ProjectManager.LoadedProjects.Should().Equal(before);
            Composition.ProjectManager.StartupProject.Should().BeSameAs(startup);
            Composition.ProjectManager.CurrentGroup.Should().BeSameAs(group);
            if (group is not null)
            {
                group.Name.Should().Be("Saved");
                group.AbsolutePath.Should().Be(groupPath, "the saved group must not become unsaved");
            }
            await windows.Received(1).MessageBox(
                $"{Distribution.DisplayName} has no project type installed that makes a Standard EXE project, so it started without one.",
                Distribution.DisplayName, MessageBoxButtons.Ok, MessageBoxIcon.Information);
            windows.ReceivedCalls().Should().HaveCount(1);
        }
        finally
        {
            Static.WithoutVb6 = withoutVb6;
            Static.IsRecoveryMode = recovery;
            Static.DeveloperMode = developer;
            Static.CaptureLsp = capture;
            Static.StartupProjectHook = oldStartup;
            Static.DesktopStartupHook = oldDesktop;
            for (var i = 0; i < options.Length; i++) options[i].SetValue(null, values[i]);
        }
    }
}
