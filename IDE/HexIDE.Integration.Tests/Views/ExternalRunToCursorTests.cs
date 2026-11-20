using System;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Debugging;
using HexIDE.Forms.Views;
using HexIDE.Automation;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class ExternalRunToCursorTests
{
    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task Linked_source_matches_the_stopped_marker_and_navigation(bool checkMarker)
    {
        await using var owned = new RealComposition();
        var composition = owned.Composition;
        composition.LocalizationService.Apply("en");
        var directory = Path.Combine(Path.GetTempPath(), "hexide-cursor-menu-" + Guid.NewGuid());
        Directory.CreateDirectory(directory);
        var link = directory + "-link";
        Directory.CreateSymbolicLink(link, directory);
        var project = new ProjectDefinition(VBProjectType.EXE, "Target");
        var module = new ModuleDefinition(project, "Callee", ModuleKind.StandardModule)
        {
            AbsolutePath = Path.Combine(link, "Callee.bas")
        };
        module.UpdateCode(string.Join(Environment.NewLine, Enumerable.Repeat("' target", 25)));
        project.AddModule(module);
        composition.ProjectManager.AddProject(project);
        var root = composition.Root;
        var view = new MainView { DataContext = root };
        view.DataTemplates.Add(new ViewLocator());
        var host = new Window { Content = view, Width = 1200, Height = 800 };
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
            await Until(() => view.GetVisualDescendants().OfType<CodeEditorView>()
                .Any(code => ReferenceEquals(code.DataContext, editor)));
            var code = view.GetVisualDescendants().OfType<CodeEditorView>()
                .Single(code => ReferenceEquals(code.DataContext, editor));
            var bar = code.FindControl<TextEditor>("TextEditor")!.TextArea.TextView
                .BackgroundRenderers.OfType<CurrentLineRenderer>().Single();
            if (checkMarker)
                bar.Line.Should().Be(external.CurrentLocation!.Value.Line);
            var other = new ModuleDefinition(project, "Other", ModuleKind.StandardModule)
            {
                AbsolutePath = Path.Combine(link, "Other.bas")
            };
            other.UpdateCode(module.Code);
            project.AddModule(other);
            composition.EditorService.EditCode(other);
            await Until(() => view.GetVisualDescendants().OfType<CodeEditorView>()
                .Any(code => ReferenceEquals(code.DataContext,
                    composition.EditorService.FindOpenCode(DocumentIdentity.For(other)))));
            var otherCode = view.GetVisualDescendants().OfType<CodeEditorView>()
                .Single(code => ReferenceEquals(code.DataContext,
                    composition.EditorService.FindOpenCode(DocumentIdentity.For(other))));
            otherCode.FindControl<TextEditor>("TextEditor")!.TextArea.TextView
                .BackgroundRenderers.OfType<CurrentLineRenderer>().Single().Line.Should().BeNull();
            if (!checkMarker)
            {
                editor.CaretOffset = editor.Document.GetLineByNumber(19).Offset;
                root.ShowNextStatementCommand.Execute(null);
                editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber
                    .Should().Be(external.CurrentLocation!.Value.Line);
                composition.DocumentDockService.ActiveDocument.Should().BeSameAs(editor);
                composition.DocumentDockService.OpenDocuments.Count().Should().Be(2);
            }
        }
        finally
        {
            await external.StopAsync();
            await run.WaitAsync(TimeSpan.FromSeconds(20), TestContext.Current.CancellationToken);
            foreach (var loadedModule in project.Modules)
                composition.EditorService.FindOpenCode(DocumentIdentity.For(loadedModule))?.Dispose();
            composition.ProjectManager.UnloadProject(project);
            await Until(() => !HexIDE.TestGuards.LspSessionLeakGuard.StillOpen().Any(open =>
                project.Modules.Any(loadedModule => open.Session.Uri == new Uri(loadedModule.AbsolutePath!).AbsoluteUri)));
            host.Close();
            view.DataContext = null;
            Directory.Delete(link);
            Directory.Delete(directory, true);
        }
    }

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
