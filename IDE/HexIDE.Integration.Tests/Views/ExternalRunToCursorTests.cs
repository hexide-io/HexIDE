using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using HexIDE.Automation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class ExternalRunToCursorTests
{
    [AvaloniaFact]
    public async Task Stopped_external_session_enables_the_menu_and_runs_to_the_editor_caret()
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        composition.LocalizationService.Apply("en");
        var directory = Path.Combine(Path.GetTempPath(), "hexide-cursor-menu-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var project = new ProjectDefinition(VBProjectType.EXE, "Target");
        var module = new ModuleDefinition(project, "Callee", ModuleKind.StandardModule)
        {
            AbsolutePath = Path.Combine(directory, "Callee.bas")
        };
        module.UpdateCode(string.Join(Environment.NewLine, Enumerable.Repeat("' target", 25)));
        project.AddModule(module);
        composition.ProjectManager.AddProject(project);
        var root = composition.Root;
        var view = new MainView { DataContext = root };
        var host = new Window { Content = view };
        var executable = Path.GetFullPath("../../../dotnet" + (OperatingSystem.IsWindows() ? ".exe" : ""), RuntimeEnvironment.GetRuntimeDirectory());
        var configuration = Path.Combine(directory, "launch.json");
        await File.WriteAllTextAsync(configuration, JsonSerializer.Serialize(new
        {
            executable, arguments = new[] { typeof(Dap.TestAdapter.Program).Assembly.Location, "cursor" },
            adapterId = "fixture", launch = new { }
        }), TestContext.Current.CancellationToken);
        var external = composition.ExternalDebugger;
        string? failure = null;
        external.Failed += message => failure = message;
        var run = external.StartAsync(configuration);
        async Task Until(Func<bool> predicate)
        {
            var deadline = DateTime.UtcNow.AddSeconds(20);
            while (!predicate())
            {
                DateTime.UtcNow.Should().BeBefore(deadline, "the adapter should progress (failure: {0}, location: {1})",
                    failure, external.CurrentLocation);
                await Task.Delay(10, TestContext.Current.CancellationToken);
            }
        }
        try
        {
            host.Show();
            await Until(() => external.CanEvaluate && external.CanEditBreakpoints);
            composition.EditorService.EditCode(module);
            var editor = composition.EditorService.FindOpenCode(DocumentIdentity.For(module))!;
            composition.DocumentDockService.ActiveDocument.Should().BeSameAs(editor);
            var menu = view.GetLogicalDescendants().OfType<Menu>().Single();
            var target = MenuPath.Resolve(menu.Items, "Debug/Run To Cursor");
            target.Error.Should().BeNull();
            target.Item!.IsEnabled.Should().BeTrue();
            editor.CaretOffset = editor.Document.GetLineByNumber(19).Offset;
            var stop = external.Stop;
            root.RunToCursorCommand.Execute(null);
            await Until(() => external.Stop != stop && external.CurrentLocation?.Line == 19);
            external.GetBreakpoints(new Uri(module.AbsolutePath).AbsoluteUri).Should().BeEmpty();
            composition.BreakpointService.All().Should().BeEmpty();
        }
        finally
        {
            await external.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            composition.EditorService.FindOpenCode(DocumentIdentity.For(module))?.Dispose();
            composition.ProjectManager.UnloadProject(project);
            await Until(() => !HexIDE.TestGuards.LspSessionLeakGuard.StillOpen().Any(open =>
                open.Session.Uri == new Uri(module.AbsolutePath).AbsoluteUri));
            host.Close();
            view.DataContext = null;
            Directory.Delete(directory, true);
        }
    }
}
