using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// Undo in the code window once the buffer holds the whole file (hexide-io/HexIDE#273 task 3.8).
/// </summary>
/// <remarks>
/// <para>
/// Two things are being held at once and they pull against each other. A committed designer change reaches
/// this window as a write to the header, and it must not be undoable here — the designer keeps its own
/// history and the developer expects each window to undo what was done in it. But the write cannot simply go
/// unrecorded either: an AvaloniaEdit undo entry holds an ABSOLUTE OFFSET, so an unrecorded change above one
/// makes it replay against the wrong text, which is worse than the problem it would solve.
/// </para>
/// <para>
/// So the write IS recorded, paired with an operation that carries the buffer's prefix, and the code
/// window's Undo pops header writes until an ordinary edit comes off with them and then puts the current
/// header back. The tests below are split accordingly: the first group is that policy, and the second is the
/// safety net underneath it — an undo arriving by a route HexIDE does not own (AvaloniaEdit answers Ctrl+Y
/// itself) still leaves the buffer and the prefix agreeing about where the header ends.
/// </para>
/// </remarks>
public class CodeEditorUndoTests : IDisposable
{
    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private const string ShortHeader = "Attribute VB_Name = \"Module1\"\r\n";
    private const string LongHeader = "Attribute VB_Name = \"Module1\"\r\nAttribute VB_Description = \"grown\"\r\n";

    private CodeEditorViewModel OpenOn(ModuleDefinition module)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm.Initialize(module);
    }

    private static ModuleDefinition AModule(string code)
    {
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode(code);
        return module;
    }

    /// <summary>What the view supplies as the real pop: the editor control's own undo.</summary>
    private static void Undo(CodeEditorViewModel vm) => vm.UndoRequested(() => vm.Document.UndoStack.Undo());

    /// <summary>A designer commit, as HeaderRefresher delivers it to an open window.</summary>
    private static void ADesignerCommit(CodeEditorViewModel vm, ModuleDefinition module, string header)
    {
        module.RecordOriginalHeader(header);
        vm.RefreshPrefix(header);
    }

    // -- The policy ---------------------------------------------------------------------------------

    [AvaloniaFact]
    public void ACodeEditThenADesignerMoveThenUndoUndoesTheEditAndNotTheMove()
    {
        // The task's own scenario, verbatim. Three claims, and they are separable: the edit is undone, the
        // move is not, and the document is left in a state a further undo can still work against.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);

        vm.Document.Insert(vm.Document.TextLength, "Dim x As Long\r\n");
        ADesignerCommit(vm, module, LongHeader);

        Undo(vm);

        vm.BufferBody.Should().Be("Option Explicit\r\n", "the developer's edit is what an undo undoes");
        vm.Document.Text.Should().StartWith(LongHeader, "and the designer's change stands");
        vm.Document.Text.Should().Be(LongHeader + "Option Explicit\r\n");
    }

    [AvaloniaFact]
    public void AndASecondUndoStillUndoesTheRightText()
    {
        // The third claim, which is where an offset bug would show. Each entry has to replay against the
        // header it was recorded under, and the loop arranges that by undoing the header write FIRST rather
        // than stepping over it.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);

        vm.Document.Insert(vm.Document.TextLength, "Dim a\r\n");
        vm.Document.Insert(vm.Document.TextLength, "Dim b\r\n");
        ADesignerCommit(vm, module, LongHeader);

        Undo(vm);
        vm.BufferBody.Should().Be("Option Explicit\r\nDim a\r\n");

        Undo(vm);
        vm.BufferBody.Should().Be("Option Explicit\r\n");
        vm.Document.Text.Should().Be(LongHeader + "Option Explicit\r\n");
    }

    [AvaloniaFact]
    public void AnEditMadeAfterTheDesignerChangeUndoesFirstAndOnItsOwn()
    {
        // The interleaving that a descriptor-based check gets wrong. Stack: edit, header write, edit. The
        // first undo takes the top edit and must NOT touch the header; the second has to reach past the
        // header write to the edit below it, which only replays correctly once the old header is back.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);

        vm.Document.Insert(vm.Document.TextLength, "Dim a\r\n");
        ADesignerCommit(vm, module, LongHeader);
        vm.Document.Insert(vm.Document.TextLength, "Dim b\r\n");

        Undo(vm);
        vm.BufferBody.Should().Be("Option Explicit\r\nDim a\r\n");
        vm.Document.Text.Should().StartWith(LongHeader, "the designer's change was not on the way");

        Undo(vm);
        vm.BufferBody.Should().Be("Option Explicit\r\n");
        vm.Document.Text.Should().StartWith(LongHeader);
    }

    [AvaloniaFact]
    public void TwoDesignerChangesInARowStillCostOneUndo()
    {
        // Depth, which is the case LastGroupDescriptor cannot answer: it reports the last group OPENED and
        // an Undo clears it, so after the first pop nothing on the stack identifies itself. The operation
        // inside each group does, at any depth.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);

        vm.Document.Insert(vm.Document.TextLength, "Dim x As Long\r\n");
        ADesignerCommit(vm, module, LongHeader);
        ADesignerCommit(vm, module, LongHeader + "Attribute VB_Ext_KEY = \"a\"\r\n");

        Undo(vm);

        vm.BufferBody.Should().Be("Option Explicit\r\n");
        vm.Document.Text.Should().StartWith(LongHeader + "Attribute VB_Ext_KEY = \"a\"\r\n");
    }

    [AvaloniaFact]
    public void AWindowHoldingNothingButDesignerChangesUndoesToItselfRatherThanEmptyingItself()
    {
        // There is no code edit in it, so there is nothing for an undo to do -- and the loop must stop
        // rather than run the document down to nothing. The header must still be the current one at the end.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);
        ADesignerCommit(vm, module, LongHeader);

        Undo(vm);

        vm.Document.Text.Should().Be(LongHeader + "Option Explicit\r\n");
        vm.BufferBody.Should().Be("Option Explicit\r\n");
    }

    [AvaloniaFact]
    public void AnOrdinaryUndoIsLeftExactlyAsItWas()
    {
        // The other direction, and the one a careless fix breaks: an undo with no designer change anywhere
        // near it must not re-apply anything, because re-applying pushes an entry and pushing clears the
        // redo stack. An undo that cost the developer their redo would be a worse bug than the one fixed.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);
        vm.Document.Insert(vm.Document.TextLength, "Dim x As Long\r\n");

        Undo(vm);

        vm.BufferBody.Should().Be("Option Explicit\r\n");
        vm.Document.UndoStack.CanRedo.Should().BeTrue("an ordinary undo leaves something to redo");
    }

    [AvaloniaFact]
    public void AnOrdinaryUndoDoesNotRewriteAHeaderThatHasDriftedEither()
    {
        // The guard above only bites when the model's header and the buffer's differ, which they do the
        // moment a document is renamed and nothing has refreshed the window yet. Without the guard THAT
        // undo would push a header write and take the developer's redo with it -- an undo costing a redo,
        // for a reason having nothing to do with what was undone.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);
        vm.Document.Insert(vm.Document.TextLength, "Dim x As Long\r\n");
        module.RecordOriginalHeader(LongHeader);   // the model moves; the buffer is not told

        Undo(vm);

        vm.Document.Text.Should().Be(ShortHeader + "Option Explicit\r\n",
            "an undo is not a refresh, and rewriting the header here would be one");
        vm.Document.UndoStack.CanRedo.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AFreshlyOpenedWindowHasNothingToUndo()
    {
        // Loading the document is a Document.Text assignment and AvaloniaEdit records every one, so before
        // this the first Ctrl+Z in a newly opened code window emptied the buffer to nothing (measured). It
        // also matters to the loop above, which pops one entry past the header writes on the assumption
        // that it is something a developer typed.
        var vm = OpenOn(AModule("Option Explicit\r\n"));

        vm.Document.UndoStack.CanUndo.Should().BeFalse();

        Undo(vm);
        vm.Document.Text.Should().Be(ShortHeader + "Option Explicit\r\n");
    }

    [AvaloniaFact]
    public void AReloadFromDiskDiscardsTheHistory()
    {
        // Every entry holds an absolute offset into a document that has just been replaced wholesale, so
        // undoing across a reload would put back content the file no longer has. The designer half of the
        // same reload already clears its own stack; the two halves now agree.
        var module = AModule("Option Explicit\r\n");
        var vm = OpenOn(module);
        vm.Document.Insert(vm.Document.TextLength, "Dim x As Long\r\n");

        module.UpdateCode("Public Sub Main()\r\nEnd Sub\r\n");
        vm.ReloadFrom(HexIDE.Runtime.Serialization.FormCodeText.Prefix(module), module.Code);

        vm.Document.UndoStack.CanUndo.Should().BeFalse();
        vm.Document.UndoStack.CanRedo.Should().BeFalse();
    }

    // -- The safety net underneath it ----------------------------------------------------------------

    [AvaloniaFact]
    public void AHeaderWriteUndoneByARouteHexIdeDoesNotOwnStillLeavesTheSplitHonest()
    {
        // Ctrl+Y is AvaloniaEdit's own redo gesture and HexIDE binds nothing to it, so an undo or redo can
        // reach this document without passing through UndoRequested at all. That is why the prefix rides in
        // the undo group as an operation rather than being fixed up by the caller: whatever pops the entry,
        // the buffer and the prefix move together.
        var module = AModule("Option Explicit\r\nDim x As Long\r\n");
        var vm = OpenOn(module);
        ADesignerCommit(vm, module, LongHeader);

        vm.Document.UndoStack.Undo();   // NOT through UndoRequested

        vm.Document.Text.Should().Be(ShortHeader + "Option Explicit\r\nDim x As Long\r\n");
        vm.BufferBody.Should().Be("Option Explicit\r\nDim x As Long\r\n",
            "before the paired operation this answered \"x As Long\\r\\n\" and the next flush wrote that as the document");
    }

    [AvaloniaFact]
    public void AndSoDoesARedoOfOne()
    {
        var module = AModule("Option Explicit\r\nDim x As Long\r\n");
        var vm = OpenOn(module);
        ADesignerCommit(vm, module, LongHeader);
        vm.Document.UndoStack.Undo();

        vm.Document.UndoStack.Redo();

        vm.Document.Text.Should().Be(LongHeader + "Option Explicit\r\nDim x As Long\r\n");
        vm.BufferBody.Should().Be("Option Explicit\r\nDim x As Long\r\n");
    }

    [AvaloniaFact]
    public void TheFlushAfterAnUndoWritesTheWholeBodyBack()
    {
        // The consequence the whole task is about, asserted where it actually bites. BufferBody is what the
        // flush on close writes into the model, and a body taken from the wrong offset is silent data loss,
        // not a display fault.
        var module = AModule("Option Explicit\r\nDim x As Long\r\n");
        var vm = OpenOn(module);
        ADesignerCommit(vm, module, LongHeader);
        vm.Document.UndoStack.Undo();

        vm.Dispose();

        module.Code.Should().Be("Option Explicit\r\nDim x As Long\r\n");
    }
}
