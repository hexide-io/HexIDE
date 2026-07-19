using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Commands;
using NSubstitute;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Exercises the designer side of the file-watcher reload: <see cref="FormEditViewModel.ReloadFromModel"/>
/// rebuilds the canvas from the (externally-reloaded) model and clears the undo stack. This is the
/// deterministic, headless counterpart to the MCP live verification of the file watcher.
/// </summary>
public class FileWatcherReloadTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static FormDefinition Deserialize(string source) =>
        new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;

    // The full ctor is used (not the previewer ctor) so ComputeTitle's localization isn't null; only the
    // deps Initialize/ReloadFromModel actually touch need to be real (event bus + localization).
    private static FormEditViewModel NewDesignerVm() => new(
        null!, Substitute.For<IEventBus>(), null!, null!, null!, null!, Substitute.For<ILocalizationService>());

    private const string OneLabel = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Label Label1
              Caption         =   "Hi"
              Left            =   120
              Top             =   120
              Width           =   1000
              Height          =   240
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private const string TwoControls = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Label Label1
              Caption         =   "Hi"
              Left            =   120
              Top             =   120
              Width           =   1000
              Height          =   240
           End
           Begin VB.CommandButton Command1
              Caption         =   "OK"
              Left            =   120
              Top             =   480
              Width           =   1000
              Height          =   360
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaFact]
    public void ReloadFromModel_RebuildsCanvas_AndClearsUndo()
    {
        var vm = NewDesignerVm();
        var form = Deserialize(OneLabel);
        vm.Initialize(form);
        vm.Components.Should().HaveCount(1); // Label1 (the form root is held in Form, not Components)

        // Dirty the designer so we can prove the reload clears the undo history.
        vm.UndoStack.Push(new LockControlsCommand(false, true));
        vm.CanUndo.Should().BeTrue();

        // Simulate the file watcher updating the model in place from a changed .frm on disk.
        var fresh = Deserialize(TwoControls);
        form.UpdateComponents(fresh.Components);
        form.UpdateCode(fresh.Code);

        vm.ReloadFromModel();

        vm.Components.Should().HaveCount(2);  // canvas rebuilt from the new model
        vm.CanUndo.Should().BeFalse();        // stale undo history discarded
    }
}
