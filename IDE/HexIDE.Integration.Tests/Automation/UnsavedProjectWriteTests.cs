using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia.Controls.ApplicationLifetimes;
using HexIDE.Desktop.Server;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Projects;

namespace HexIDE.Integration.Tests.Automation;

public class UnsavedProjectWriteTests
{
    [AvaloniaFact]
    public async Task Refusal_gives_a_menu_route_that_saves_the_unsaved_startup_project()
    {
        await using var owned = new RealComposition();
        var setup = owned.Composition;
        var root = setup.Root;
        // The same creation entry point as DesktopStartup's --newproject-unsaved hook.
        await setup.ProjectService.CreateNewProject(Vb6ProjectType.StandardExe);
        var project = setup.ProjectManager.LoadedProjects.Single();
        project.AbsolutePath.Should().BeNull();
        project.WorkingDirectory.Should().BeNull();
        var form = project.Forms.Single();
        var window = ToolbarAutomationNameTests.ShowMainView();
        var main = (MainView)window.Content!;
        main.DataContext = root;
        main.Focusable = true;
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        main.Focus().Should().BeTrue();
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        var lifetime = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        lifetime.MainWindow.Returns(window);
        lifetime.Windows.Returns(new[] { window });
        var field = typeof(Avalonia.Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(f => f.FieldType == typeof(IApplicationLifetime));
        var previous = field.GetValue(Avalonia.Application.Current!);
        field.SetValue(Avalonia.Application.Current!, lifetime);
        using var context = new IdeContext(setup.ProjectManager, setup.DocumentDockService, setup.LspClient,
            setup.EditorService, setup.ProjectRunnerService, setup.ProjectService, setup.EventBus,
            setup.BookmarkService, setup.BreakpointService, setup.DebugController, root, setup.ToolBoxViewModel,
            setup.ProfileService, setup.AddinProjectTemplateService, setup.LanguageSwitchService, setup.Capture,
            setup.Pseudonyms, setup.LocalizationService, setup.SettingsService);
        var tools = new HexIdeTools(context);
        var directory = Path.Combine(Path.GetTempPath(), "hexide-4335-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            ScriptedFileDialogs.Clear();
            var countBefore = form.Components.Count;
            var refusal = await tools.AddControlAsync(form.Name, "CommandButton", 16, 16, 81, 33);
            refusal.Success.Should().BeFalse();
            refusal.Error.Should().Contain("project has no folder").And.Contain("forms, then modules, then the project file")
                .And.Contain("File/Save Project1").And.Contain("alone won't help");
            ScriptedFileDialogs.Pending.Should().Be(0);
            form.Components.Count.Should().Be(countBefore);
            await tools.AnswerNextFileDialogAsync(Path.Combine(directory, "Form1.frm"));
            await tools.AnswerNextFileDialogAsync(Path.Combine(directory, "Project1.vbp"));
            var saved = await tools.InvokeMenuItemAsync("File/Save Project1", TestContext.Current.CancellationToken);
            saved.Success.Should().BeTrue(saved.Error);
            ScriptedFileDialogs.Pending.Should().Be(0);
            project.AbsolutePath.Should().Be(Path.Combine(directory, "Project1.vbp"));
            File.Exists(form.AbsolutePath).Should().BeTrue();
            File.Exists(project.AbsolutePath).Should().BeTrue();
            var added = await tools.AddControlAsync(form.Name, "CommandButton", 16, 16, 81, 33);
            added.Success.Should().BeTrue(added.Error);
            File.ReadAllText(form.AbsolutePath!).Should().Contain("VB.CommandButton");
            var image = await tools.AddControlAsync(form.Name, "Image", 16, 64, 81, 65);
            image.Success.Should().BeTrue(image.Error);
            var items = await tools.GetToolboxItemsAsync(TestContext.Current.CancellationToken);
            items.Items.Should().Contain(item => item.Name == "Image");
            File.ReadAllText(form.AbsolutePath!).Should().Contain("VB.Image");
        }
        finally
        {
            ScriptedFileDialogs.Clear();
            setup.ProjectManager.UnloadAllProjects();
            window.Close();
            field.SetValue(Avalonia.Application.Current!, previous);
            Directory.Delete(directory, true);
        }
    }
}
