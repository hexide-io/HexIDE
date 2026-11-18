using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Controls;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Debugging;
using HexIDE.Runtime.Debugging;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A double click on a designer's background writes the root's own event: a form's Form_Load (#1532), a UserControl's
/// UserControl_Initialize and a PropertyPage's PropertyPage_SelectionChanged (#1632). The designer wrote Form_Load for all
/// three. Measured in VB6's IDE on the oracle guest's console, on the release build and on SP6 alike (the oracle's "A
/// UserControl's and a PropertyPage's own events").
/// </summary>
public class RootDoubleClickTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormEditViewModel Open(string kind, string name, string control = "", IEventBus? bus = null, IEditorService? editors = null, string code = "")
    {
        var file = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            $"VERSION 5.00\r\nBegin VB.{kind} {name} \r\n   ClientHeight    =   3600\r\n   ClientLeft      =   0\r\n"
          + $"   ClientTop       =   0\r\n   ClientWidth     =   4800\r\n{control}End\r\nAttribute VB_Name = \"{name}\"\r\n{code}", new NullSink())!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return new FormEditViewModel(null!, bus ?? Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            editors!, null!, Substitute.For<ISettingsService>(), localization).Initialize(file);
    }

    [AvaloniaTheory]
    [InlineData("Form", "Form1", "Form_Load")]
    [InlineData("UserControl", "UserControl1", "UserControl_Initialize")]
    [InlineData("PropertyPage", "PropertyPage1", "PropertyPage_SelectionChanged")]
    public void A_double_click_on_the_background_writes_the_roots_own_event(string kind, string name, string procedure) =>
        Open(kind, name).RootDoubleClickProcedure.Should().Be(procedure);

    // Measured with the real pointer on VB6 SP6, oracle PR #4460 (#4411).
    [AvaloniaTheory]
    [InlineData("TextBox", "Text1", "Text1_Change")]
    [InlineData("Frame", "Frame1", "Frame1_DragDrop")]
    [InlineData("Shape", "Shape1", null)]
    public void A_control_double_click_uses_its_declared_event(string kind, string name, string? procedure)
    {
        var designer = Open("Form", "Form1", $"Begin VB.{kind} {name}\r\nEnd\r\n");
        designer.ControlDoubleClickProcedure(designer.AllComponents.Single(c => c.Name == name)).Should().Be(procedure);
    }

    [AvaloniaFact]
    public void A_Frame_DragDrop_stub_has_the_measured_arguments()
    {
        var designer = Open("Form", "Form1", "Begin VB.Frame Frame1\r\nEnd\r\n");
        var bus = new EventBus();
        using var editor = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), bus, Substitute.For<ILspClient>(), Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>())
            .Initialize(designer.FormDefinition!);
        bus.Publish(new CreateOrNavigateToSubEvent(designer.FormDefinition!, "Frame1_DragDrop", "Frame1", "DragDrop"));
        editor.Document.Text.ReplaceLineEndings("\n").Should().Contain("Private Sub Frame1_DragDrop(Source As Control, X As Single, Y As Single)\n\nEnd Sub");
    }

    [AvaloniaFact]
    public async Task A_TextBox_double_tap_routes_to_Change()
    {
        var bus = new EventBus();
        var editors = Substitute.For<IEditorService>();
        using var designer = Open("Form", "Form1", "Begin VB.TextBox Text1\r\nEnd\r\n", bus, editors);
        CreateOrNavigateToSubEvent? request = null;
        using var subscription = bus.Subscribe<CreateOrNavigateToSubEvent>(e => request = e);
        var view = new FormEditView { DataContext = designer };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var item = view.GetVisualDescendants().OfType<ControlItem>().Single();
            item.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                new PointerEventArgs(InputElement.PointerPressedEvent, item, new Pointer(1, PointerType.Mouse, true),
                    item, default, 0, default, KeyModifiers.None)));
            for (var i = 0; i < 100 && request is null; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            editors.Received().EditCode(designer.FormDefinition);
            request.Should().NotBeNull();
            request!.Sub.Should().Be("Text1_Change");
        }
        finally { window.Close(); }
    }

    private static CodeEditorViewModel Editor(FormEditViewModel designer, EventBus bus, ILspClient? lsp = null)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        return new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), bus, lsp ?? Substitute.For<ILspClient>(), settings,
            Substitute.For<IStatusBarService>(), Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>())
            .Initialize(designer.FormDefinition!);
    }

    // The selections and absence of a stub were measured in VB6 SP6 (oracle PR #4460).
    [AvaloniaTheory]
    [InlineData("Shape", "Shape1")]
    [InlineData("Line", "Line1")]
    public async Task Eventless_control_double_tap_opens_declarations(string kind, string name)
    {
        var bus = new EventBus();
        var editors = Substitute.For<IEditorService>();
        using var designer = Open("Form", "Form1", $"Begin VB.{kind} {name}\r\nEnd\r\n", bus, editors,
            "Option Explicit\r\nPrivate Sub Existing()\r\nEnd Sub\r\n");
        using var editor = Editor(designer, bus);
        var before = editor.Document.Text;
        editor.CaretOffset = before.Length;
        var view = new FormEditView { DataContext = designer };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var item = view.GetVisualDescendants().OfType<ControlItem>().Single();
            item.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                new PointerEventArgs(InputElement.PointerPressedEvent, item, new Pointer(1, PointerType.Mouse, true),
                    item, default, 0, default, KeyModifiers.None)));
            for (var i = 0; i < 100 && editor.SelectedProcedure != "(Declarations)"; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            editors.Received().EditCode(designer.FormDefinition);
            editor.SelectedObject.Should().Be("(General)");
            editor.SelectedProcedure.Should().Be("(Declarations)");
            editor.CaretOffset.Should().Be(before.IndexOf("Option Explicit", StringComparison.Ordinal));
            editor.Document.Text.Should().Be(before);
            editor.IsModified.Should().BeFalse();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Choosing_declarations_moves_to_the_start_of_code()
    {
        var bus = new EventBus();
        using var designer = Open("Form", "Form1", code: "Option Explicit\r\nPrivate Sub Existing()\r\nEnd Sub\r\n");
        using var editor = Editor(designer, bus);
        editor.CaretOffset = editor.Document.TextLength;
        editor.SelectedProcedure = "(Declarations)";
        editor.CaretOffset.Should().Be(editor.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task CommandButton_double_tap_creates_and_navigates_to_Click()
    {
        var bus = new EventBus();
        using var designer = Open("Form", "Form1", "Begin VB.CommandButton Command1\r\nEnd\r\n", bus,
            Substitute.For<IEditorService>());
        using var editor = Editor(designer, bus);
        var view = new FormEditView { DataContext = designer };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var item = view.GetVisualDescendants().OfType<ControlItem>().Single();
            item.RaiseEvent(new TappedEventArgs(InputElement.DoubleTappedEvent,
                new PointerEventArgs(InputElement.PointerPressedEvent, item, new Pointer(1, PointerType.Mouse, true),
                    item, default, 0, default, KeyModifiers.None)));
            for (var i = 0; i < 100 && !editor.Document.Text.Contains("Private Sub Command1_Click()", StringComparison.Ordinal); i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            editor.Document.Text.ReplaceLineEndings("\n").Should().Contain("Private Sub Command1_Click()\n\nEnd Sub");
            var end = editor.Document.GetLineByOffset(editor.Document.Text.IndexOf("End Sub", StringComparison.Ordinal));
            editor.CaretOffset.Should().Be(editor.Document.GetLineByNumber(end.LineNumber - 1).Offset);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("(Declarations)")]
    [InlineData("Existing")]
    public async Task Symbol_refresh_preserves_the_procedure_selection_without_moving_the_caret(string procedure)
    {
        var bus = new EventBus();
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(true);
        using var designer = Open("Form", "Form1", code:
            "Option Explicit\r\nPrivate Sub Existing()\r\n    ' body\r\nEnd Sub\r\n");
        using var editor = Editor(designer, bus, lsp);
        var line = editor.Document.GetLineByOffset(editor.Document.Text.IndexOf("Private Sub", StringComparison.Ordinal)).LineNumber - 1;
        var range = new HexIDE.Lsp.Messages.Range(new Position(line, 0), new Position(line + 2, 7));
        var symbol = new DocumentSymbol("Existing", SymbolKind.Method, range, range);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns([symbol]);
        var view = new CodeEditorView { DataContext = editor };
        var window = new Window { Content = view, Width = 800, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            await editor.RefreshSymbolsAsync();
            Dispatcher.UIThread.RunJobs();
            var combo = view.FindControl<ComboBox>("ProcedureCombo")!;
            combo.SelectedItem = procedure;
            editor.SelectedProcedure.Should().Be(procedure);
            var caret = editor.Document.Text.IndexOf("' body", StringComparison.Ordinal) + 6;
            editor.CaretOffset = caret;
            var before = editor.Document.Text;
            var rebuilds = 0;
            var selectionDroppedDuringClear = false;
            editor.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(editor.SelectedProcedure) && editor.SelectedProcedure is null)
                    selectionDroppedDuringClear = editor.ProcedureNames.Count == 0;
            };
            editor.ProcedureNames.CollectionChanged += (_, _) => rebuilds++;

            lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
                lsp, new PublishDiagnosticsParams(editor.GetDocumentUriPublic(), []));
            for (var i = 0; i < 100 && rebuilds == 0; i++)
                await Task.Delay(10, TestContext.Current.CancellationToken);
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            rebuilds.Should().BeGreaterThan(0);
            selectionDroppedDuringClear.Should().BeTrue();
            editor.CaretOffset.Should().Be(caret);
            editor.Document.Text.Should().Be(before);
            editor.IsModified.Should().BeFalse();
            editor.SelectedProcedure.Should().Be(procedure);
            combo.SelectedItem.Should().Be(procedure);
        }
        finally { window.Close(); }
    }
}
