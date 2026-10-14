using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using AvaloniaEdit.Document;
using AvaloniaEdit.Folding;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// What AvaloniaEdit 12.0.0 does with a fold that starts part-way along a line, measured on a bare editor before
/// the code window's member-attribute folds relied on any of it (hexide-io/HexIDE#273 task 4.3).
/// </summary>
/// <remarks>
/// <para>
/// A member's run is folded from the end of the line it describes, which no fold the code window made before did:
/// the header's and every server's start at a line's start. So where the caret goes at such a fold, which fold the
/// margin shows a marker for, and how the folding manager pairs and moves one were all read from the library's
/// decompiled source first and then measured here. Each test pins one thing the window's code depends on, and says
/// which.
/// </para>
/// <para>
/// Nothing of HexIDE's is installed: an editor, a document and a folding manager, as the library ships them.
/// </para>
/// </remarks>
public class MidLineFoldBehaviourTests : IDisposable
{
    private readonly List<Window> windows = [];

    public void Dispose()
    {
        foreach (var w in windows) w.Close();
        GC.SuppressFinalize(this);
    }

    // A procedure with a two-line run under its declaration, as VB6 writes one.
    private const string Text =
        "Option Explicit\r\n" +                              // 1
        "\r\n" +                                             // 2
        "Public Sub Go()\r\n" +                              // 3  the described line
        "Attribute Go.VB_Description = \"Goes\"\r\n" +       // 4  the run
        "Attribute Go.VB_UserMemId = 0\r\n" +                // 5
        "    Debug.Print 1 ' a comment that runs well past the box on any font\r\n" + // 6
        "End Sub\r\n";                                       // 7

    private sealed record Rig(Window Window, TextEditor Editor, FoldingManager Folding)
    {
        public TextDocument Document => Editor.Document;
        public int LineStart(int line) => Document.GetLineByNumber(line).Offset;
        public int LineEnd(int line) => Document.GetLineByNumber(line).EndOffset;

        /// <summary>The end of the described line's text: where the run's fold starts.</summary>
        public int Anchor => LineEnd(3);

        /// <summary>The end of the run's last line's text: where the run's fold ends.</summary>
        public int RunEnd => LineEnd(5);
    }

    private Rig Show()
    {
        var window = new Window { Width = 1200, Height = 800 };
        windows.Add(window);
        var editor = new TextEditor { Document = new TextDocument(Text), ShowLineNumbers = true };
        window.Content = editor;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        editor.TextArea.Focus();
        var folding = FoldingManager.Install(editor.TextArea);
        Dispatcher.UIThread.RunJobs();
        return new Rig(window, editor, folding);
    }

    /// <summary>The run's fold, folded, and optionally the procedure's fold a server would send.</summary>
    private static FoldingSection FoldTheRun(Rig rig, bool withProcedureFold = false)
    {
        var folds = new List<NewFolding>();
        if (withProcedureFold)
            folds.Add(new NewFolding(rig.LineStart(3), rig.LineEnd(7)));
        folds.Add(new NewFolding(rig.Anchor, rig.RunEnd));
        rig.Folding.UpdateFoldings(folds, -1);
        var run = rig.Folding.GetFoldingsAt(rig.Anchor).Single();
        run.IsFolded = true;
        rig.Editor.TextArea.TextView.EnsureVisualLines();
        Dispatcher.UIThread.RunJobs();
        return run;
    }

    private static void Press(Rig rig, Key key, KeyModifiers modifiers = KeyModifiers.None)
    {
        rig.Editor.TextArea.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent, Key = key, KeyModifiers = modifiers,
        });
        Dispatcher.UIThread.RunJobs();
    }

    private static int Caret(Rig rig) => rig.Editor.TextArea.Caret.Offset;

    private static void PutCaret(Rig rig, int offset)
    {
        rig.Editor.TextArea.Caret.Offset = offset;
        Dispatcher.UIThread.RunJobs();
    }

    // ── What a folded run looks like ────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFoldedRunJoinsTheLineItDescribesAsOneRowAndTheLineAfterItKeepsItsOwn()
    {
        var rig = Show();
        FoldTheRun(rig);

        rig.Editor.TextArea.TextView.VisualLines
            .Select(v => (v.FirstDocumentLine.LineNumber, v.LastDocumentLine.LineNumber))
            .Should().StartWith([(1, 1), (2, 2), (3, 5), (6, 6)],
                "the run's lines are drawn inside the declaration's row, and the body starts the next one");
    }

    [AvaloniaFact]
    public void AFoldWithNoTitleIsDrawnAsTheLibrarysOwnBox()
    {
        // The window gives a run's fold no title, so there is nothing to translate: the library draws "...".
        var rig = Show();
        FoldTheRun(rig).Title.Should().BeNull();

        var row = rig.Editor.TextArea.TextView.GetOrConstructVisualLine(rig.Document.GetLineByNumber(3));
        var box = row.Elements.Last();
        box.GetType().Name.Should().Be("FoldingLineElement");
        box.DocumentLength.Should().Be(rig.RunEnd - rig.Anchor, "the box stands for the whole run");
        box.VisualLength.Should().Be(1, "and is one caret stop wide");
    }

    // ── (a) The margin: one marker per row ─────────────────────────────────────────────────────────────────

    /// <summary>The fold each of the margin's markers toggles, by the row it is drawn on.</summary>
    private static List<(int Line, FoldingSection Section)> Markers(Rig rig)
    {
        var margin = rig.Editor.TextArea.LeftMargins.OfType<FoldingMargin>().Single();
        margin.InvalidateMeasure();
        Dispatcher.UIThread.RunJobs();
        var markers = new List<(int, FoldingSection)>();
        foreach (var child in margin.GetVisualChildren())
        {
            var type = child.GetType();
            var section = (FoldingSection)type.GetField("FoldingSection", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(child)!;
            var line = (AvaloniaEdit.Rendering.VisualLine)type.GetField("VisualLine", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(child)!;
            markers.Add((line.FirstDocumentLine.LineNumber, section));
        }
        return markers;
    }

    [AvaloniaFact]
    public void WithAProceduresFoldOnTheSameRowTheMarginShowsOnlyTheProcedures()
    {
        // Read in FoldingMargin.OnTextViewVisualLinesChanged: one marker per row, for the first fold starting at or
        // after the row's start, and the procedure's starts at the start of the row. So a run expanded beside a
        // server's procedure fold has no marker to fold it again with; design.md records that as an issue to file.
        var rig = Show();
        var run = FoldTheRun(rig, withProcedureFold: true);
        var procedure = rig.Folding.GetFoldingsAt(rig.LineStart(3)).Single();

        var onTheRow = Markers(rig).Where(m => m.Line == 3).ToList();
        onTheRow.Should().ContainSingle();
        onTheRow[0].Section.Should().BeSameAs(procedure);

        run.IsFolded = false;
        Dispatcher.UIThread.RunJobs();
        Markers(rig).Where(m => m.Line == 3).Select(m => m.Section).Should().Equal([procedure],
            "expanded, the run still has no marker of its own");
        Markers(rig).Select(m => m.Section).Should().NotContain(run);
    }

    [AvaloniaFact]
    public void WithNoOtherFoldOnItsRowTheRunsFoldHasTheMarker()
    {
        // No server, or a variable's run, which no server folds: the run's own marker folds and unfolds it.
        var rig = Show();
        var run = FoldTheRun(rig);

        Markers(rig).Where(m => m.Line == 3).Select(m => m.Section).Should().Equal([run]);
    }

    // ── (b) Where the caret goes at a collapsed fold that starts mid-line ──────────────────────────────────

    [AvaloniaFact]
    public void TheRowHasTwoCaretStopsAtTheBoxBeforeItAtTheAnchorAndAfterItInsideTheRun()
    {
        // Right from the anchor steps over the box, to the fold's end: the end of the run's last line, which is
        // inside the run. The window's caret rule moves a caret landing there; this is where it lands first.
        var rig = Show();
        FoldTheRun(rig);
        PutCaret(rig, rig.Anchor);

        Press(rig, Key.Right);
        Caret(rig).Should().Be(rig.RunEnd);
        rig.Editor.TextArea.Caret.Position.IsAtEndOfLine.Should().BeFalse("a step, not the end of the row");

        Press(rig, Key.Right);
        Caret(rig).Should().Be(rig.LineStart(6), "the next step is the line after the run");
    }

    [AvaloniaFact]
    public void EndPutsTheCaretAfterTheBoxAndSaysItIsAtTheEndOfTheRow()
    {
        // From the start of the row and from the anchor alike, so End from the anchor lands where Right from it does.
        // The caret rule has to send the two different ways, and tells them apart by the key that was pressed:
        // IsAtEndOfLine looked like the tell here, but a click lands without it (the click's test, below).
        var rig = Show();
        FoldTheRun(rig);

        foreach (var from in new[] { rig.LineStart(3), rig.LineStart(3) + 4, rig.Anchor })
        {
            PutCaret(rig, from);
            Press(rig, Key.End);
            Caret(rig).Should().Be(rig.RunEnd, $"End from {from}");
            rig.Editor.TextArea.Caret.Position.IsAtEndOfLine.Should().BeTrue($"End from {from}");
        }
    }

    [AvaloniaFact]
    public void LeftFromTheLineAfterTheRunLandsAfterTheBoxAndThenAtTheAnchor()
    {
        var rig = Show();
        FoldTheRun(rig);
        PutCaret(rig, rig.LineStart(6));

        Press(rig, Key.Left);
        Caret(rig).Should().Be(rig.RunEnd);
        rig.Editor.TextArea.Caret.Position.IsAtEndOfLine.Should().BeFalse();

        Press(rig, Key.Left);
        Caret(rig).Should().Be(rig.Anchor);
    }

    [AvaloniaFact]
    public void HomeFromAfterTheBoxGoesToTheStartOfTheDescribedLine()
    {
        var rig = Show();
        FoldTheRun(rig);
        PutCaret(rig, rig.RunEnd);

        Press(rig, Key.Home);
        Caret(rig).Should().Be(rig.LineStart(3));
    }

    [AvaloniaFact]
    public void UpAndDownOntoTheRowLandAfterTheBoxFromAColumnPastIt()
    {
        // Line 6 is far longer than "Public Sub Go()" and its box, so Up from its end lands at the row's last stop,
        // after the box; Down from a short line's start lands at the row's start. It used to end at "Debug.Print 1",
        // which on Linux's and macOS's fonts drew short of the box's far side, so Up landed before the box (CI, #273).
        var rig = Show();
        FoldTheRun(rig);

        PutCaret(rig, rig.LineEnd(6));
        Press(rig, Key.Up);
        Caret(rig).Should().Be(rig.RunEnd, "Up from past the box's column");

        PutCaret(rig, rig.LineStart(2));
        Press(rig, Key.Down);
        Caret(rig).Should().Be(rig.LineStart(3), "Down from column 1");

        PutCaret(rig, rig.LineStart(3));
        Press(rig, Key.Down);
        Caret(rig).Should().Be(rig.LineStart(6), "Down from the row goes to the line after the run");
    }

    [AvaloniaFact]
    public void ControlRightAndControlLeftBothStopAfterTheBox()
    {
        // The first guess, read from VisualLineElement's word-start rule, was that a word step skips the box both
        // ways. It skips it neither way: the stop after the box is a word start, so Ctrl+Right from the anchor lands
        // inside the run as Right does, and Ctrl+Left from the line after the run lands there as Left does. The caret
        // rule therefore treats Ctrl+Right as a step, and Ctrl+Left needs nothing of its own.
        var rig = Show();
        FoldTheRun(rig);

        PutCaret(rig, rig.Anchor);
        Press(rig, Key.Right, KeyModifiers.Control);
        Caret(rig).Should().Be(rig.RunEnd);
        rig.Editor.TextArea.Caret.Position.IsAtEndOfLine.Should().BeFalse();

        Press(rig, Key.Right, KeyModifiers.Control);
        Caret(rig).Should().Be(rig.LineStart(6) + 4, "the first word of the line after the run");

        PutCaret(rig, rig.LineStart(6));
        Press(rig, Key.Left, KeyModifiers.Control);
        Caret(rig).Should().Be(rig.RunEnd, "a word step back stops after the box too");
        Press(rig, Key.Left, KeyModifiers.Control);
        Caret(rig).Should().Be(rig.Anchor);
    }

    [AvaloniaFact]
    public void AClickPastTheEndOfTheRowPutsTheCaretAfterTheBoxAsAStepDoes()
    {
        // Where End does, but without End's IsAtEndOfLine: a click from the anchor lands exactly as Right from it
        // does. So the caret rule cannot read where a caret came from off the position alone, and asks which key
        // moved it; a click is taken to the anchor whether or not the caret was there already.
        var rig = Show();
        FoldTheRun(rig);
        PutCaret(rig, rig.Anchor);
        var textView = rig.Editor.TextArea.TextView;
        var row = textView.GetOrConstructVisualLine(rig.Document.GetLineByNumber(3));
        var at = textView.TranslatePoint(
            new Point(textView.Bounds.Width - 20, row.VisualTop - textView.VerticalOffset + row.Height / 2), rig.Window)!.Value;
        var pointer = new Avalonia.Input.Pointer(Avalonia.Input.Pointer.GetNextFreeId(), PointerType.Mouse, isPrimary: true);

        rig.Editor.TextArea.RaiseEvent(new PointerPressedEventArgs(
            rig.Editor.TextArea, pointer, rig.Window, at, timestamp: 0,
            new PointerPointProperties(RawInputModifiers.LeftMouseButton, PointerUpdateKind.LeftButtonPressed),
            KeyModifiers.None));
        rig.Editor.TextArea.RaiseEvent(new PointerReleasedEventArgs(
            rig.Editor.TextArea, pointer, rig.Window, at, timestamp: 1,
            new PointerPointProperties(RawInputModifiers.None, PointerUpdateKind.LeftButtonReleased),
            KeyModifiers.None, MouseButton.Left));
        Dispatcher.UIThread.RunJobs();

        Caret(rig).Should().Be(rig.RunEnd);
        rig.Editor.TextArea.Caret.Position.IsAtEndOfLine.Should().BeFalse("unlike End");
    }

    [AvaloniaFact]
    public void ShiftRightReadsTheCaretBackAfterMovingItSoASelectionEndsWhereTheCaretWasPutDuringTheMove()
    {
        // The window's caret rule moves the caret from inside the caret's own PositionChanged, which the library
        // raises synchronously while it moves the caret, and the selection is made after that from the caret as
        // it then is. So a Shift+Right the rule moves on selects up to where the rule put it.
        var rig = Show();
        FoldTheRun(rig);
        PutCaret(rig, rig.Anchor);
        var moved = false;
        rig.Editor.TextArea.Caret.PositionChanged += (_, _) =>
        {
            if (moved || rig.Editor.TextArea.Caret.Offset != rig.RunEnd) return;
            moved = true;
            rig.Editor.TextArea.Caret.Offset = rig.LineStart(6);
        };

        Press(rig, Key.Right, KeyModifiers.Shift);

        Caret(rig).Should().Be(rig.LineStart(6));
        var selection = rig.Editor.TextArea.Selection.SurroundingSegment!;
        (selection.Offset, selection.EndOffset).Should().Be((rig.Anchor, rig.LineStart(6)));
    }

    // ── (c) What counts as inside the fold ─────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AFoldContainsItsStartAndItsEndForTheManagersLookup()
    {
        var rig = Show();
        var run = FoldTheRun(rig);

        rig.Folding.GetFoldingsContaining(rig.Anchor).Should().Contain(run);
        rig.Folding.GetFoldingsContaining(rig.RunEnd).Should().Contain(run);
        rig.Folding.GetFoldingsContaining(rig.Anchor - 1).Should().NotContain(run);
        rig.Folding.GetFoldingsContaining(rig.RunEnd + 1).Should().NotContain(run);
    }

    [AvaloniaFact]
    public void TheCaretAtEitherEndLeavesTheFoldFoldedAndStrictlyInsideUnfoldsIt()
    {
        // FoldingManagerInstallation unfolds a fold the caret moves strictly inside, so neither of the row's caret
        // stops opens it, and the caret rule's moves do not either.
        var rig = Show();
        var run = FoldTheRun(rig);

        PutCaret(rig, rig.Anchor);
        run.IsFolded.Should().BeTrue("at its start");
        PutCaret(rig, rig.RunEnd);
        run.IsFolded.Should().BeTrue("at its end");
        PutCaret(rig, rig.LineStart(4) + 3);
        run.IsFolded.Should().BeFalse("strictly inside");
    }

    [AvaloniaFact]
    public void TextTypedAtTheAnchorGoesBeforeTheFoldAndTheFoldStaysFolded()
    {
        // Text inserted at a fold's start moves the fold along with it, so typing at the end of the described line
        // lands before the box and the fold still starts at the line's new end.
        var rig = Show();
        var run = FoldTheRun(rig);

        rig.Document.Insert(rig.Anchor, " ' Starts");
        Dispatcher.UIThread.RunJobs();

        rig.Document.GetText(rig.Document.GetLineByNumber(3)).Should().Be("Public Sub Go() ' Starts");
        run.StartOffset.Should().Be(rig.LineEnd(3));
        run.EndOffset.Should().Be(rig.LineEnd(5));
        run.IsFolded.Should().BeTrue();
    }

    [AvaloniaFact]
    public void AnUpdateWithTheSameStartKeepsTheSectionItsStateAndItsTagWhateverItsEnd()
    {
        // The manager pairs the folds it is given with the sections it holds by start offset alone (3.14's
        // measurement, again here for a fold starting mid-line): a section paired is the same object, its state
        // and tag kept, its length taken from the new fold. The window relies on this to keep a run's live state.
        var rig = Show();
        var run = FoldTheRun(rig);
        var tag = new object();
        run.Tag = tag;

        rig.Folding.UpdateFoldings([new NewFolding(rig.Anchor, rig.LineEnd(4))], -1);

        var after = rig.Folding.GetFoldingsAt(rig.Anchor).Single();
        after.Should().BeSameAs(run);
        after.Tag.Should().BeSameAs(tag);
        after.IsFolded.Should().BeTrue();
        after.EndOffset.Should().Be(rig.LineEnd(4));
    }

    [AvaloniaFact]
    public void AnUpdateWithoutASectionsStartRemovesItAndUnfoldsIt()
    {
        // So a run's fold left out of an application is gone, unfolded, and a fold made at a new start is new and
        // expanded: the window sets the state of each one it makes.
        var rig = Show();
        var run = FoldTheRun(rig);

        rig.Folding.UpdateFoldings([new NewFolding(rig.LineEnd(4), rig.RunEnd)], -1);

        run.IsFolded.Should().BeFalse("removed sections are unfolded");
        rig.Folding.AllFoldings.Should().ContainSingle().Which.IsFolded.Should().BeFalse("and a new one starts expanded");
    }
}
