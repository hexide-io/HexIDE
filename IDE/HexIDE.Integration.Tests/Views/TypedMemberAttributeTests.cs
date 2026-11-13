using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using HexIDE.Automation;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

// The accepted-input ownership design in https://github.com/hexide-io/HexIDE/issues/3819.
public class TypedMemberAttributeTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly IEventBus events = Substitute.For<IEventBus>();
    private readonly IStatusBarService status = Substitute.For<IStatusBarService>();
    private ModuleDefinition module = null!;
    private Action<DocumentSavedEvent>? saved;

    public void Dispose()
    {
        foreach (var window in windows) window.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private (CodeEditorViewModel Vm, TextEditor Editor) Open(string attribute = "")
    {
        events.Subscribe<DocumentSavedEvent>(Arg.Do<Action<DocumentSavedEvent>>(handler => saved = handler))
            .Returns(Substitute.For<IDisposable>());
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            events, Substitute.For<ILspClient>(), settings, status,
            new BookmarkService(), new BreakpointService(), Substitute.For<IDebugController>(), new RunScope(),
            localization, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        made.Add(vm);
        module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "DName", ModuleKind.StandardModule);
        module.RecordOriginalHeader("Attribute VB_Name = \"DName\"\r\n");
        module.UpdateCode("Public Function Bar() As Long\r\n" + attribute + "\r\n    Bar = 2\r\nEnd Function\r\n");
        vm.Initialize(module);
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        window.Content = new CodeEditorView { DataContext = vm };
        Dispatcher.UIThread.RunJobs();
        var editor = ((CodeEditorView)window.Content).FindControl<TextEditor>("TextEditor")!;
        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
        return (vm, editor);
    }

    private static async Task Type(TextEditor editor, string text)
    {
        var result = await UiAutomationDriver.TypeKeysAsync(editor, text);
        result.Success.Should().BeTrue(result.Error);
    }

    [AvaloniaTheory]
    [InlineData("Attribute Bar.VB_Description = \"e\"")]
    [InlineData("Attribute Bar.VB_UserMemId = -45")]
    [InlineData("Attribute Bar.VB_Description = True")]
    public async Task EveryKeyWritesUntilTheCaretLeaves(string text)
    {
        var (vm, editor) = Open();
        await Type(editor, text);
        editor.Document.GetText(editor.Document.GetLineByNumber(3)).Should().Be(text);
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
        vm.HangingRegionsNow.Should().BeEmpty();
        editor.CaretOffset = editor.Document.GetLineByNumber(4).Offset;
        vm.IsReadOnlyRegion(editor.Document.GetLineByNumber(3).Offset, 0).Should().BeTrue();
        vm.HangingRegionsNow.Should().ContainSingle();
        editor.CaretOffset = editor.Document.GetLineByNumber(3).EndOffset;
        (await UiAutomationDriver.TypeKeysAsync(editor, "x")).Success.Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task TypingInEitherSplitPaneKeepsOwnership(int pane)
    {
        var (vm, _) = Open();
        var view = (CodeEditorView)windows[0].Content!;
        view.ToggleSplit();
        Dispatcher.UIThread.RunJobs();
        var editor = view.GetVisualDescendants().OfType<TextEditor>().ElementAt(pane);
        editor.TextArea.Focus().Should().BeTrue();
        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
        await Type(editor, "Attribute Bar.VB_Description = \"e\"");
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AHalfTypedLineCanBeDeleted(bool selection)
    {
        var (_, editor) = Open();
        await Type(editor, "Attribute Bar.");
        if (selection)
        {
            editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
            UiAutomationDriver.PressKey(editor, "Down", "shift").Success.Should().BeTrue();
            UiAutomationDriver.PressKey(editor, "Delete", null).Success.Should().BeTrue();
        }
        else
            for (var i = 0; i < "Attribute Bar.".Length; i++)
                UiAutomationDriver.PressKey(editor, "Back", null).Success.Should().BeTrue();
        editor.Document.Text.Should().NotContain("Attribute Bar.");
    }

    [AvaloniaFact]
    public async Task ClearingASelectionOffTheTypedLineEndsOwnership()
    {
        var (vm, editor) = Open();
        await Type(editor, "Attribute Bar.");
        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
        UiAutomationDriver.PressKey(editor, "Down", "shift").Success.Should().BeTrue();
        vm.IsReadOnlyRegion(editor.Document.GetLineByNumber(3).Offset, 0).Should().BeFalse();
        editor.TextArea.ClearSelection();
        vm.IsReadOnlyRegion(editor.Document.GetLineByNumber(3).Offset, 0).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task AnExistingAttributeLineRefusesTyping()
    {
        var (_, editor) = Open("Attribute Bar.VB_Description = \"e\"");
        var before = editor.Document.Text;
        (await UiAutomationDriver.TypeKeysAsync(editor, "x")).Success.Should().BeFalse();
        editor.Document.Text.Should().Be(before);
    }

    [AvaloniaTheory]
    [InlineData("delete")]
    [InlineData("library-delete")]
    [InlineData("indent")]
    [InlineData("outdent")]
    [InlineData("menu-indent")]
    [InlineData("menu-outdent")]
    [InlineData("cut")]
    public async Task ProtectedLineCommandsPreserveHistoryAndSelection(string command)
    {
        var (_, editor) = Open("Attribute Bar.VB_Description = \"e\"");
        editor.CaretOffset = editor.Document.GetLineByNumber(4).EndOffset;
        await Type(editor, "2");
        editor.Undo();
        editor.CanRedo.Should().BeTrue();
        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset + 4;
        if (command is "indent" or "outdent" or "menu-indent" or "menu-outdent")
            editor.Select(editor.Document.GetLineByNumber(3).Offset, editor.Document.GetLineByNumber(3).Length);
        var before = editor.Text;
        var caret = editor.CaretOffset;
        var selection = editor.TextArea.Selection;
        var undo = editor.CanUndo;
        var redo = editor.CanRedo;
        status.ClearReceivedCalls();

        var gesture = command switch
        {
            "delete" => ApplicationCommands.DeleteLineCommand.Gestures[0],
            "library-delete" => AvaloniaEdit.AvaloniaEditCommands.DeleteLine.Gesture!,
            "cut" => ApplicationCommands.CutCommand.Gestures[0],
            "menu-indent" => ApplicationCommands.IndentCommand.Gestures[0],
            "menu-outdent" => ApplicationCommands.OutdentCommand.Gestures[0],
            _ => new KeyGesture(Key.Tab, command == "outdent" ? KeyModifiers.Shift : KeyModifiers.None)
        };
        editor.TextArea.Focus();
        if (command == "menu-indent") ((CodeEditorView)windows[0].Content!).Indent();
        else if (command == "menu-outdent") ((CodeEditorView)windows[0].Content!).Outdent();
        else editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers,
        });
        Dispatcher.UIThread.RunJobs();

        editor.Text.Should().Be(before);
        editor.CanRedo.Should().Be(redo);
        editor.CanUndo.Should().Be(undo);
        editor.CaretOffset.Should().Be(caret);
        editor.TextArea.Selection.Should().BeSameAs(selection);
        status.Received().SetTemporaryMessage("Str.CodeEditor.Msg.TypingChangesReadOnlyText", Arg.Any<TimeSpan>());
        editor.Redo();
        editor.Document.GetText(editor.Document.GetLineByNumber(4)).Should().EndWith("22");
    }

    [AvaloniaFact]
    public async Task SavingEndsTrackingWithoutMovingTheCaret()
    {
        var (vm, editor) = Open();
        await Type(editor, "Attribute Bar.");
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
        saved!(new DocumentSavedEvent(null, module));
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EnterAdmitsTheLineAndDoesNotJumpPastIt(bool shift)
    {
        var (vm, editor) = Open();
        await Type(editor, "Attribute Bar.");
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
        UiAutomationDriver.PressKey(editor, "Enter", shift ? "shift" : null).Success.Should().BeTrue();
        editor.TextArea.Caret.Line.Should().Be(4);
        vm.IsReadOnlyRegion(editor.Document.GetLineByNumber(3).Offset, 0).Should().BeTrue();
    }

    [AvaloniaTheory]
    [InlineData("Attribute Bar.")]
    [InlineData("' note\r\nPublic Function Baz() As Long\r\nAttribute Baz.")]
    public async Task APastedLineCanStillBeFinished(string text)
    {
        var (vm, editor) = Open();
        await TopLevel.GetTopLevel(editor)!.Clipboard!.SetTextAsync(text);
        var before = editor.Document.Text;
        var gesture = HexIDE.ApplicationCommands.PasteCommand.Gestures[0];
        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers,
        });
        for (var i = 0; i < 50 && editor.Document.Text == before; i++)
        {
            await Task.Delay(10, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }
        editor.Document.Text.Should().Contain(text);
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
        await Type(editor, "VB_Description = \"e\"");
        editor.Document.Text.Should().Contain(text + "VB_Description = \"e\"");
    }

    [AvaloniaFact]
    public async Task PastePastADescribedDeclarationKeepsTheLastLineEditable()
    {
        var (vm, editor) = Open("Attribute Bar.VB_Description = \"e\"");
        editor.CaretOffset = editor.Document.GetLineByNumber(2).EndOffset;
        await Paste(editor, "x\r\nPublic Function Baz() As Long\r\nAttribute Baz.");
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
        await Type(editor, "VB_Description = \"e\"");
        editor.Document.Text.Should().Contain("Attribute Baz.VB_Description = \"e\"");
    }

    [AvaloniaTheory]
    [InlineData("automation")]
    [InlineData("breakpoint")]
    [InlineData("bookmark")]
    public async Task TrackedAttributesRefuseNewMarks(string mark)
    {
        var (vm, editor) = Open();
        await Type(editor, "Attribute Bar.");
        if (mark == "automation")
            MarkLineRules.Refusal(vm.CodeWindowText, vm.Identity.Display, [3], [],
                first: 1, "breakpoint").Should().NotBeNull();
        else if (mark == "bookmark")
            vm.ToggleBookmark(3).Should().BeFalse();
        else
            vm.ToggleBreakpoint(3).Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task PasteReplacesOnlyTheEditablePartOfAProtectedSelection()
    {
        var (_, editor) = Open("Attribute Bar.VB_Description = \"e\"");
        var declaration = editor.Document.GetLineByNumber(2);
        editor.Select(declaration.Offset + 7, editor.Document.GetLineByNumber(3).Offset + 5 - declaration.Offset - 7);
        await Paste(editor, "replacement");
        editor.Document.Text.Should().Contain("Public replacement");
        editor.Document.Text.Should().Contain("Attribute Bar.VB_Description = \"e\"");
    }

    [AvaloniaFact]
    public async Task UndoAndRedoDoNotRearmTypedLineOwnership()
    {
        var (vm, editor) = Open();
        await Type(editor, "Attribute Bar.");
        editor.Document.UndoStack.ClearAll();
        editor.Document.UndoStack.StartUndoGroup();
        await Type(editor, "VB_Description");
        editor.Document.UndoStack.EndUndoGroup();
        editor.CaretOffset = editor.Document.GetLineByNumber(4).Offset;
        Dispatcher.UIThread.RunJobs();
        editor.Document.UndoStack.Undo();
        editor.Document.GetText(editor.Document.GetLineByNumber(3)).Should().Be("Attribute Bar.");
        vm.IsReadOnlyRegion(editor.Document.GetLineByNumber(3).Offset, 0).Should().BeTrue();
        editor.Document.UndoStack.Redo();
        vm.IsReadOnlyRegion(editor.Document.GetLineByNumber(3).Offset, 0).Should().BeTrue();
    }

    private static async Task Paste(TextEditor editor, string text)
    {
        await TopLevel.GetTopLevel(editor)!.Clipboard!.SetTextAsync(text);
        var before = editor.Document.Text;
        var gesture = HexIDE.ApplicationCommands.PasteCommand.Gestures[0];
        editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = gesture.Key, KeyModifiers = gesture.KeyModifiers,
        });
        for (var i = 0; i < 50 && editor.Document.Text == before; i++)
        {
            await Task.Delay(10, CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
        }
        editor.Document.Text.Should().NotBe(before);
    }

    [AvaloniaFact]
    public async Task FormattingTheTypedLineDoesNotEndOwnership()
    {
        var (vm, editor) = Open();
        await Type(editor, "Attribute Bar.");
        var line = editor.Document.GetLineByNumber(3);
        editor.Document.Replace(line.Offset, "Attribute".Length, "ATTRIBUTE");
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
        await Type(editor, "VB_Description = \"e\"");
    }

    [AvaloniaFact]
    public void AnIdeWriteDoesNotStartTracking()
    {
        var (vm, editor) = Open();
        editor.Document.Insert(editor.CaretOffset, "Attribute Bar.");
        vm.IsReadOnlyRegion(editor.Document.GetLineByNumber(3).Offset, 0).Should().BeTrue();
    }

    [AvaloniaFact]
    public async Task ReloadEndsTrackingEvenWhenTheTextIsUnchanged()
    {
        var (vm, editor) = Open();
        await Type(editor, "Attribute Bar.");
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeFalse();
        vm.ReloadFrom(module.OriginalHeader!, vm.BufferBody);
        vm.IsReadOnlyRegion(editor.CaretOffset, 0).Should().BeTrue();
    }
}
