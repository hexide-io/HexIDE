using System.Reflection;
using System.Windows.Input;
using Avalonia.Threading;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

// Measurements: docs/vb6-fidelity-oracle/view-last-position.md and https://github.com/hexide-io/HexIDE/pull/5426.
public class LastPositionTests : CompositionTestBase
{
    public override async ValueTask DisposeAsync()
    {
        try
        {
            await Dispatcher.UIThread.InvokeAsync(() => Composition.ProjectManager.UnloadAllProjects());
        }
        finally { await base.DisposeAsync(); }
    }

    private MainViewViewModel Root => Composition.Root;
    // Reflection keeps these behavior tests runnable against main's disabled placeholder.
    private ICommand? Command => Root.GetType().GetProperty("LastPositionCommand")?.GetValue(Root) as ICommand;
    private CodeEditorViewModel Active => (CodeEditorViewModel)Composition.DocumentDockService.ActiveDocument!;
    private void Press() => Command?.Execute(null);
    private static int Line(CodeEditorViewModel vm) => vm.Document.GetLineByOffset(vm.CaretOffset).LineNumber;
    private static void Move(CodeEditorViewModel vm, int line, int column = 1) =>
        vm.CaretOffset = vm.Document.GetLineByNumber(line).Offset + column - 1;

    private CodeEditorViewModel Open(string name = "Module1", ProjectDefinition? project = null)
    {
        _ = Root;
        project ??= TestHelpers.CreateProject();
        if (!Composition.ProjectManager.LoadedProjects.Contains(project)) Composition.ProjectManager.AddProject(project);
        var module = TestHelpers.CreateModule(project, name);
        module.UpdateCode("Option Explicit\nSub Main()\n    Foo\n    Bar\nEnd Sub\n\nSub Foo()\n    Debug.Print 1\nEnd Sub\n\nSub Bar()\n    Debug.Print 2\nEnd Sub\n");
        project.AddModule(module);
        Composition.EditorService.EditCode(module);
        return Active;
    }

    private static void Edit(CodeEditorViewModel vm, int line, bool leave = true)
    {
        Move(vm, line);
        vm.CaretOffset = vm.Document.GetLineByNumber(line).EndOffset;
        vm.Document.Insert(vm.CaretOffset, " ' a");
        vm.CaretOffset += 4;
        // Unit tests deliver TextEntered's notification; the integration tests deliver actual text input.
        typeof(CodeEditorViewModel).GetMethod("LastPositionTextEntered", BindingFlags.Instance | BindingFlags.NonPublic)
            ?.Invoke(vm, [" ' a"]);
        if (leave) Move(vm, 1);
    }

    [AvaloniaFact]
    public void FreshHistoryIsDisabled()
    {
        _ = Root;
        (Command?.CanExecute(null) ?? false).Should().BeFalse();
    }

    [AvaloniaFact]
    public void EditedLineEnablesCommandAndLandsAtColumnOne()
    {
        var vm = Open();
        Edit(vm, 4);
        (Command?.CanExecute(null) ?? false).Should().BeTrue();
        Press();
        vm.CaretOffset.Should().Be(vm.Document.GetLineByNumber(4).Offset);
    }

    [AvaloniaFact]
    public void ThreeEditsCycleNewestFirstWithoutRecordingPresses()
    {
        var vm = Open();
        foreach (var line in new[] { 4, 9, 13 }) Edit(vm, line);
        var landed = new List<int>();
        for (var i = 0; i < 4; i++) { Press(); landed.Add(Line(vm)); }
        landed.Should().Equal(13, 9, 4, 13);
    }

    [AvaloniaFact]
    public void TenEditsKeepEightEntries()
    {
        var vm = Open();
        vm.Document.Text = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"Sub P{i}()\n    Debug.Print {i}\nEnd Sub"));
        for (var i = 0; i < 10; i++) Edit(vm, i * 3 + 2);
        var landed = new List<int>();
        for (var i = 0; i < 12; i++) { Press(); landed.Add(Line(vm)); }
        landed.Should().Equal(29, 26, 23, 20, 17, 14, 11, 8, 29, 26, 23, 20);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void FindRecordsTheLineLeftAndRepeatedPressStaysThere(bool repeat)
    {
        var vm = Open();
        Move(vm, 4, 5);
        var find = new FindReplaceViewModel(Substitute.For<IWindowManager>(), Composition.DocumentDockService,
            Substitute.For<ILocalizationService>()) { SearchText = "Print 2" };
        find.FindNextCommand.Execute(null);
        Line(vm).Should().Be(13);
        Press();
        if (repeat) Press();
        vm.CaretOffset.Should().Be(vm.Document.GetLineByNumber(4).Offset);
    }

    [AvaloniaTheory]
    [InlineData(1)]
    [InlineData(-1)]
    public void EntryLineNumberStaysPutAfterInsertionOrWholeLineDeletion(int direction)
    {
        var vm = Open();
        Edit(vm, 13);
        var line = vm.Document.GetLineByNumber(7);
        if (direction > 0) vm.Document.Insert(line.Offset, "\n\n");
        else vm.Document.Remove(line.Offset, line.TotalLength);
        Move(vm, 1);
        Press();
        vm.CaretOffset.Should().Be(vm.Document.GetLineByNumber(13).Offset);
    }

    [AvaloniaFact]
    public void PastEndGoesToLastLine()
    {
        var vm = Open();
        Edit(vm, 13);
        var start = vm.Document.GetLineByNumber(2).Offset;
        vm.Document.Remove(start, vm.Document.GetLineByNumber(10).Offset - start);
        Move(vm, 1);
        Press();
        vm.CaretOffset.Should().Be(vm.Document.GetLineByNumber(vm.Document.LineCount).Offset);
    }

    [AvaloniaFact]
    public void PressLeavingEditedLineUsesOldEntryThenNewEntry()
    {
        var vm = Open();
        Edit(vm, 13);
        Edit(vm, 4, leave: false);
        var landed = new List<int>();
        for (var i = 0; i < 3; i++) { Press(); landed.Add(Line(vm)); }
        landed.Should().Equal(13, 4, 13);
    }

    [AvaloniaFact]
    public void ConsecutiveEditsOnSameLineCoalesce()
    {
        var vm = Open();
        Edit(vm, 4); Edit(vm, 4); Edit(vm, 9);
        var landed = new List<int>();
        for (var i = 0; i < 3; i++) { Press(); landed.Add(Line(vm)); }
        landed.Should().Equal(9, 4, 9);
    }

    [AvaloniaFact]
    public void NewEntryRestartsCycle()
    {
        var vm = Open();
        foreach (var line in new[] { 4, 9, 13 }) Edit(vm, line);
        Press(); Press();
        Edit(vm, 5);
        Press();
        Line(vm).Should().Be(5);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void OtherWindowIsActivatedAndClosedWindowReopens(bool close)
    {
        var first = Open();
        var identity = first.Identity;
        Edit(first, 4);
        var secondProject = TestHelpers.CreateProject("SecondProject");
        var second = Open("Module2", secondProject);
        var groupPath = Path.Combine(Path.GetTempPath(), "LastPosition.vbg");
        var group = new ProjectGroupDefinition("LastPositionGroup") { AbsolutePath = groupPath };
        Composition.ProjectManager.CurrentGroup = group;
        Composition.ProjectManager.StartupProject = identity.Project;
        Edit(second, 9);
        if (close) Composition.DocumentDockService.CloseDocument(first);
        Press(); Line(Active).Should().Be(9);
        Press();
        Active.Identity.Should().Be(identity);
        Line(Active).Should().Be(4);
        Press(); Active.Should().BeSameAs(second);
        Composition.ProjectManager.CurrentGroup.Should().BeSameAs(group);
        group.Name.Should().Be("LastPositionGroup");
        group.AbsolutePath.Should().Be(groupPath, "reopening code must keep the group saved");
        Composition.ProjectManager.LoadedProjects.Should().Equal(identity.Project, secondProject);
        Composition.ProjectManager.StartupProject.Should().BeSameAs(identity.Project);
    }

    [AvaloniaFact]
    public void RemovedModuleEntriesAreDropped()
    {
        var first = Open("Module2");
        Edit(first, 9);
        var second = Open("Module1", first.Identity.Project);
        Edit(second, 4);
        first.Identity.Project.DeleteModule(first.Identity.Module!);
        Press(); Line(Active).Should().Be(4);
        Press(); Line(Active).Should().Be(4);
        Active.Should().BeSameAs(second);
    }

    [AvaloniaFact]
    public void UnloadedProjectDoesNotKeepHistoryWhenReopened()
    {
        var vm = Open();
        Edit(vm, 4);
        var project = vm.Identity.Project;
        var module = vm.Identity.Module!;
        Composition.ProjectManager.UnloadAllProjects();
        Composition.ProjectManager.AddProject(project);
        Composition.EditorService.EditCode(module);
        (Command?.CanExecute(null) ?? false).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void RunAddsCaretLineAndControlKeepsOnlyEdit(bool run)
    {
        var vm = Open();
        // Output is unrelated to RunStarting; earlier roots retain Immediate listeners on the global program.
        vm.Document.Text = vm.Document.Text.Replace("Debug.Print", "' Debug.Print");
        vm.Identity.Project.StartsAtSubMain = true;
        Edit(vm, 4);
        Move(vm, 12, 10);
        if (run)
        {
            Composition.ProjectRunnerService.RunStartupProject();
            Avalonia.Threading.Dispatcher.UIThread.RunJobs();
            Composition.ProjectRunnerService.IsRunning.Should().BeFalse();
        }
        var landed = new List<int>();
        for (var j = 0; j < 3; j++) { Press(); landed.Add(Line(vm)); }
        landed.Should().Equal(run ? new[] { 12, 4, 12 } : new[] { 4, 4, 4 });
    }

    [AvaloniaFact]
    public void FindAndEditsShareEightEntries()
    {
        var vm = Open();
        vm.Document.Text = string.Join("\n", Enumerable.Range(1, 10).Select(i => $"Sub P{i}()\n    Debug.Print {i}\nEnd Sub"));
        for (var i = 0; i < 8; i++) Edit(vm, i * 3 + 2);
        Move(vm, 1);
        var find = new FindReplaceViewModel(Substitute.For<IWindowManager>(), Composition.DocumentDockService,
            Substitute.For<ILocalizationService>()) { SearchText = "Print 10" };
        find.FindNextCommand.Execute(null);
        var landed = new List<int>();
        for (var i = 0; i < 10; i++) { Press(); landed.Add(Line(vm)); }
        landed.Should().Equal(1, 23, 20, 17, 14, 11, 8, 5, 1, 23);
    }
}
