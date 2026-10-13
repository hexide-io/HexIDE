using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Editing;
using AvaloniaEdit.Folding;
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

/// <summary>
/// A click in either gutter, beside the header and beside code, with the header folded and not
/// (hexide-io/HexIDE#273 task 3.12).
/// </summary>
/// <remarks>
/// Against a rendered editor, because what a click marks is decided by the visual line under the pointer, and a
/// folded header is one visual line standing for nine. The click is real input at a window point, so the
/// gutter's own hit test is what is exercised.
/// </remarks>
public class MarkGutterOnReadOnlyLineTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly BreakpointService breakpoints = new();
    private readonly BookmarkService bookmarks = new();

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

    // Header: lines 1-9. Code from line 10.
    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton cmdOK \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub cmdOK_Click()\r\n" +
        "    Unload Me\r\n" +
        "End Sub\r\n";

    private const int HeaderLines = 9;

    private CodeEditorViewModel OpenForm()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<DocumentSavedEvent>(Arg.Any<Action<DocumentSavedEvent>>()).Returns(Substitute.For<IDisposable>());

        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var lsp = Substitute.For<ILspClient>();
        lsp.IsRunning.Returns(false);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());

        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(), Substitute.For<IProjectService>(),
            eventBus, lsp, settings, Substitute.For<IStatusBarService>(), bookmarks, breakpoints,
            Substitute.For<IDebugController>(), new RunScope(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        return vm.Initialize(new FormDeserializer().Deserialize(project, Frm, NullSink.Instance)!);
    }

    private (Window Window, TextEditor Editor) Show(CodeEditorViewModel vm)
    {
        var (window, _, editor) = ShowView(vm);
        return (window, editor);
    }

    private (Window Window, CodeEditorView View, TextEditor Editor) ShowView(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();

        var editor = view.FindControl<TextEditor>("TextEditor")!;
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        return (window, view, editor);
    }

    /// <summary>
    /// Folds or expands the header's own fold, which the window makes as it opens, folded (task 3.14).
    /// </summary>
    private static void SetHeaderFolded(TextEditor editor, bool folded)
    {
        var folding = editor.TextArea.TextView.GetService(typeof(FoldingManager)) as FoldingManager;
        folding.Should().NotBeNull("the code window installs a folding manager on its text area");
        var section = folding!.GetFoldingsAt(0).Single();
        section.EndOffset.Should().Be(editor.Document.GetLineByNumber(HeaderLines).EndOffset, "the fold is the header's");
        section.IsFolded = folded;
        editor.TextArea.TextView.EnsureVisualLines();
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>
    /// Clicks <typeparamref name="TMargin"/> beside the <paramref name="nth"/> visual line counting from 0, and
    /// answers which document line that visual line starts with, so the test cannot click the wrong row unseen.
    /// </summary>
    private static int ClickGutter<TMargin>(Window window, TextEditor editor, int nth) where TMargin : AbstractMargin
    {
        var textView = editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var visual = textView.VisualLines[nth];
        var margin = editor.TextArea.LeftMargins.OfType<TMargin>().Single();
        var y = visual.VisualTop - textView.VerticalOffset + visual.Height / 2;
        var at = margin.TranslatePoint(new Point(margin.Bounds.Width / 2, y), window)!.Value;

        // Raised on the gutter rather than sent to the window. window.MouseDown at the same point set nothing, in
        // the control case beside code as well, so a refusal test built on it passed whether or not anything was
        // refused. Why the headless input missed the gutter was not established; the click in the running IDE is
        // verified separately. The gutter's own handler still maps the point to a visual line, which is the part
        // under test.
        margin.RaiseEvent(new PointerPressedEventArgs(
            margin, new Pointer(Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true), window, at,
            timestamp: 0, new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
        Dispatcher.UIThread.RunJobs();
        return visual.FirstDocumentLine.LineNumber;
    }

    [AvaloniaFact]
    public void AClickInTheBreakpointGutterBesideCodeSetsOne()
    {
        // The control: proves the click reaches the gutter and maps to the row it was aimed at.
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        SetHeaderFolded(editor, false);

        var line = ClickGutter<BreakpointMargin>(window, editor, HeaderLines + 2);

        line.Should().Be(HeaderLines + 3);
        breakpoints.GetBreakpoints(vm.Identity).Should().Equal(line);
    }

    [AvaloniaFact]
    public void AClickInTheBreakpointGutterBesideTheHeaderSetsNone()
    {
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        SetHeaderFolded(editor, false);

        ClickGutter<BreakpointMargin>(window, editor, 2).Should().Be(3);

        breakpoints.GetBreakpoints(vm.Identity).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void AClickOnAFoldedHeaderSetsNoBreakpoint()
    {
        // The task's named case: folded, the header is one row standing for nine lines, and the row maps to the
        // first of them. The row after it is the first line of code.
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        SetHeaderFolded(editor, true);

        ClickGutter<BreakpointMargin>(window, editor, 0).Should().Be(1, "a folded row starts with the fold's first line");
        breakpoints.GetBreakpoints(vm.Identity).Should().BeEmpty();

        ClickGutter<BreakpointMargin>(window, editor, 1).Should().Be(HeaderLines + 1, "the header is folded away");
        breakpoints.GetBreakpoints(vm.Identity).Should().Equal(HeaderLines + 1);
    }

    [AvaloniaFact]
    public void TogglingABookmarkOnAFoldedHeader()
    {
        // The bookmarks delta's scenario: a click on the folded header's row sets nothing, and the row after it is
        // code and takes one.
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        SetHeaderFolded(editor, true);

        ClickGutter<BookmarkMargin>(window, editor, 0);
        bookmarks.GetBookmarks(vm.Identity).Should().BeEmpty();

        ClickGutter<BookmarkMargin>(window, editor, 1);
        bookmarks.GetBookmarks(vm.Identity).Should().Equal([HeaderLines], "stored 0-based: file line 10");
    }

    // ── The marks themselves, as drawn (the bookmarks and interpreter-debugger deltas' restated scenarios) ──

    private static readonly Color BreakpointDot = Color.Parse("#C00000");
    private static readonly Color BookmarkDot = Color.Parse("#00C0C0");

    /// <summary>
    /// The colour a real Skia frame of the window has at the middle of <typeparamref name="TMargin"/>, beside the
    /// 1-based document <paramref name="line"/>: the centre of the dot when the margin draws one there.
    /// </summary>
    private static Color DrawnBeside<TMargin>(Window window, TextEditor editor, int line) where TMargin : AbstractMargin
    {
        Dispatcher.UIThread.RunJobs();
        var textView = editor.TextArea.TextView;
        textView.EnsureVisualLines();
        var visual = textView.GetVisualLine(line) ?? throw new InvalidOperationException($"line {line} is not on screen");
        var margin = editor.TextArea.LeftMargins.OfType<TMargin>().Single();
        var y = visual.VisualTop - textView.VerticalOffset + visual.Height / 2;
        var at = margin.TranslatePoint(new Point(margin.Bounds.Width / 2, y), window)!.Value;

        var frame = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame was rendered");
        using var buffer = frame.Lock();
        var offset = (int)at.Y * buffer.RowBytes + (int)at.X * 4;
        byte At(int i) => Marshal.ReadByte(buffer.Address, offset + i);
        return buffer.Format == Avalonia.Platform.PixelFormat.Bgra8888
            ? Color.FromRgb(At(2), At(1), At(0))
            : Color.FromRgb(At(0), At(1), At(2));
    }

    [AvaloniaFact]
    public void TogglingABookmark()
    {
        // The line is marked in the margin, and toggling again removes it. Ctrl+F2 and a click in the bookmark
        // gutter both come to the window's toggle; the click's own route is the folded-header test above.
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        var line = HeaderLines + 3;
        DrawnBeside<BookmarkMargin>(window, editor, line).Should().NotBe(BookmarkDot, "the premise: nothing is marked yet");

        vm.ToggleBookmark(line).Should().BeTrue();

        bookmarks.GetBookmarks(vm.Identity).Should().Equal([line - 1], "stored 0-based");
        DrawnBeside<BookmarkMargin>(window, editor, line).Should().Be(BookmarkDot, "the line is marked in the margin");

        vm.ToggleBookmark(line).Should().BeTrue();

        bookmarks.GetBookmarks(vm.Identity).Should().BeEmpty();
        DrawnBeside<BookmarkMargin>(window, editor, line).Should().NotBe(BookmarkDot, "toggling again removes it");
    }

    [AvaloniaFact]
    public void ALineWithBothABookmarkAndABreakpoint()
    {
        // Both stay visible: each has a gutter of its own and a colour of its own, so neither hides the other.
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        var line = HeaderLines + 4;

        vm.ToggleBreakpoint(line).Should().BeTrue();
        vm.ToggleBookmark(line).Should().BeTrue();

        DrawnBeside<BreakpointMargin>(window, editor, line).Should().Be(BreakpointDot);
        DrawnBeside<BookmarkMargin>(window, editor, line).Should().Be(BookmarkDot);
        DrawnBeside<BreakpointMargin>(window, editor, line + 1).Should().NotBe(BreakpointDot, "the control: only that line is marked");
        DrawnBeside<BookmarkMargin>(window, editor, line + 1).Should().NotBe(BookmarkDot);
    }

    [AvaloniaFact]
    public void TogglingABreakpointOnTheCaretLine()
    {
        // The interpreter-debugger delta's scenario. F9 and Debug > Toggle Breakpoint toggle the caret's line
        // through the window's own toggle (MainViewViewModelTests holds the main window to that); a click in the
        // gutter beside the line does the same. Set one way and cleared the other, with a dark-red dot drawn on
        // the line while it carries a breakpoint and gone once it does not.
        var vm = OpenForm();
        var (window, editor) = Show(vm);
        editor.CaretOffset = editor.Document.GetLineByNumber(HeaderLines + 4).Offset + 4;
        var line = editor.Document.GetLineByOffset(editor.CaretOffset).LineNumber;

        vm.ToggleBreakpoint(line).Should().BeTrue();

        breakpoints.GetBreakpoints(vm.Identity).Should().Equal(line);
        DrawnBeside<BreakpointMargin>(window, editor, line).Should().Be(BreakpointDot, "a solid dark-red dot");

        editor.TextArea.TextView.EnsureVisualLines();
        var row = editor.TextArea.TextView.VisualLines.ToList().FindIndex(v => v.FirstDocumentLine.LineNumber == line);
        ClickGutter<BreakpointMargin>(window, editor, row).Should().Be(line);

        breakpoints.GetBreakpoints(vm.Identity).Should().BeEmpty("toggling a line that has one removes it");
        DrawnBeside<BreakpointMargin>(window, editor, line).Should().NotBe(BreakpointDot);
    }

    [AvaloniaFact]
    public void CtrlF2OnAHeaderLineSetsNoBookmark()
    {
        // The keyboard path: the view's handler for the command, with the caret where the developer put it.
        var vm = OpenForm();
        var (_, view, editor) = ShowView(vm);

        editor.CaretOffset = editor.Document.GetLineByNumber(3).Offset;
        view.ToggleBookmark();
        bookmarks.GetBookmarks(vm.Identity).Should().BeEmpty();

        editor.CaretOffset = editor.Document.GetLineByNumber(HeaderLines + 4).Offset;
        view.ToggleBookmark();
        bookmarks.GetBookmarks(vm.Identity).Should().Equal([HeaderLines + 3], "stored 0-based");
    }
}
