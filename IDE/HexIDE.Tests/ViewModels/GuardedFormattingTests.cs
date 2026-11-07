using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// A server's formatting answer reaches a code window through one guarded path, which leaves the header alone
/// (hexide-io/HexIDE#273 task 3.9: the formatting row of the writer policy).
/// </summary>
/// <remarks>
/// The fixture is a VB6-authored form, read through the real deserializer, and the answer has the shape the
/// bundled server was captured sending: ONE edit from (0,0) past the last line, every designer line pushed to
/// column zero, and <c>\n</c> throughout. Applied as sent, that edit cost a form its attribute block and the
/// first sixteen lines of its code on a plain Ctrl+S.
/// </remarks>
public class GuardedFormattingTests : IDisposable
{
    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

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
        "private sub cmdOK_Click()\r\n" +
        "unload me\r\n" +
        "end sub\r\n";

    private CodeEditorViewModel OpenForm(out FormDefinition form)
    {
        form = new FormDeserializer().Deserialize(Project, Frm, NullSink.Instance)!;
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
        return vm.Initialize(form);
    }

    /// <summary>The bundled server's answer: the whole document, re-indented to column zero, in \n.</summary>
    private static TextEdit WholeDocumentAnswer(string buffer, Func<string, string> formatCode)
    {
        var headerEnd = buffer.IndexOf("Option Explicit", StringComparison.Ordinal);
        var flattened = string.Join("\n", buffer[..headerEnd].Split("\r\n").Select(l => l.TrimStart()));
        var lines = buffer.Split('\n').Length;
        return new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(lines, 0)),
            flattened + formatCode(buffer[headerEnd..].Replace("\r\n", "\n")));
    }

    private static string Formatted(string code) => code
        .Replace("private sub", "Private Sub")
        .Replace("unload me", "    Unload Me")
        .Replace("end sub", "End Sub");

    [AvaloniaFact]
    public void FormattingAFormFormatsTheCodeAndLeavesTheHeaderUnchanged()
    {
        // The kind that bit: a form, whose header straddles the split. The delta's scenario names a class, and is
        // FormattingAClass below.
        var vm = OpenForm(out _);
        var header = vm.Document.Text[..vm.Document.Text.IndexOf("Option Explicit", StringComparison.Ordinal)];

        vm.ApplyFormatting([WholeDocumentAnswer(vm.Document.Text, Formatted)]);

        vm.Document.Text.Should().Be(header +
            "Option Explicit\r\n\r\nPrivate Sub cmdOK_Click()\r\n    Unload Me\r\nEnd Sub\r\n");
    }

    /// <summary>A class as VB6 writes it, the header with its decoded enumeration comments.</summary>
    private const string Cls =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "  Persistable = 0  'NotPersistable\r\n" +
        "  DataBindingBehavior = 0  'vbNone\r\n" +
        "  DataSourceBehavior  = 0  'vbNone\r\n" +
        "  MTSTransactionMode  = 0  'NotAnMTSObject\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Order\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = True\r\n" +
        "Attribute VB_PredeclaredId = False\r\n" +
        "Attribute VB_Exposed = False\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "public function total() as currency\r\n" +
        "total = 0\r\n" +
        "end function\r\n";

    /// <summary>A class read as the project service reads one: the header split off and kept, the body as its code.</summary>
    private CodeEditorViewModel OpenClass()
    {
        var (header, body) = ModuleFileFormat.SplitHeader(Cls, ModuleKind.ClassModule);
        header.Should().StartWith("VERSION 1.0 CLASS").And.EndWith("Attribute VB_Exposed = False\r\n",
            "the premise: the reader recognised the whole header, so the code window composes it as the prefix");
        var module = new ModuleDefinition(Project, "Order", ModuleKind.ClassModule);
        module.RecordOriginalHeader(header);
        module.UpdateCode(body);
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

    [AvaloniaFact]
    public void FormattingAClass()
    {
        // The code-editor delta's scenario as written: a class, formatted by a server whose answer replaces the
        // whole document. The answer flattens the class block's two-space indent to column zero and speaks \n,
        // as the bundled server's did, so a header that survives it survives because the guard kept it.
        var vm = OpenClass();
        var header = Cls[..Cls.IndexOf("Option Explicit", StringComparison.Ordinal)];
        vm.Document.Text.Should().Be(Cls, "the premise: the window holds the file");

        vm.ApplyFormatting([WholeDocumentAnswer(vm.Document.Text, code => code
            .Replace("public function total() as currency", "Public Function total() As Currency")
            .Replace("total = 0", "    total = 0")
            .Replace("end function", "End Function"))]);

        vm.Document.Text.Should().StartWith(header, "the header is unchanged, byte for byte");
        vm.Document.Text.Should().Be(header +
            "Option Explicit\r\n\r\nPublic Function total() As Currency\r\n    total = 0\r\nEnd Function\r\n",
            "and the code is formatted");
        vm.BufferBody.Should().Be(vm.Document.Text[header.Length..], "the split still falls where the header ends");
    }

    [AvaloniaFact]
    public void AndTheFlushAfterItWritesTheWholeCodeBack()
    {
        // The data loss itself: BufferBody is what a save writes, and it splits by the prefix's length. Before
        // the guard, the flattened header was shorter than the prefix and this lost the attribute block and the
        // lines below it.
        var vm = OpenForm(out var form);

        vm.ApplyFormatting([WholeDocumentAnswer(vm.Document.Text, Formatted)]);
        vm.Dispose();

        form.Code.Should().Be(
            "Attribute VB_Name = \"frmOrders\"\r\n" +
            "Attribute VB_PredeclaredId = True\r\n" +
            "Option Explicit\r\n\r\nPrivate Sub cmdOK_Click()\r\n    Unload Me\r\nEnd Sub\r\n");
    }

    [AvaloniaFact]
    public void AnAnswerThatChangesOnlyTheHeaderChangesNothing()
    {
        var vm = OpenForm(out _);
        var before = vm.Document.Text;

        var reduction = vm.ApplyFormatting([WholeDocumentAnswer(before, code => code)]);

        vm.Document.Text.Should().Be(before);
        reduction.Changes.Should().BeEmpty();
        reduction.DroppedLines.Should().BeGreaterThan(0);
        vm.Document.UndoStack.CanUndo.Should().BeFalse("nothing was written, so there is nothing to undo");
    }

    [AvaloniaFact]
    public void FormattingIsOneUndoStep()
    {
        var vm = OpenForm(out _);
        var before = vm.Document.Text;
        vm.ApplyFormatting([WholeDocumentAnswer(before, Formatted)]);

        vm.Document.UndoStack.Undo();

        vm.Document.Text.Should().Be(before);
        vm.Document.UndoStack.CanUndo.Should().BeFalse();
    }

    [AvaloniaFact]
    public void PreciseEditsOutsideTheHeaderAreAppliedAsSent()
    {
        // A server that answers with small edits, as most do, is not second-guessed: what it changes outside
        // a region lands exactly.
        var vm = OpenForm(out _);
        var line = vm.Document.GetLineByOffset(vm.Document.Text.IndexOf("unload me", StringComparison.Ordinal));

        vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(line.LineNumber - 1, 0), new Position(line.LineNumber - 1, 9)),
            "    Unload Me")]);

        vm.Document.Text.Should().Contain("\r\n    Unload Me\r\n");
    }

    [AvaloniaTheory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void FormattingDeletesTheRunWithItsWholeDeclaration(string newline)
    {
        var vm = OpenForm(out _);
        var declaration = "private sub cmdOK_Click()";
        var run = "Attribute cmdOK_Click.VB_Description = \"Orders\"";
        vm.Document.Text = vm.Document.Text.ReplaceLineEndings(newline)
            .Replace(declaration + newline, declaration + newline + run + newline);
        var before = vm.Document.Text;
        var after = before.Replace(declaration + newline, "")
            .Replace("unload me", "    Unload Me");

        vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(vm.Document.LineCount, 0)), after)]);

        vm.Document.Text.Should().Be(after.Replace(run + newline, ""));
        vm.Document.UndoStack.Undo();
        vm.Document.Text.Should().Be(before);
    }

    [AvaloniaFact]
    public void APreciseEditInsideTheHeaderIsDropped()
    {
        var vm = OpenForm(out _);
        var before = vm.Document.Text;

        vm.ApplyFormatting([new TextEdit(
            new HexIDE.Lsp.Messages.Range(new Position(2, 0), new Position(2, 3)), "")]);

        vm.Document.Text.Should().Be(before);
    }
}
