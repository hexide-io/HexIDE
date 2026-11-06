using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using HexIDE.Language;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter.ConditionalCompilation;
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

    // Dispatcher jobs leave their captured context on the thread, including an earlier test's TestContext (#2346).
    private static void RunJobsKeepingContext()
    {
        var context = ExecutionContext.Capture();
        try
        {
            Dispatcher.UIThread.RunJobs();
        }
        finally
        {
            if (context is not null)
                ExecutionContext.Restore(context);
        }
    }

    [AvaloniaFact]
    public void DrainingJobsKeepsTheTestsExecutionContext()
    {
        var marker = new AsyncLocal<string> { Value = "queued job" };
        Dispatcher.UIThread.Post(() => marker.Value.Should().Be("queued job"));
        marker.Value = "current test";

        RunJobsKeepingContext();

        marker.Value.Should().Be("current test");
    }

    private static async Task WaitForConditionalEvaluation(CodeEditorView view)
    {
        var elapsed = Stopwatch.StartNew();
        var timeout = TimeSpan.FromSeconds(10);
        while (view.IsConditionalCompilationPending && elapsed.Elapsed < timeout)
        {
            // A dispatcher job can carry another test's TestContext (#2473); this wait has its own upper bound.
            await Task.Delay(10, CancellationToken.None);
            RunJobsKeepingContext();
        }
        view.IsConditionalCompilationPending.Should().BeFalse(
            "the debounced conditional-compilation evaluation should complete within {0}", timeout);
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
            RunJobsKeepingContext();
        });
    }

    /// <summary>
    /// Takes Classic.axaml out of the application's resources, where the harness keeps it as App.axaml does (#908), and
    /// puts it back when disposed: a theme that resolves none of the IDE's keys.
    /// </summary>
    private static IDisposable NoTheme()
    {
        var merged = Application.Current!.Resources.MergedDictionaries;
        var classic = merged.OfType<ResourceInclude>()
            .Where(r => r.Source?.ToString() == "avares://HexIDE/Themes/Classic.axaml").ToList();
        var at = classic.Select(merged.IndexOf).ToList();
        foreach (var dictionary in classic)
            merged.Remove(dictionary);
        return new Restore(() =>
        {
            for (var i = 0; i < classic.Count; i++)
                merged.Insert(Math.Min(at[i], merged.Count), classic[i]);
            RunJobsKeepingContext();
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
        RunJobsKeepingContext();

        var view = new CodeEditorView { DataContext = vm };
        window.Content = view;
        RunJobsKeepingContext();
        return view.FindControl<TextEditor>("TextEditor")!;
    }

    /// <summary>
    /// Has each named member's attribute run expanded in <paramref name="vm"/>'s window, as though the developer had
    /// opened it there, a run an edit only makes later included.
    /// </summary>
    /// <remarks>
    /// These tests read how a run's own lines are drawn, and since #273 task 4.3 a run is folded by default, its lines
    /// drawn as one box after the declaration. Expanded through the view model before the window opens rather than
    /// through the folds afterwards, so the test does nothing between an edit and its assertion that redraws the run.
    /// The window's own re-made folds still redraw it, which since 4.3 is a second reason a changed run is redrawn
    /// besides <see cref="ReadOnlyRegionRedraw"/>. How a folded run is drawn is
    /// <see cref="AFoldedRunIsDrawnAsABoxInTheReadOnlyColourAndAnExpandedOneIsGreyedLineByLine"/>.
    /// </remarks>
    private static void ExpandRuns(CodeEditorViewModel vm, params string[] members)
    {
        foreach (var member in members)
            vm.ExpandedMemberRuns.Add(new MemberRunKey(member.ToUpperInvariant(), 0));
    }

    private static void ExpandHeader(TextEditor editor)
    {
        var folding = (FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!;
        folding.GetFoldingsAt(0).Single().IsFolded = false;
        RunJobsKeepingContext();
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

    [AvaloniaTheory]
    [InlineData(0, 6)]
    [InlineData(1, 2)]
    [InlineData(2, 2)]
    public async Task ConditionalTargetGreysOnlyInactiveLines(int target, int activeLine)
    {
        var themes = new ThemeService();
        using var themeScope = TheIdesThemes(themes);
        themes.Apply(target == 1 ? "Dark" : target == 2 ? "Abyss" : "Classic");
        var vm = OpenModule(ModuleKind.StandardModule, "Targets",
            "#If VBA7 Then\nDebug.Print 7\n#ElseIf Win64 Then\nDebug.Print 64\n#Else\nDebug.Print 6\n#End If\n" +
            "#If VBA6 And Win32 Then\nDebug.Print 32\n#Else\nDebug.Print 0\n#End If\n");
        var editor = Show(vm);
        var view = (CodeEditorView)windows.Last().Content!;
        var selector = view.FindControl<ComboBox>("ConditionalTargetCombo");
        selector.Should().NotBeNull();
        selector!.SelectedIndex.Should().Be(0);
        selector.SelectedIndex = target;
        await WaitForConditionalEvaluation(view);
        var grey = ((ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
        grey.Should().Be(((ISolidColorBrush)view.FindResource("ReadOnlyTextBrush")!).Color);
        foreach (var line in new[] { 2, 4, 6 })
        {
            var text = "Debug.Print " + (line == 2 ? "7" : line == 4 ? "64" : "6");
            var actual = editor.Document.Lines.Single(l => editor.Document.GetText(l) == text).LineNumber;
            if (line == activeLine) ColoursOn(editor, actual).Should().Contain(c => c != grey);
            else ColoursOn(editor, actual).Should().OnlyContain(c => c == grey);
        }
        ColoursOn(editor, LineOf(editor, target == 0 ? "Debug.Print 32" : "Debug.Print 0"))
            .Should().OnlyContain(c => c == grey);
        ColoursOn(editor, LineOf(editor, target == 0 ? "Debug.Print 0" : "Debug.Print 32"))
            .Should().Contain(c => c != grey);
        var runtimeConstants = CompilationConstants.ForProject(vm.ModuleDefinition!.Owner);
        runtimeConstants.BuiltIns.Should().BeSameAs(CompilationConstants.Vb6BuiltIns);
        ConditionalCompiler.Preprocess(vm.BufferBody, runtimeConstants)
            .InactiveLines.Should().Equal(new LineRange(2, 2), new LineRange(4, 4), new LineRange(9, 9));
        var another = OpenModule(ModuleKind.StandardModule, "Another", vm.BufferBody);
        Show(another);
        ((CodeEditorView)windows.Last().Content!).FindControl<ComboBox>("ConditionalTargetCombo")!.SelectedIndex.Should().Be(0);
        selector.SelectedIndex.Should().Be(target, "the view belongs to its own window");
        themes.Apply(target == 1 ? "Abyss" : "Dark");
        RunJobsKeepingContext();
        var switchedGrey = ((ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
        switchedGrey.Should().Be(((ISolidColorBrush)view.FindResource("ReadOnlyTextBrush")!).Color);
        ColoursOn(editor, LineOf(editor, target == 0 ? "Debug.Print 32" : "Debug.Print 0"))
            .Should().OnlyContain(c => c == switchedGrey);
    }

    [AvaloniaTheory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public async Task ConditionalWin64DistinguishesVbaTargets(int target, bool active)
    {
        var vm = OpenModule(ModuleKind.StandardModule, "Targets", "#If Win64 Then\nDebug.Print 64\n#Else\nDebug.Print 32\n#End If\n");
        var editor = Show(vm);
        var view = (CodeEditorView)windows.Last().Content!;
        var selector = view.FindControl<ComboBox>("ConditionalTargetCombo");
        selector.Should().NotBeNull();
        selector!.SelectedIndex = target;
        await WaitForConditionalEvaluation(view);
        var grey = ((ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
        ColoursOn(editor, LineOf(editor, active ? "Debug.Print 32" : "Debug.Print 64"))
            .Should().OnlyContain(c => c == grey);
        ColoursOn(editor, LineOf(editor, active ? "Debug.Print 64" : "Debug.Print 32"))
            .Should().Contain(c => c != grey);
    }

    [AvaloniaFact]
    public async Task OpeningACodeWindowRequestsFoldsOnlyOnce()
    {
        Show(OpenModule(ModuleKind.StandardModule, "Targets",
            "#If Win64 Then\nDebug.Print 64\n#End If\n"));

        await Task.Delay(650, TestContext.Current.CancellationToken);
        RunJobsKeepingContext();

        lsp.ReceivedCalls().Count(call => call.GetMethodInfo().Name == nameof(ILspClient.RequestFoldingRangesAsync))
            .Should().Be(1, "opening requests folds immediately; attaching the document must not schedule a second request");
    }

    [AvaloniaTheory]
    [InlineData(3, false)]
    [InlineData(4, true)]
    public async Task TwinBasicTargetsUseMeasuredConstantsWithoutChangingExecution(int target, bool win64)
    {
        var vm = OpenModule(ModuleKind.StandardModule, "Targets",
            "#If CBool(Win32) Then\nDebug.Print 101\n#Else\nDebug.Print 102\n#End If\n" +
            "#If Win64 Then\nDebug.Print 201\n#Else\nDebug.Print 202\n#End If\n" +
            "#If TWINBASIC_BUILD > 0 Then\nDebug.Print 301\n#Else\nDebug.Print 302\n#End If\n");
        var editor = Show(vm);
        var view = (CodeEditorView)windows.Last().Content!;
        var selector = view.FindControl<ComboBox>("ConditionalTargetCombo")!;
        var grey = ((ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
        ColoursOn(editor, LineOf(editor, "Debug.Print 101")).Should().Contain(c => c != grey);
        await WaitForConditionalEvaluation(view);
        lsp.ClearReceivedCalls();
        selector.SelectedIndex = target;
        await WaitForConditionalEvaluation(view);
        selector.SelectedIndex.Should().Be(target);
        ColoursOn(editor, LineOf(editor, "Debug.Print 101")).Should().OnlyContain(c => c == grey);
        ColoursOn(editor, LineOf(editor, "Debug.Print 102")).Should().Contain(c => c != grey);
        ColoursOn(editor, LineOf(editor, win64 ? "Debug.Print 202" : "Debug.Print 201"))
            .Should().OnlyContain(c => c == grey);
        ColoursOn(editor, LineOf(editor, win64 ? "Debug.Print 201" : "Debug.Print 202"))
            .Should().Contain(c => c != grey);
        foreach (var text in new[] { "Debug.Print 301", "Debug.Print 302" })
            ColoursOn(editor, LineOf(editor, text)).Should().Contain(c => c != grey);
        CompilationConstants.ForProject(vm.ModuleDefinition!.Owner).BuiltIns.Should().BeSameAs(CompilationConstants.Vb6BuiltIns);
        lsp.ReceivedCalls().Select(call => call.GetMethodInfo().Name).Should().BeEmpty();
    }

    [AvaloniaTheory]
    [InlineData(VBProjectType.EXE, "Standard EXE")]
    [InlineData(VBProjectType.OleDll, "ActiveX DLL")]
    [InlineData(VBProjectType.Control, "ActiveX Control")]
    [InlineData(VBProjectType.OleExe, null)]
    public async Task TwinBasicBuildTypeUsesOnlyMeasuredProjectNames(VBProjectType type, string? buildType)
    {
        var vm = OpenModule(ModuleKind.StandardModule, "Targets",
            "#If TWINBASIC_BUILD_TYPE = \"Standard EXE\" Then\nDebug.Print 101\n#Else\nDebug.Print 102\n#End If\n");
        vm.ModuleDefinition!.Owner.ProjectType = type;
        if (buildType is not null) vm.ReplaceBody(vm.BufferBody.Replace("Standard EXE", buildType));
        var editor = Show(vm);
        var view = (CodeEditorView)windows.Last().Content!;
        var selector = view.FindControl<ComboBox>("ConditionalTargetCombo")!;
        selector.SelectedIndex = 3;
        selector.SelectedIndex.Should().Be(3);
        await WaitForConditionalEvaluation(view);
        var grey = ((ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
        if (buildType is null)
        {
            ColoursOn(editor, LineOf(editor, "Debug.Print 101")).Should().Contain(c => c != grey);
            ColoursOn(editor, LineOf(editor, "Debug.Print 102")).Should().Contain(c => c != grey);
        }
        else
        {
            ColoursOn(editor, LineOf(editor, "Debug.Print 102"))
                .Should().OnlyContain(c => c == grey);
            ColoursOn(editor, LineOf(editor, "Debug.Print 101"))
                .Should().Contain(c => c != grey);
        }
    }

    [AvaloniaFact]
    public async Task TypingWithinALineKeepsTheGreyingUntilTheNextEvaluation()
    {
        var vm = OpenModule(ModuleKind.StandardModule, "Targets", "#If VBA7 Then\nDebug.Print 7\n#Else\nDebug.Print 6\n#End If\n");
        var editor = Show(vm);
        var view = (CodeEditorView)windows.Last().Content!;
        await WaitForConditionalEvaluation(view);
        var grey = ((ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
        ColoursOn(editor, LineOf(editor, "Debug.Print 7")).Should().OnlyContain(c => c == grey);

        // A keystroke that keeps every line where it was must not flash the greying off while the next evaluation waits.
        var active = editor.Document.GetLineByNumber(LineOf(editor, "Debug.Print 6"));
        editor.Document.Insert(active.EndOffset, "0");
        ColoursOn(editor, LineOf(editor, "Debug.Print 7")).Should().OnlyContain(c => c == grey);

        // One that moves lines does clear it, since the old line numbers would grey the wrong text: here the #If line,
        // which now sits where the inactive branch was.
        editor.Document.Insert(0, "\n");
        // Read the synchronous redraw before pumping can run the debounce timer.
        ColoursOn(editor, LineOf(editor, "#If VBA7 Then")).Should().Contain(c => c != grey);
        ColoursOn(editor, LineOf(editor, "Debug.Print 7")).Should().Contain(c => c != grey);
        await WaitForConditionalEvaluation(view);
        ColoursOn(editor, LineOf(editor, "Debug.Print 7")).Should().OnlyContain(c => c == grey);
    }

    [AvaloniaFact]
    public async Task ConditionalSelectorFollowsTypingAndMalformedBlocksClearGreying()
    {
        var vm = OpenModule(ModuleKind.StandardModule, "Targets", "Debug.Print 6\n");
        var editor = Show(vm);
        var view = (CodeEditorView)windows.Last().Content!;
        var selector = view.FindControl<ComboBox>("ConditionalTargetCombo");
        selector.Should().NotBeNull();
        selector!.IsVisible.Should().BeFalse();
        vm.ReplaceBody("#If VBA7 Then\nDebug.Print 7\n#Else\nDebug.Print 6\n#End If\n");
        await WaitForConditionalEvaluation(view);
        selector.IsVisible.Should().BeTrue();
        var grey = ((ISolidColorBrush)view.FindResource("InactiveCodeTextBrush")!).Color;
        ColoursOn(editor, LineOf(editor, "Debug.Print 7")).Should().OnlyContain(c => c == grey);
        vm.ReplaceBody("#If VBA7 Then\nDebug.Print 7\n");
        await WaitForConditionalEvaluation(view);
        ColoursOn(editor, LineOf(editor, "Debug.Print 7")).Should().Contain(c => c != grey);
        vm.ReplaceBody("Debug.Print 6\n");
        await WaitForConditionalEvaluation(view);
        selector.IsVisible.Should().BeFalse();
    }

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
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\nPublic Sub Go()\r\nAttribute Go.VB_Description = \"Goes\"\r\n    Debug.Print 1\r\nEnd Sub\r\n");
        ExpandRuns(vm, "Go");
        var editor = Show(vm);

        var attribute = LineOf(editor, "Attribute Go.VB_Description");
        ColoursOn(editor, attribute).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey);
        ColoursOn(editor, attribute - 1).Should().NotContain(ClassicGrey, "the declaration is code");
        ColoursOn(editor, attribute + 1).Should().NotContain(ClassicGrey, "so is the body");
    }

    [AvaloniaFact]
    public void AFoldedRunIsDrawnAsABoxInTheReadOnlyColourAndAnExpandedOneIsGreyedLineByLine()
    {
        // #273 task 4.2's greying half, once 4.3 folds the runs. Folded, all that is drawn of a run is the library's box
        // after the declaration, whose text and border take the fold-label brush the window sets from the theme's
        // read-only colour; expanded, every line of it is greyed as before, and the declaration is code either way.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\nPublic Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "Attribute Go.VB_UserMemId = 0\r\n" +
            "    Debug.Print 1\r\nEnd Sub\r\n");
        var editor = Show(vm);
        var declaration = LineOf(editor, "Public Sub Go()");
        var folding = (FoldingManager)editor.TextArea.TextView.GetService(typeof(FoldingManager))!;
        var run = folding.GetFoldingsAt(editor.Document.GetLineByNumber(declaration).EndOffset).Single();
        run.IsFolded.Should().BeTrue("the premise: the run opens folded");

        var row = editor.TextArea.TextView.GetOrConstructVisualLine(editor.Document.GetLineByNumber(declaration));
        row.LastDocumentLine.LineNumber.Should().Be(declaration + 2, "folded, the run is drawn on the declaration's row");
        row.Elements.Last().GetType().Name.Should().Be("FoldingLineElement", "as the library's box, at the row's end");
        FoldLabelColour.Should().Be(ClassicGrey, "which is drawn in the theme's read-only colour");
        ColoursOn(editor, declaration).SkipLast(1).Should().NotBeEmpty().And.NotContain(ClassicGrey,
            "and the declaration before it is code");

        run.IsFolded = false;
        RunJobsKeepingContext();
        ColoursOn(editor, declaration).Should().NotContain(ClassicGrey, "expanded, the declaration is code");
        ColoursOn(editor, declaration + 1).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "and each run line grey");
        ColoursOn(editor, declaration + 2).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey);
        ColoursOn(editor, declaration + 3).Should().NotContain(ClassicGrey, "and the body code again");
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
        ExpandRuns(vm, "Halt");
        var editor = Show(vm);
        editor.TextArea.Focus();
        RunJobsKeepingContext();

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
        ExpandRuns(vm, "Halt", "Alarm");
        var editor = Show(vm);
        editor.TextArea.Focus();
        RunJobsKeepingContext();
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
        RunJobsKeepingContext();
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
        RunJobsKeepingContext();
        ColoursOn(editor, alarm).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "named again, it is the variable's");
    }

    // The redraw compares where each region is, not only how many there are (review of 4.1). Each test below changes
    // the regions without changing their count, so a redraw that compared counts alone would leave its lines drawn
    // as they were; and each edits only lines AvaloniaEdit rebuilds by itself, away from the lines asserted on.

    [AvaloniaFact]
    public void OneEditGroupThatTurnsOneRunIntoAnOrphanAndAnotherOutOfOneRedrawsBoth()
    {
        // A Replace All or a multi-edit undo step: one change event, the same number of regions before and after.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\n" +
            "Public Sub Halt()\r\n" +
            "Attribute Halt.VB_Description = \"Stops\"\r\n" +
            "End Sub\r\n" +
            "'Public Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "End Sub\r\n");
        ExpandRuns(vm, "Halt", "Go");
        var editor = Show(vm);
        var halt = LineOf(editor, "Attribute Halt");
        var go = LineOf(editor, "Attribute Go");
        ColoursOn(editor, halt).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "the premise: Halt's run is greyed");
        ColoursOn(editor, go).Should().NotBeEmpty().And.NotContain(ClassicGrey, "and Go's, under a comment, is not");
        var count = vm.ReadOnlyRegionsNow.Count;

        editor.Document.BeginUpdate();
        editor.Document.Remove(editor.Document.GetLineByNumber(go - 1).Offset, 1);
        editor.Document.Insert(editor.Document.GetLineByNumber(halt - 1).Offset, "'");
        editor.Document.EndUpdate();
        RunJobsKeepingContext();

        vm.ReadOnlyRegionsNow.Should().HaveCount(count, "the premise: as many regions as before, in another place");
        ColoursOn(editor, halt).Should().NotBeEmpty().And.NotContain(ClassicGrey, "Halt's run is an orphan now");
        ColoursOn(editor, go).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "and Go's describes Go");
    }

    [AvaloniaFact]
    public void ARunThatLosesItsFirstLineToTheDeclarationAboveIsRedrawn()
    {
        // A continuation typed at the end of the declaration takes the run's first line into the declaration's
        // logical line, and the rest of the run hangs from that. The region starts later and ends where it did.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\n" +
            "Public Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "Attribute Go.VB_HelpID = 7\r\n" +
            "End Sub\r\n");
        ExpandRuns(vm, "Go");
        var editor = Show(vm);
        var first = LineOf(editor, "Attribute Go.VB_Description");
        var second = LineOf(editor, "Attribute Go.VB_HelpID");
        ColoursOn(editor, first).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "the premise: the run is greyed");
        var count = vm.ReadOnlyRegionsNow.Count;

        editor.Document.Insert(editor.Document.GetLineByNumber(first - 1).EndOffset, " _");
        RunJobsKeepingContext();

        vm.ReadOnlyRegionsNow.Should().HaveCount(count, "the premise: the run is still a region, one line shorter");
        ColoursOn(editor, first).Should().NotBeEmpty().And.NotContain(ClassicGrey, "its first line is the declaration's now");
        ColoursOn(editor, second).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "and the rest is still the run");
    }

    [AvaloniaFact]
    public void ARunThatTakesInTheOrphanBelowItIsRedrawn()
    {
        // Deleting the statement between a run and an orphan joins them, and the procedure's run takes in the orphan's
        // lines. The region starts where it did and ends later.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\n" +
            "Public Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "    Debug.Print 1\r\n" +
            "Attribute Go.VB_HelpID = 7\r\n" +
            "Attribute Go.VB_UserMemId = 0\r\n" +
            "End Sub\r\n");
        ExpandRuns(vm, "Go");
        var editor = Show(vm);
        ColoursOn(editor, LineOf(editor, "Attribute Go.VB_UserMemId")).Should().NotBeEmpty().And.NotContain(ClassicGrey,
            "the premise: the orphan is drawn as code");
        var count = vm.ReadOnlyRegionsNow.Count;

        var statement = editor.Document.GetLineByNumber(LineOf(editor, "Debug.Print"));
        editor.Document.Remove(statement.Offset, statement.TotalLength);
        RunJobsKeepingContext();

        vm.ReadOnlyRegionsNow.Should().HaveCount(count, "the premise: one region, grown");
        // The orphan's first line is next to the deletion, and AvaloniaEdit rebuilds it by itself; its last is not.
        ColoursOn(editor, LineOf(editor, "Attribute Go.VB_HelpID")).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey,
            "the orphan is the procedure's run now");
        ColoursOn(editor, LineOf(editor, "Attribute Go.VB_UserMemId")).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey,
            "all of it");
    }

    [AvaloniaFact]
    public void AnEditFarFromARunThatTurnsItIntoAnOrphanRedrawsIt()
    {
        // The renamed kind reads every name the module declares, so a run under a line declaring one variable becomes an
        // orphan when the name it carries is declared anywhere, here in a procedure four lines below it.
        var themes = new ThemeService();
        using var _ = TheIdesThemes(themes);
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\n" +
            "Public Tally As Long\r\n" +
            "Attribute Count.VB_VarDescription = \"Left by a rename\"\r\n" +
            "\r\n" +
            "Public Sub Other()\r\n" +
            "    Debug.Print 1\r\n" +
            "End Sub\r\n");
        ExpandRuns(vm, "Tally");
        var editor = Show(vm);
        var run = LineOf(editor, "Attribute Count");
        ColoursOn(editor, run).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "the premise: the run is Tally's, renamed");

        editor.Document.Insert(editor.Document.GetLineByNumber(LineOf(editor, "Debug.Print")).Offset, "    Dim Count As Long\r\n");
        RunJobsKeepingContext();
        ColoursOn(editor, run).Should().NotBeEmpty().And.NotContain(ClassicGrey, "Count is declared now, so the run is no rename's");

        editor.Document.UndoStack.Undo();
        RunJobsKeepingContext();
        ColoursOn(editor, run).Should().NotBeEmpty().And.AllBeEquivalentTo(ClassicGrey, "undone, it is Tally's again");
    }

    /// <summary>Typed text as the input system delivers it: a TextInput event on the focused text area.</summary>
    private static void TypeText(TextEditor editor, string text)
    {
        editor.TextArea.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text });
        RunJobsKeepingContext();
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
        RunJobsKeepingContext();

        var colours = ColoursOn(editor, 3);
        colours.Should().Contain(Colors.Red, "the error's text is red");
        colours.Should().Contain(ClassicGrey, "and the rest of the line is still grey");
    }

    [AvaloniaFact]
    public void WithNoReadOnlyColourInTheThemeNothingIsGreyed()
    {
        // No literal stands in for a theme that does not resolve the key: here, no theme at all is merged, so the
        // header keeps its syntax colours rather than taking a grey no theme could reach.
        using var _ = NoTheme();
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
        var vm = OpenModule(ModuleKind.ClassModule, "Order",
            "Option Explicit\r\n\r\nPublic Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"See https://example.com/orders or ask help@example.com\"\r\n" +
            "    ' https://example.com/orders\r\nEnd Sub\r\n");
        ExpandRuns(vm, "Go");
        var editor = Show(vm);

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
        RunJobsKeepingContext();

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
        RunJobsKeepingContext();
        var editor = Show(OpenForm());
        ExpandHeader(editor);
        ColoursOn(editor, 2).Should().AllBeEquivalentTo(DarkGrey);
        var variant = Application.Current!.ActualThemeVariant;

        themes.Apply("Abyss");
        RunJobsKeepingContext();

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
        RunJobsKeepingContext();
        return new Restore(() =>
        {
            Application.Current!.Resources.MergedDictionaries.Remove(overriding);
            RunJobsKeepingContext();
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
            var vm = new RelatedDocumentEditorViewModel(lsp, Substitute.For<IWindowManager>(), Substitute.For<ILocalizationService>())
                .Initialize(new RelatedDocumentDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "notes.md", path));
            var view = new RelatedDocumentEditorView { DataContext = vm };
            var window = new Window { Content = view, Width = 900, Height = 700 };
            windows.Add(window);
            window.Show();
            RunJobsKeepingContext();
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
        RunJobsKeepingContext();
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
