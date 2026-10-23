using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using AvaloniaEdit;
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
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Where the caret is when a code window opens with nowhere asked for: on the first line that can be typed on, below
/// the file's header (hexide-io/HexIDE#1518).
/// </summary>
/// <remarks>
/// It used to open at offset 0, which is the header's <c>VERSION</c> line, folded and read-only, so the first keystroke
/// in a window opened by View Code was refused and nothing appeared. VB6 shows no header, and its View Code puts the
/// caret in the code. Against a real editor, because the view writes its caret back to the view model as it attaches,
/// and only the editor's own caret says where a keystroke lands.
/// </remarks>
public class OpeningCaretTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public OpeningCaretTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Form_Load()\r\n" +
        "End Sub\r\n";

    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private CodeEditorViewModel MakeViewModel()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), new BookmarkService(), new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm;
    }

    private (Window Window, TextEditor Editor) Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return (window, view.FindControl<TextEditor>("TextEditor")!);
    }

    /// <summary>Types <paramref name="text"/> into the editor as keystrokes, where its caret is.</summary>
    private static void Type(Window window, TextEditor editor, string text)
    {
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        window.KeyTextInput(text);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void A_form_opens_with_the_caret_on_its_first_line_of_code_and_the_first_keystroke_lands_there()
    {
        var vm = MakeViewModel().Initialize(new FormDeserializer().Deserialize(Project, Frm, NullSink.Instance)!);
        var code = vm.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal);

        var (window, editor) = Show(vm);

        editor.CaretOffset.Should().Be(code, "the header above it is the IDE's, and typing there is refused");
        vm.CaretOffset.Should().Be(code);
        Type(window, editor, "'");
        vm.Document.Text.Should().Contain("Attribute VB_PredeclaredId = True\r\n'Option Explicit\r\n");
    }

    [AvaloniaFact]
    public void A_class_opens_with_the_caret_below_its_header()
    {
        var module = new ModuleDefinition(Project, "Order", ModuleKind.ClassModule);
        module.UpdateCode("Option Explicit\r\n\r\nPublic Sub Clear()\r\nEnd Sub\r\n");
        var vm = MakeViewModel().Initialize(module);

        var (_, editor) = Show(vm);

        vm.Document.Text.Should().StartWith("VERSION 1.0 CLASS", "a class's header is in its window too");
        editor.CaretOffset.Should().Be(vm.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public void A_new_form_opens_with_the_caret_after_its_Attribute_lines()
    {
        // Add Form, then View Code: the commonest way into this, and since #1369 a new form has a header too.
        var vm = MakeViewModel().Initialize(FormDefinition.NewForm(Project, FormComponentClass.Instance, "Form2"));
        const string lastAttribute = "Attribute VB_Exposed = False\r\n";
        var code = vm.Document.Text.IndexOf(lastAttribute, StringComparison.Ordinal) + lastAttribute.Length;

        var (window, editor) = Show(vm);

        editor.CaretOffset.Should().Be(code);
        Type(window, editor, "Dim");
        vm.Document.Text.Should().Contain(lastAttribute + "Dim");
    }

    [AvaloniaFact]
    public void A_place_asked_for_before_the_window_is_shown_is_kept()
    {
        // Navigating to a document not yet open places its caret before there is a view; the opening caret is the
        // view model's, set as it loads, so it never overrides that.
        var vm = MakeViewModel().Initialize(new FormDeserializer().Deserialize(Project, Frm, NullSink.Instance)!);
        var sub = vm.Document.Text.IndexOf("Private Sub Form_Load", StringComparison.Ordinal);
        vm.CaretOffset = sub;

        var (_, editor) = Show(vm);

        editor.CaretOffset.Should().Be(sub);
    }
}
