using System;
using System.IO;
using Avalonia.Headless.XUnit;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.IDE;

/// <summary>
/// The header the code window shows is kept in step with the document, and the document's marks are moved
/// when it changes height (hexide-io/HexIDE#273 tasks 3.3, 3.3a and 3.3b).
/// </summary>
/// <remarks>
/// <b>Most of this deliberately runs with nothing open.</b> Breakpoints and bookmarks are bare integers held
/// per document rather than anchors in a buffer, so the case that matters — and the one 3.3b exists for — is
/// a header that changes on a document with no code window at all. A refresher tested only through an open
/// editor would pass while leaving every closed document's marks pointing at the wrong statements.
/// </remarks>
public class HeaderRefresherTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private readonly EventBus bus = new();
    private readonly BreakpointService breakpoints = new();
    private readonly BookmarkService bookmarks = new();
    private readonly IDocumentDockService dock = Substitute.For<IDocumentDockService>();

    private HeaderRefresher CreateSut()
    {
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel>());
        return new HeaderRefresher(bus, dock, breakpoints, bookmarks);
    }

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
        Option Explicit
        """;

    private static FormDefinition Deserialize(string source)
    {
        var form = new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;
        form.AbsolutePath = Path.Combine(Path.GetTempPath(), "hexide-header-refresher", "Form1.frm");
        return form;
    }

    private static ComponentInstance AButton(string name) =>
        new(CommandButtonComponentClass.Instance, name);

    // -- The arithmetic ----------------------------------------------------------------------------

    [Fact]
    public void MarksBelowTheHeaderMoveByTheDifference()
    {
        var form = Deserialize(OneLabel);
        var document = DocumentIdentity.For(form);
        // Line 12 of the buffer is Option Explicit: the header is 10 lines and the code section opens with
        // its Attribute VB_Name.
        breakpoints.SetDocument(document, [12]);
        bookmarks.SetBookmarks(document, [11]);

        CreateSut().ShiftMarks(document, Header(10), Header(13));

        breakpoints.GetBreakpoints(document).Should().Equal(15);
        bookmarks.GetBookmarks(document).Should().Equal(14);
    }

    [Fact]
    public void AShorterHeaderMovesThemBack()
    {
        var form = Deserialize(OneLabel);
        var document = DocumentIdentity.For(form);
        breakpoints.SetDocument(document, [12, 20]);

        CreateSut().ShiftMarks(document, Header(10), Header(8));

        breakpoints.GetBreakpoints(document).Should().Equal(10, 18);
    }

    [Fact]
    public void AMarkInsideTheHeaderStaysWhereItIs()
    {
        // The header is being replaced by another header, so a mark on its line 3 is still on its line 3.
        // Nothing sets a mark there since #273 task 3.12, but one can come to sit there (debugger-vb6-divergences
        // D24), and this is the honest answer rather than a clamp that would pile every header mark onto one line.
        var form = Deserialize(OneLabel);
        var document = DocumentIdentity.For(form);
        breakpoints.SetDocument(document, [3, 10, 11]);

        CreateSut().ShiftMarks(document, Header(10), Header(14));

        breakpoints.GetBreakpoints(document).Should().Equal(3, 10, 15);
    }

    [Fact]
    public void TheTwoStoresDisagreeAboutTheBaseAndTheShiftRespectsThat()
    {
        // Breakpoints are 1-based and bookmarks 0-based, both stated to callers on the shipped automation
        // tools and both written raw into the same .user.hexproj. The same source line is therefore a
        // different integer in each store, and a shift that used one rule for both would move one of them
        // off by one -- onto the wrong statement, which for a breakpoint means it never fires.
        var form = Deserialize(OneLabel);
        var document = DocumentIdentity.For(form);
        // One mark on the header's LAST line and one on the code's FIRST, in each store, because only a
        // mark at the boundary can tell the two rules apart -- and a test without one passes whichever
        // rule the code uses.
        breakpoints.SetDocument(document, [10, 11]);   // 1-based: last header line, first code line
        bookmarks.SetBookmarks(document, [9, 10]);     // 0-based: the same two lines

        CreateSut().ShiftMarks(document, Header(10), Header(12));

        breakpoints.GetBreakpoints(document).Should().Equal(10, 13);
        bookmarks.GetBookmarks(document).Should().Equal(9, 12);
    }

    [Fact]
    public void AHeaderOfTheSameHeightMovesNothingAndSaysNothing()
    {
        // Not merely "the numbers are unchanged": both stores raise a change event from every mutator, and
        // the gutters repaint, the sidecar saves and a live run is re-pushed off those events. A no-op that
        // still announced would do all three on every nudge of a control.
        var form = Deserialize(OneLabel);
        var document = DocumentIdentity.For(form);
        breakpoints.SetDocument(document, [12]);
        var announced = 0;
        breakpoints.BreakpointsChanged += _ => announced++;

        CreateSut().ShiftMarks(document, Header(10), "x" + Header(10));

        breakpoints.GetBreakpoints(document).Should().Equal(12);
        announced.Should().Be(0);
    }

    [Fact]
    public void LineEndingsDoNotDecideTheCount()
    {
        // Counted by '\n', never by Environment.NewLine. A .frm is CRLF wherever it came from, but the
        // buffer is also composed from text HexIDE produced, and build-ide runs on ubuntu-latest -- a count
        // that asked the host what a line ending is would move every mark on one machine and not the other.
        var form = Deserialize(OneLabel);
        var document = DocumentIdentity.For(form);
        breakpoints.SetDocument(document, [12]);

        CreateSut().ShiftMarks(document, "a\nb\n", "a\r\nb\r\nc\r\n");

        breakpoints.GetBreakpoints(document).Should().Equal(13);
    }

    private static string Header(int lines) => string.Concat(Enumerable.Repeat("x\r\n", lines));

    // -- The gates ---------------------------------------------------------------------------------

    [Fact]
    public void AFormThatCannotBeSavedFaithfullyIsNeverReRendered()
    {
        // FormSerializer carries no fidelity check of its own -- SerializeFormToFile refuses BEFORE calling
        // it -- so without this gate a refresh would put a flattened menu hierarchy in the code window as
        // though it were the file, for exactly the forms whose save is refused to stop that reaching disk.
        var form = Deserialize(OneLabel);
        var asRead = form.DesignerText;
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        form.UpdateComponents([.. form.Components, AButton("Command1")]);

        CreateSut().LayoutChanged(form);

        form.DesignerText.Should().Be(asRead, "the buffer keeps showing the file the save refusal protects");
    }

    [Fact]
    public void AFormWithNoFileRendersTheHeaderItsFirstSaveWillWrite()
    {
        // Task 3.4. The fixture is a form built the way IProjectTemplate builds Form1 -- constructed, never
        // parsed -- because that is the production state: it has no file AND no designer text. Setting
        // AbsolutePath = null on a form read from disk, which this test used to do, is a state nothing
        // produces and would have tested a form that already had a header.
        var form = new FormDefinition(Project, FormComponentClass.Instance, "Form1");

        form.DesignerText.Should().BeNull("a created form has nothing recorded until something commits");

        CreateSut().LayoutChanged(form);

        form.DesignerText.Should().NotBeNull();
        form.DesignerText.Should().StartWith("VERSION 5.00").And.EndWith("End\r\n");
        FormCodeText.WholeFile(form).Should().Be(form.DesignerText + form.Code);
    }

    [Fact]
    public void AndItsCompanionCitationNamesTheFileThatSaveWillCreate()
    {
        // The only way the file name reaches the rendered text at all. FormSerializer reads it solely to
        // derive the companion name, and writes that name solely for a property holding a blob -- so
        // without one here the test could not tell <Name>.frx from any other string, and the rule would be
        // asserted by a render that does not contain it.
        var form = new FormDefinition(Project, FormComponentClass.Instance, "Splash");
        form.Components[0].SetProperty(VBProperties.IconProperty, new byte[] { 1, 2, 3, 4 });

        CreateSut().LayoutChanged(form);

        form.DesignerText.Should().Contain("\"Splash.frx\":",
            "the citation names the companion the form's first save will write beside it");
    }

    [Fact]
    public void AFirstHeaderMovesTheMarksByItsWholeHeight()
    {
        // A created form's buffer is its code alone, so marks in it are numbered from the first line of the
        // code. The moment a header appears in front of that code, every one of them moves by its height --
        // the same rule as any other refresh, with an empty header as the before.
        var form = new FormDefinition(Project, FormComponentClass.Instance, "Form1");
        var document = DocumentIdentity.For(form);
        breakpoints.SetDocument(document, [1, 4]);

        CreateSut().LayoutChanged(form);

        var height = form.DesignerText!.Count(c => c == '\n');
        height.Should().BeGreaterThan(0);
        breakpoints.GetBreakpoints(document).Should().Equal(1 + height, 4 + height);
    }

    [Fact]
    public void AFormWithNoNameAtAllIsStillNotRendered()
    {
        // Path.ChangeExtension("", ".frx") returns "" (measured on .NET 10), so a render handed a nameless
        // document would emit a citation of ""(colon)HHHH -- a file that looks valid and names nothing.
        // Nothing here produces a nameless form; if one ever arrives it gets no render rather than that.
        var form = new FormDefinition(Project, FormComponentClass.Instance, "Form1");
        form.Components[0].SetProperty(VBProperties.NameProperty, "");

        CreateSut().LayoutChanged(form);

        form.DesignerText.Should().BeNull();
    }

    [Fact]
    public void OnlyTheReaderEverHoldsAFormReadOnly()
    {
        // The design record says a form held read-only is never re-rendered, and the fidelity gate is the
        // whole of it -- which is only true while being unable to reproduce a form is the ONLY thing that
        // holds one read-only. This pins that, because a second cause added later would silently make the
        // sentence false: a form with no file has never been through the deserializer, so the branch that
        // renders one cannot reach an unfaithful form by construction rather than by a second check.
        var created = new FormDefinition(Project, FormComponentClass.Instance, "Form1");

        created.CanSaveFaithfully.Should().BeTrue();
        created.UnfaithfulSaveCauses.Should().Be(UnfaithfulSaveCause.None);
    }

    // -- End to end, with nothing open -------------------------------------------------------------

    [Fact]
    public void ACommitReRendersTheHeaderAndMovesTheMarksWithNoCodeWindowOpen()
    {
        // The clause 3.3b exists for. Nothing here is open: the marks live in the stores, keyed by the
        // document's identity, and a header that grew has to move them there or they stay pointing at
        // whatever statement used to be on that line.
        var sut = CreateSut();
        var form = Deserialize(OneLabel);
        var document = DocumentIdentity.For(form);

        // Settle the header on what a render produces, so the second call measures the control rather than
        // the difference between the file's formatting and the serializer's.
        sut.LayoutChanged(form);
        var before = form.DesignerText!;
        var firstBodyLine = before.Count(c => c == '\n') + 1;
        breakpoints.SetDocument(document, [firstBodyLine + 1]);

        form.UpdateComponents([.. form.Components, AButton("Command1")]);
        sut.LayoutChanged(form);

        var grewBy = form.DesignerText!.Count(c => c == '\n') - before.Count(c => c == '\n');
        grewBy.Should().BeGreaterThan(0, "a control adds at least its Begin and its End");
        breakpoints.GetBreakpoints(document).Should().Equal(firstBodyLine + 1 + grewBy);
    }

    [AvaloniaFact]
    public void AddingAControlBelowABreakpoint()
    {
        // The code-editor delta's scenario, judged by the statement the breakpoint is on rather than by arithmetic,
        // in the open window and in the file the model composes. The control is added the way every designer path
        // announces a commit, on the bus.
        var sut = CreateSut();
        var form = Deserialize(OneLabel + "\r\n\r\nPrivate Sub Form_Load()\r\n    Label1.Caption = \"Ready\"\r\nEnd Sub\r\n");
        sut.LayoutChanged(form);
        var editor = NewCodeEditor().Initialize(form);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var document = DocumentIdentity.For(form);
        const string statement = "    Label1.Caption = \"Ready\"";
        var line = LineHolding(editor.Document.Text, statement);
        breakpoints.SetDocument(document, [line]);

        form.UpdateComponents([.. form.Components, AButton("Command1")]);
        bus.Publish(new FormLayoutChangedEvent(form));

        var now = breakpoints.GetBreakpoints(document).Should().ContainSingle().Subject;
        now.Should().BeGreaterThan(line, "the premise: the designer block grew above it");
        LineAt(editor.Document.Text, now).Should().Be(statement, "the breakpoint is still on the same statement");
        LineAt(FormCodeText.WholeFile(form), now).Should().Be(statement, "in the file the model composes too");
        editor.Dispose();

        static int LineHolding(string text, string line) =>
            Array.IndexOf(text.Replace("\r\n", "\n").Split('\n'), line) + 1;
        static string LineAt(string text, int line) => text.Replace("\r\n", "\n").Split('\n')[line - 1];
    }

    [Fact]
    public void TheNotificationOnTheBusIsWhatDrivesIt()
    {
        // The four paths that can commit a layout change do not know about this class, and must not have to:
        // the designer's undo stack, the menu editor, the colour palette and automation with no designer
        // open all publish the one event instead.
        var sut = CreateSut();
        var form = Deserialize(OneLabel);
        sut.LayoutChanged(form);
        var before = form.DesignerText!;
        form.UpdateComponents([.. form.Components, AButton("Command1")]);

        bus.Publish(new FormLayoutChangedEvent(form));

        form.DesignerText.Should().NotBe(before);
        form.DesignerText.Should().Contain("Command1");
    }

    [Fact]
    public void TheRenderedHeaderIsTheDesignerHalfAndStopsAtTheRootEnd()
    {
        // The composition invariant the whole phase rests on: prefix + Code is the file. If the render ran
        // past the root End it would put VERSION and Begin VB.Form into the code window twice, and the
        // second copy would be compiled as VB.
        var sut = CreateSut();
        var form = Deserialize(OneLabel);
        form.UpdateComponents([.. form.Components, AButton("Command1")]);

        sut.LayoutChanged(form);

        // The Command1 clause is what stops this passing vacuously: every assertion below is equally true
        // of the text as READ, so without it the test would go green with the render deleted.
        form.DesignerText.Should().Contain("Begin VB.CommandButton Command1");
        form.DesignerText.Should().EndWith("End\r\n");
        form.DesignerText.Should().NotContain("Option Explicit");
        FormCodeText.WholeFile(form).Should().Be(form.DesignerText + form.Code);
    }

    // -- With a window open ------------------------------------------------------------------------

    [Fact]
    public void ANewHeaderIsRecordedEvenWhenItIsTheOnlyThingThatChanged()
    {
        // ApplyHeader is what a save calls with the header it just wrote. The serializer is a reproduction
        // rather than a byte-faithful copy, so this fires on the first save of a file HexIDE did not write
        // -- which is the case the invariant is worded for: after a save the buffer is the text that save
        // wrote, not the text it was opened with.
        var form = Deserialize(OneLabel);

        CreateSut().ApplyHeader(form, "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n");

        form.DesignerText.Should().Be("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n");
    }

    [AvaloniaFact]
    public void AnOpenCodeWindowIsReHeadedWhereItStands()
    {
        // The other half of the same operation, and the only part a developer sees. The refresher finds the
        // window by the document's IDENTITY rather than by a name or a path, because both of those change
        // under it -- renaming the form is one of the things that moves the header.
        var form = Deserialize(OneLabel);
        var editor = NewCodeEditor().Initialize(form);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);

        sut.ApplyHeader(form, "VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n");

        editor.Document.Text.Should().Be("VERSION 5.00\r\nBegin VB.Form Form1\r\nEnd\r\n" + form.Code);
        editor.BufferBody.Should().Be(form.Code);
        editor.Dispose();
    }

    [AvaloniaFact]
    public void AWindowShowingAnOlderHeaderThanTheModelIsBroughtBackIntoLine()
    {
        // An undo in the code window puts the previous header back in the buffer while the model keeps the
        // current one, so the two can disagree with nobody at fault. Gating the buffer half on "the model
        // moved" would leave that window showing a header for a form that no longer looks like it, and
        // leave it FOREVER: the next render equals what is recorded, so nothing would repair it.
        var form = Deserialize(OneLabel);
        var editor = NewCodeEditor().Initialize(form);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);

        var current = "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"x\"\r\nEnd\r\n";
        sut.ApplyHeader(form, current);
        editor.Document.UndoStack.Undo();                       // the buffer drifts back; the model does not
        editor.Document.Text.Should().NotStartWith(current);

        sut.ApplyHeader(form, current);                         // the same header the model already holds

        editor.Document.Text.Should().StartWith(current);
        editor.BufferBody.Should().Be(form.Code);
        editor.Dispose();
    }

    // -- VB_Name follows a rename (task 3.5, #473) ---------------------------------------------------

    private const string FiveAttributes = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Orders"
        End
        Attribute VB_Name = "Form1"
        Attribute VB_GlobalNameSpace = False
        Attribute VB_Creatable = False
        Attribute VB_PredeclaredId = True
        Attribute VB_Exposed = False
        Option Explicit
        """;

    private static void Rename(FormDefinition form, string name) =>
        form.Components.Single(c => c.BaseClass.VBTypeName == "VB.Form")
            .SetProperty(VBProperties.NameProperty, name);

    [Fact]
    public void ARenamedFormsFileNamesOneFormNotTwo()
    {
        // #473, as filed: the Begin line followed a rename and the attribute did not, so the file HexIDE
        // wrote named two different forms. Asserted on what a save writes, because that is where it bit.
        var form = Deserialize(FiveAttributes);
        Rename(form, "frmOrders");

        CreateSut().LayoutChanged(form);

        var (frm, _) = new FormSerializer().Serialize(form, "Form1.frm");
        frm.Should().Contain("Begin VB.Form frmOrders");
        frm.Should().Contain("Attribute VB_Name = \"frmOrders\"");
        frm.Should().NotContain("\"Form1\"");
    }

    [Fact]
    public void OnlyTheNameMovesAndTheOtherFourAttributesAreLeftAsTheyWere()
    {
        // VB_PredeclaredId and VB_Exposed are how VB6 encodes what a form IS to other code. A retarget that
        // regenerated the block, rather than rewriting one line of it, would reset them.
        var form = Deserialize(FiveAttributes);
        var asRead = form.Code;
        Rename(form, "frmOrders");

        CreateSut().LayoutChanged(form);

        form.Code.Should().Be(asRead.Replace("\"Form1\"", "\"frmOrders\""));
    }

    [Fact]
    public void AFormHexIdeCreatedGainsNoAttributeBlock()
    {
        // The fidelity gap the task records rather than fixes: VB6 writes five attributes for a form it
        // creates, HexIDE writes none, and inventing them is not this routine's decision to take.
        var form = new FormDefinition(Project, FormComponentClass.Instance, "Form1");
        var asCreated = form.Code;
        Rename(form, "frmOrders");

        CreateSut().LayoutChanged(form);

        form.Code.Should().Be(asCreated);
    }

    [Fact]
    public void AFormThatCannotBeSavedFaithfullyKeepsTheNameItWasReadWith()
    {
        // The same gate as the header: a form HexIDE cannot reproduce is not edited by it either. Its save
        // is refused, so what matters is that the code window goes on showing the file that refusal protects.
        var form = Deserialize(FiveAttributes);
        var asRead = form.Code;
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        Rename(form, "frmOrders");

        CreateSut().LayoutChanged(form);

        form.Code.Should().Be(asRead);
    }

    [Fact]
    public void AUserControlFollowsItsModulesName()
    {
        // A .ctl keeps VB_Name in its module's Code, not in the designer half, and the document's name is the
        // module's. The rename gesture itself does not exist yet (#493); this is what it will call.
        var module = TestHelpers.CreateModule(name: "ucGauge", kind: ModuleKind.UserControl);
        module.Code.Should().Be("Attribute VB_Name = \"ucGauge\"\r\n");
        module.Name = "ucDial";

        CreateSut().NameChanged(DocumentIdentity.For(module));

        module.Code.Should().Be("Attribute VB_Name = \"ucDial\"\r\n");
    }

    [AvaloniaFact]
    public void AnOpenWindowFollowsTooAndTheBufferIsStillTheFile()
    {
        // The invariant the phase rests on, after a rename: prefix + code is what a save would write. The
        // header half is the refresh 3.3 built; the VB_Name half is below the prefix and is this task's.
        var form = Deserialize(FiveAttributes);
        var editor = NewCodeEditor().Initialize(form);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);
        Rename(form, "frmOrders");

        sut.LayoutChanged(form);

        editor.BufferBody.Should().Be(form.Code);
        editor.BufferBody.Should().StartWith("Attribute VB_Name = \"frmOrders\"");
        editor.Document.Text.Should().Be(form.DesignerText + form.Code);
        editor.Dispose();
    }

    [AvaloniaFact]
    public void AStandardModulesOpenWindowIsReHeadedOnARename()
    {
        // A .bas keeps VB_Name in its header, which the model already renders from the live name -- so the
        // model is right the moment the name changes, and the open buffer is the only thing left stale.
        var module = TestHelpers.CreateModule(name: "Module1");
        module.UpdateCode("Option Explicit\r\n");
        var editor = NewCodeEditor().Initialize(module);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);
        module.Name = "Utilities";
        editor.Document.Text.Should().StartWith("Attribute VB_Name = \"Module1\"", "nothing has told the window");

        sut.NameChanged(DocumentIdentity.For(module));

        editor.Document.Text.Should().Be("Attribute VB_Name = \"Utilities\"\r\nOption Explicit\r\n");
        editor.BufferBody.Should().Be("Option Explicit\r\n");
        editor.Dispose();
    }

    [AvaloniaFact]
    public void ACommitThatRenamesNothingPushesNothing()
    {
        // LayoutChanged fires on every committed nudge of a control. An undo entry per nudge would be one
        // Ctrl+Z the developer did not earn, and pushing any entry clears their redo stack.
        var form = Deserialize(FiveAttributes);
        var editor = NewCodeEditor().Initialize(form);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);
        sut.LayoutChanged(form);                  // settle the header on the serializer's own formatting
        editor.Document.UndoStack.ClearAll();

        sut.LayoutChanged(form);

        editor.Document.UndoStack.CanUndo.Should().BeFalse();
    }

    [AvaloniaFact]
    public void ARenameIsOneUndoEntryWhateverUndoesIt()
    {
        // The Begin line and the VB_Name line are one gesture. Popped by a route that does not come through
        // the code window's own Undo (#513), two entries would separate them and leave the buffer naming the
        // form one way in its header and another in its code.
        var form = Deserialize(FiveAttributes);
        var editor = NewCodeEditor().Initialize(form);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);
        sut.LayoutChanged(form);
        editor.Document.UndoStack.ClearAll();
        var beforeRename = editor.Document.Text;
        Rename(form, "frmOrders");
        sut.LayoutChanged(form);

        editor.Document.UndoStack.Undo();

        editor.Document.Text.Should().Be(beforeRename, "both halves go back together");
        editor.Document.UndoStack.CanUndo.Should().BeFalse("the rename was one entry, not two");
    }

    [AvaloniaFact]
    public void UndoInTheCodeWindowAfterARenameUndoesTheEditAndKeepsTheNewName()
    {
        // 3.8's policy, now that a commit writes below the prefix as well as in it. The code window's Undo
        // pops the IDE's own writes to reach the developer's edit and then puts back what the IDE owns --
        // which has to include the VB_Name line, or that line would be left naming the old form with nothing
        // afterwards that would ever repair it.
        var form = Deserialize(FiveAttributes);
        var editor = NewCodeEditor().Initialize(form);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);
        var asOpened = editor.BufferBody;

        editor.Document.Insert(editor.Document.TextLength, "\r\nDim x As Long");
        Rename(form, "frmOrders");
        sut.LayoutChanged(form);

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.BufferBody.Should().Be(asOpened.Replace("\"Form1\"", "\"frmOrders\""),
            "the typed line is gone and the code still names the form as it now is");
        editor.Document.Text.Should().Be(form.DesignerText + editor.BufferBody);
        form.DesignerText.Should().Contain("Begin VB.Form frmOrders");
        editor.Dispose();
    }

    [AvaloniaFact]
    public void AVbNameWriteWithNoHeaderWriteBesideItIsStillTheIdesOwn()
    {
        // A form's rename always moves its Begin line too, so a header write rides in the same entry and
        // would mark it on its own. A UserControl's does not: its name is its module's, its designer half is
        // untouched, and the VB_Name line is the ONLY write. Unmarked, the code window's Undo would take it
        // for the developer's edit, undo the rename and stop there, leaving the typed line in place.
        var module = TestHelpers.CreateModule(name: "ucGauge", kind: ModuleKind.UserControl);
        module.UpdateCode("Attribute VB_Name = \"ucGauge\"\r\nOption Explicit\r\n");
        var editor = NewCodeEditor().Initialize(module);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);

        editor.Document.Insert(editor.Document.TextLength, "Dim x As Long\r\n");
        module.Name = "ucDial";
        sut.NameChanged(DocumentIdentity.For(module));

        editor.UndoRequested(() => editor.Document.UndoStack.Undo());

        editor.BufferBody.Should().Be("Attribute VB_Name = \"ucDial\"\r\nOption Explicit\r\n");
        editor.Dispose();
    }

    [AvaloniaFact]
    public void TheCaretBelowTheRenamedLineStaysOnTheTextItWasOn()
    {
        // The view-model's own caret is moved with the text, because nothing pushes the control's back while
        // the replace is in flight and a document with no view attached has only this copy.
        var module = TestHelpers.CreateModule(name: "ucGauge", kind: ModuleKind.UserControl);
        module.UpdateCode("Attribute VB_Name = \"ucGauge\"\r\nOption Explicit\r\n");
        var editor = NewCodeEditor().Initialize(module);
        dock.OpenDocuments.Returns(new List<BaseEditorWindowViewModel> { editor });
        var sut = new HeaderRefresher(bus, dock, breakpoints, bookmarks);
        editor.CaretOffset = editor.Document.Text.IndexOf("Explicit", StringComparison.Ordinal);

        module.Name = "ucDialLonger";
        sut.NameChanged(DocumentIdentity.For(module));

        editor.Document.Text[editor.CaretOffset..].Should().StartWith("Explicit");
        editor.Dispose();
    }

    /// <summary>A code editor with every collaborator stubbed: only the buffer is under test here.</summary>
    private static CodeEditorViewModel NewCodeEditor()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>())
            .Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>())
            .Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>())
            .Returns(Substitute.For<IDisposable>());
        return new CodeEditorViewModel(
            Substitute.For<IWindowManager>(),
            Substitute.For<IEditorService>(),
            Substitute.For<HexIDE.Projects.IProjectService>(),
            eventBus,
            Substitute.For<HexIDE.Lsp.ILspClient>(),
            Substitute.For<ISettingsService>(),
            Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(),
            Substitute.For<IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
    }
}
