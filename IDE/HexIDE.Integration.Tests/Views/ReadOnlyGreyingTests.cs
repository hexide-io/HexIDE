using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;
using AvaloniaEdit.Rendering;
using HexIDE.Bookmarks;
using HexIDE.Controls;
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
using HexIDE.Themes;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// The code window's read-only regions drawn in the theme's read-only colour (hexide-io/HexIDE#273 task 3.15).
/// </summary>
/// <remarks>
/// <para>
/// Against a rendered editor, reading the foreground each visual line element was built with, because the grey
/// is a line transformer's and only a built line shows what the transformers did, in what order.
/// </para>
/// <para>
/// <b>The themes are the real ones.</b> Classic.axaml is merged into the application's resources and the packs
/// are applied by the real <see cref="ThemeService"/>, both where the running IDE puts them, so a pack's value
/// sits over Classic's exactly as it does there. Window-level resources would sit in front of a pack and fake
/// the switch this most needs to prove.
/// </para>
/// </remarks>
public class ReadOnlyGreyingTests : IDisposable
{
    private readonly List<Window> windows = [];
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILspClient lsp = Substitute.For<ILspClient>();

    public ReadOnlyGreyingTests()
    {
        lsp.IsRunning.Returns(true);
        lsp.RequestDocumentSymbolsAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<DocumentSymbol>());
        lsp.RequestFoldingRangesAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns(Array.Empty<FoldingRange>());

        // The fold label's brush is one static for the process, so a test asserting an editor set it must not be
        // able to pass on a value an earlier test left there.
        FoldingElementGenerator.TextBrush = Unset;
    }

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        foreach (var vm in made) vm.Dispose();
        FoldingElementGenerator.TextBrush = FoldingElementGenerator.DefaultTextBrush;
        GC.SuppressFinalize(this);
    }

    /// <summary>A fold-label brush no theme and no library default uses.</summary>
    private static readonly IBrush Unset = new SolidColorBrush(Color.Parse("#123456"));

    private static Color? FoldLabelColour => (FoldingElementGenerator.TextBrush as ISolidColorBrush)?.Color;

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

    private static readonly Color ClassicGrey = Color.Parse("#707070");
    private static readonly Color DarkGrey = Color.Parse("#929292");
    private static readonly Color AbyssGrey = Color.Parse("#7C7C7C");

    /// <summary>
    /// Merges Classic.axaml into the application's resources, as App.axaml does, and undoes it and any pack the
    /// test applied when disposed.
    /// </summary>
    private static IDisposable TheIdesThemes(ThemeService themes)
    {
        var classic = new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://HexIDE/Themes/Classic.axaml"),
        };
        Application.Current!.Resources.MergedDictionaries.Insert(0, classic);
        return new Restore(() =>
        {
            themes.Apply("Classic");
            Application.Current!.Resources.MergedDictionaries.Remove(classic);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private sealed class Restore(Action undo) : IDisposable
    {
        public void Dispose() => undo();
    }

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

    private CodeEditorViewModel OpenForm()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        return MakeViewModel().Initialize(new FormDeserializer().Deserialize(project, Frm, NullSink.Instance)!);
    }

    private CodeEditorViewModel OpenModule(ModuleKind kind, string name, string code)
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, name, kind);
        module.UpdateCode(code);
        return MakeViewModel().Initialize(module);
    }

    private TextEditor Show(CodeEditorViewModel vm)
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        Dispatcher.UIThread.RunJobs();
        return view.FindControl<TextEditor>("TextEditor")!;
    }

    private static void ExpandHeader(TextEditor editor)
    {
        var folding = (FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!;
        folding.GetFoldingsAt(0).Single().IsFolded = false;
        Dispatcher.UIThread.RunJobs();
    }

    /// <summary>The colour each element with text on the 1-based <paramref name="line"/> was drawn in.</summary>
    private static List<Color?> ColoursOn(TextEditor editor, int line)
    {
        var visual = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(line));
        return visual.Elements
            .Where(e => e.DocumentLength > 0)
            .Select(e => (e.TextRunProperties.ForegroundBrush as ISolidColorBrush)?.Color)
            .ToList();
    }

    /// <summary>The links AvaloniaEdit made on the 1-based <paramref name="line"/>.</summary>
    private static List<VisualLineLinkText> LinksOn(TextEditor editor, int line) =>
        editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(line))
            .Elements.OfType<VisualLineLinkText>().ToList();

    private static int LineOf(TextEditor editor, string text) =>
        editor.Document.GetLineByOffset(editor.Document.Text.IndexOf(text, StringComparison.Ordinal)).LineNumber;

    // ── The deltas' scenarios ───────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void OpeningAForm()
    {
        // The code-editor delta's scenario: the designer block and attribute lines folded into one line, shown
        // greyed out. Folded, that line is the fold's label, which AvaloniaEdit draws in one brush of its own.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenForm();
        var editor = Show(vm);

        var fold = ((FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!).GetFoldingsAt(0).Single();
        fold.IsFolded.Should().BeTrue("the header is folded when the window opens");
        fold.EndOffset.Should().Be(editor.Document.GetLineByNumber(HeaderLines).EndOffset,
            "into one line, the designer block and the attribute lines together");
        FoldLabelColour.Should().Be(ClassicGrey,
            "the folded header's label is drawn in the theme's read-only colour");

        ExpandHeader(editor);
        for (var line = 1; line <= HeaderLines; line++)
            ColoursOn(editor, line).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, $"header line {line} is greyed");
        for (var line = HeaderLines + 1; line <= HeaderLines + 5; line++)
            ColoursOn(editor, line).Should().NotContain(ClassicGrey, $"line {line} is code");
    }

    [AvaloniaFact]
    public void OpeningAStandardModule()
    {
        // The delta's scenario: a one-line header is greyed, cannot be edited, and is not folded.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var editor = Show(OpenModule(ModuleKind.StandardModule, "Module1", "Option Explicit\r\n"));
        var header = editor.Document.GetLineByNumber(1);
        editor.Document.GetText(header).Should().Be("Attribute VB_Name = \"Module1\"", "the premise: the header is its one line");

        ((FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!).GetFoldingsAt(0).Should().BeEmpty();
        ColoursOn(editor, 1).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey);
        ColoursOn(editor, 2).Should().NotContain(ClassicGrey);

        // Asked of the section provider the window installs, which typing, pasting and deleting all consult.
        var sections = editor.TextArea.ReadOnlySectionProvider;
        sections.CanInsert(0).Should().BeFalse("the header line cannot be typed into at its start");
        sections.CanInsert(header.Offset + 10).Should().BeFalse("nor inside it");
        sections.GetDeletableSegments(new TextSegment { StartOffset = header.Offset, Length = header.Length })
            .Sum(s => s.Length).Should().Be(0, "nor can any of it be deleted");
        sections.CanInsert(editor.Document.GetLineByNumber(2).Offset).Should().BeTrue("the code below it can be edited");
    }

    // ── What else is greyed, and what is not ───────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AMembersAttributeLinesAreGreyedAndTheDeclarationTheyDescribeIsNot()
    {
        // Phase 4's attribute runs are read-only already, and are greyed by the same colour as the header.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var editor = Show(OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\n    Debug.Print 1\r\nEnd Sub\r\n"));

        var attribute = LineOf(editor, "Attribute Go.VB_Description");
        ColoursOn(editor, attribute).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey);
        ColoursOn(editor, attribute - 1).Should().NotContain(ClassicGrey, "the declaration is code");
        ColoursOn(editor, attribute + 1).Should().NotContain(ClassicGrey, "so is the body");
    }

    [AvaloniaFact]
    public void AnAttributeLineThatDescribesNothing()
    {
        // The code-editor delta's scenario (#273 task 4.1): a run VB6 would not give to the line above it, here a
        // body statement, is shown and edited as ordinary text, and the file keeps it. Beside it, a run that does
        // describe its member, which is greyed and refuses the same keystroke, so the typing path is shown to be live.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var project = new ProjectDefinition(VBProjectType.EXE, "P");
        var module = new ModuleDefinition(project, "Order", ModuleKind.ClassModule);
        module.UpdateCode(
            "Option Explicit\r\n\r\n" +
            "Public Sub Halt()\r\n" +
            "Attribute Halt.VB_Description = \"Stops\"\r\n" +
            "End Sub\r\n" +
            "Public Sub Go()\r\n" +
            "    Debug.Print 1\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "End Sub\r\n");
        var vm = MakeViewModel().Initialize(module);
        var editor = Show(vm);
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();

        var orphan = editor.Document.GetLineByNumber(LineOf(editor, "Attribute Go.VB_Description"));
        var described = editor.Document.GetLineByNumber(LineOf(editor, "Attribute Halt.VB_Description"));
        ColoursOn(editor, orphan.LineNumber).Should().NotBeEmpty().And.NotContain(ClassicGrey, "the orphan is shown as code");
        ColoursOn(editor, described.LineNumber).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "the control is greyed");

        var sections = editor.TextArea.ReadOnlySectionProvider;
        sections.CanInsert(orphan.Offset).Should().BeTrue();
        sections.GetDeletableSegments(new TextSegment { StartOffset = orphan.Offset, Length = orphan.TotalLength })
            .Sum(s => s.Length).Should().Be(orphan.TotalLength, "all of it can be deleted");

        var before = vm.Document.Text;
        editor.TextArea.Caret.Offset = described.Offset + "Attribute Halt.VB_Description = \"".Length;
        TypeText(editor, "Really ");
        vm.Document.Text.Should().Be(before, "the control: typing into a member's own run is refused");

        editor.TextArea.Caret.Offset = orphan.Offset + "Attribute Go.VB_Description = \"".Length;
        TypeText(editor, "Really ");
        vm.Document.Text.Should().Contain("Attribute Go.VB_Description = \"Really Goes\"", "the orphan is typed into");

        vm.BufferBody.Should().Contain("    Debug.Print 1\r\nAttribute Go.VB_Description = \"Really Goes\"\r\nEnd Sub",
            "and the file keeps it, where it was");
    }

    [AvaloniaFact]
    public void AnEditToTheLineAboveARunRedrawsItWhenItTurnsIntoAnOrphanOrBack()
    {
        // #273 task 4.1: whether a run is read-only depends on the line above it, and AvaloniaEdit rebuilds only the
        // lines an edit touches. No language server publishes here, so nothing else redraws the run.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\n" +
            "Public Clock As Timer, Alarm As Timer\r\n" +
            "Attribute Alarm.VB_VarDescription = \"Rings\"\r\n" +
            "\r\n" +
            "Public Sub Halt()\r\n" +
            "Attribute Halt.VB_Description = \"Stops at https://example.com/halt\"\r\n" +
            "End Sub\r\n");
        var editor = Show(vm);
        editor.TextArea.Focus();
        Dispatcher.UIThread.RunJobs();
        var sections = editor.TextArea.ReadOnlySectionProvider;
        var alarm = LineOf(editor, "Attribute Alarm");
        var halt = LineOf(editor, "Attribute Halt");
        ColoursOn(editor, alarm).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "the premise: both runs are greyed");
        ColoursOn(editor, halt).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey);
        LinksOn(editor, halt).Should().BeEmpty("and the address in one is plain");

        // Commenting out the declaration leaves its run under a comment, which VB6 gives nothing to.
        editor.TextArea.Caret.Offset = editor.Document.GetLineByNumber(halt - 1).Offset;
        TypeText(editor, "'");
        sections.CanInsert(editor.Document.GetLineByNumber(halt).Offset).Should().BeTrue("the premise: the run is an orphan now");
        ColoursOn(editor, halt).Should().NotContain(ClassicGrey, "an orphan is drawn as code");
        LinksOn(editor, halt).Should().ContainSingle("and its address is a link again");

        editor.Document.Remove(editor.Document.GetLineByNumber(halt - 1).Offset, 1);
        Dispatcher.UIThread.RunJobs();
        ColoursOn(editor, halt).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "uncommented, it is the procedure's again");
        LinksOn(editor, halt).Should().BeEmpty();

        // Retyping one variable of a line declaring two leaves the run that named it naming nothing the line
        // declares, and the line declares two, so no rename can be read into it.
        var declaration = editor.Document.GetLineByNumber(alarm - 1);
        editor.TextArea.Caret.Offset = declaration.Offset + "Public Clock As Timer, Alarm".Length;
        TypeText(editor, "s");
        sections.CanInsert(editor.Document.GetLineByNumber(alarm).Offset).Should().BeTrue("the premise: the run is an orphan now");
        ColoursOn(editor, alarm).Should().NotContain(ClassicGrey);

        editor.Document.Remove(declaration.Offset + "Public Clock As Timer, Alarm".Length, 1);
        Dispatcher.UIThread.RunJobs();
        ColoursOn(editor, alarm).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "named again, it is the variable's");
    }

    /// <summary>Typed text as the input system delivers it: a TextInput event on the focused text area.</summary>
    private static void TypeText(TextEditor editor, string text)
    {
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public void AnErrorInsideTheHeaderStillShowsRed()
    {
        // A diagnostic is a server's claim about the file, and greying it away would hide the claim. So the grey
        // runs before the diagnostics colouring, and the error's red is drawn over it.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenForm();
        var editor = Show(vm);
        ExpandHeader(editor);

        lsp.DiagnosticsPublished += Raise.Event<EventHandler<PublishDiagnosticsParams>>(
            lsp, new PublishDiagnosticsParams(vm.GetDocumentUriPublic(), [new Diagnostic(
                new LspRange(new Position(2, 3), new Position(2, 10)), "not a property", DiagnosticSeverity.Error)]));
        Dispatcher.UIThread.RunJobs();

        var colours = ColoursOn(editor, 3);
        colours.Should().Contain(Colors.Red, "the error's text is red");
        colours.Should().Contain(ClassicGrey, "and the rest of the line is still grey");
    }

    [AvaloniaFact]
    public void WithNoReadOnlyColourInTheThemeNothingIsGreyed()
    {
        // No literal stands in for a theme that does not resolve the key: here, no theme at all is merged, so the
        // header keeps its syntax colours rather than taking a grey no theme could reach.
        var vm = OpenForm();
        var editor = Show(vm);
        ExpandHeader(editor);

        // Compared with the same lines built without the greying at all, so that a stand-in grey of any value
        // fails here, not only Classic's.
        var textView = editor.TextArea.TextView;
        var greying = textView.LineTransformers.OfType<ReadOnlyRegionColorizer>().Single();
        greying.Brush.Should().BeNull("the key does not resolve");
        var drawn = Enumerable.Range(1, HeaderLines).Select(line => ColoursOn(editor, line)).ToList();
        textView.LineTransformers.Remove(greying);
        var ungreyed = Enumerable.Range(1, HeaderLines).Select(line => ColoursOn(editor, line)).ToList();

        drawn.Should().BeEquivalentTo(ungreyed, o => o.WithStrictOrdering(), "the header is drawn as if not greyed");
        ungreyed.SelectMany(colours => colours).Should().Contain(c => c != Colors.Black,
            "the comparison is of a header with syntax colours in it");
        FoldLabelColour.Should().Be(((ISolidColorBrush)Unset).Color, "and no fold label colour was taken either");
    }

    [AvaloniaFact]
    public void AnAddressInsideARegionIsGreyedAndOneInTheCodeIsStillALink()
    {
        // AvaloniaEdit gives a link its own colour after every line transformer has run, so the grey alone would
        // leave an address in a member's description blue and underlined.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var editor = Show(OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\nPublic Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"See https://example.com/orders or ask help@example.com\"\r\n" +
            "    ' https://example.com/orders\r\nEnd Sub\r\n"));

        var attribute = LineOf(editor, "Attribute Go.VB_Description");
        ColoursOn(editor, attribute).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey);
        LinksOn(editor, attribute).Should().BeEmpty("an address in a read-only region is text, as in VB6");
        LinksOn(editor, attribute + 1).Should().ContainSingle("an address in the developer's code is still a link");
    }

    // ── Following the theme ────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void SwitchingThemeRegreysAnOpenWindow()
    {
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var editor = Show(OpenForm());
        ExpandHeader(editor);
        ColoursOn(editor, 2).Should().AllBeEquivalentTo(ClassicGrey);

        themes.Apply("Dark");
        Dispatcher.UIThread.RunJobs();

        ColoursOn(editor, 2).Should().AllBeEquivalentTo(DarkGrey);
        FoldLabelColour.Should().Be(DarkGrey);
    }

    [AvaloniaFact]
    public void SwitchingBetweenTwoDarkPacksRegreysAnOpenWindow()
    {
        // Dark and Abyss are both dark, so the switch changes no theme variant and raises nothing the syntax
        // palette listens for. Only the key's own value changes, and that is what the window has to follow.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        themes.Apply("Dark");
        Dispatcher.UIThread.RunJobs();
        var editor = Show(OpenForm());
        ExpandHeader(editor);
        ColoursOn(editor, 2).Should().AllBeEquivalentTo(DarkGrey);
        var variant = Application.Current!.ActualThemeVariant;

        themes.Apply("Abyss");
        Dispatcher.UIThread.RunJobs();

        Application.Current.ActualThemeVariant.Should().Be(variant, "the test is of a switch that changes no variant");
        ColoursOn(editor, 2).Should().AllBeEquivalentTo(AbyssGrey);
        FoldLabelColour.Should().Be(AbyssGrey);
    }

    /// <summary>
    /// Gives the application a read-only colour of its own over the theme's, as a pack differing from another in
    /// that colour alone would, and takes it away when disposed.
    /// </summary>
    /// <remarks>
    /// A real switch between Dark and Abyss changes the text colour too, which can repaint an editor on its own and
    /// so hide a window that does not follow the read-only colour at all. This changes nothing else.
    /// </remarks>
    private static IDisposable OnlyTheReadOnlyColour(Color colour)
    {
        var overriding = new ResourceDictionary { [ReadOnlyTextKey] = new SolidColorBrush(colour) };
        Application.Current!.Resources.MergedDictionaries.Add(overriding);
        Dispatcher.UIThread.RunJobs();
        return new Restore(() =>
        {
            Application.Current!.Resources.MergedDictionaries.Remove(overriding);
            Dispatcher.UIThread.RunJobs();
        });
    }

    private const string ReadOnlyTextKey = "ReadOnlyTextBrush";

    [AvaloniaFact]
    public void ChangingOnlyTheReadOnlyColourRegreysAnOpenWindow()
    {
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var editor = Show(OpenForm());
        ExpandHeader(editor);
        ColoursOn(editor, 2).Should().AllBeEquivalentTo(ClassicGrey);

        var other = Color.Parse("#5A5A5A");
        using (OnlyTheReadOnlyColour(other))
        {
            ColoursOn(editor, 2).Should().AllBeEquivalentTo(other);
            FoldLabelColour.Should().Be(other);
        }

        ColoursOn(editor, 2).Should().AllBeEquivalentTo(ClassicGrey, "and back again when it goes");
    }

    [AvaloniaFact]
    public void ACarriedFileIsRepaintedWhenOnlyTheReadOnlyColourChanges()
    {
        // A carried file has no header to grey, but it folds, and every fold's label takes the read-only colour.
        // A label is drawn when its line is built, so the colour reaches one already on screen only when the
        // editor rebuilds its lines, which nothing else asks it to do here.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hexide-greying-" + Guid.NewGuid().ToString("N") + ".md");
        System.IO.File.WriteAllText(path, "# Notes\none\ntwo\nthree\n");
        try
        {
            var vm = new RelatedDocumentEditorViewModel(lsp)
                .Initialize(new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "notes.md", path));
            var view = new RelatedDocumentEditorView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 700 };
            windows.Add(window);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var textView = view.FindControl<TextEditor>("TextEditor")!.TextArea.TextView;
            FoldLabelColour.Should().Be(ClassicGrey, "the carried file set the fold labels' colour from the theme on opening");
            textView.EnsureVisualLines();
            var before = textView.GetVisualLine(1);
            before.Should().NotBeNull();

            var other = Color.Parse("#5A5A5A");
            using (OnlyTheReadOnlyColour(other))
            {
                FoldLabelColour.Should().Be(other, "and follows it, with no code window open to do so");
                textView.EnsureVisualLines();
                textView.GetVisualLine(1).Should().NotBeSameAs(before, "the editor rebuilt its lines for the new colour");
            }
        }
        finally
        {
            System.IO.File.Delete(path);
        }
    }

    [AvaloniaFact]
    public void TheProtocolInspectorFollowsTheReadOnlyColourToo()
    {
        // The inspector folds a message body, and its labels share the one brush. Were it not to follow the theme,
        // a label there would keep the grey of whichever theme some other editor last saw.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var view = new HexIDE.Tools.ProtocolInspector.ProtocolInspectorToolView();
        var body = view.FindControl<TextEditor>("Body")!;
        body.Text = "{\n  \"a\": 1\n}\n";
        var window = new Window { Content = view, Width = 900, Height = 700 };
        windows.Add(window);
        window.Show();
        Dispatcher.UIThread.RunJobs();
        FoldLabelColour.Should().Be(ClassicGrey, "the inspector set the fold labels' colour from the theme on opening");

        var textView = body.TextArea.TextView;
        textView.EnsureVisualLines();
        var before = textView.GetVisualLine(1);
        before.Should().NotBeNull();

        var other = Color.Parse("#5A5A5A");
        using (OnlyTheReadOnlyColour(other))
        {
            FoldLabelColour.Should().Be(other);
            textView.EnsureVisualLines();
            textView.GetVisualLine(1).Should().NotBeSameAs(before, "the inspector rebuilt its lines for the new colour");
        }
    }
}
