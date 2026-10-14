using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// No new breakpoint or bookmark on a line of the header or of a member's attribute lines
/// (hexide-io/HexIDE#273 task 3.12). F9, Debug ▸ Toggle Breakpoint, Ctrl+F2 and both gutters all toggle through
/// the two methods tested here; the gutters' wiring is tested against a rendered editor in
/// <c>MarkGutterOnReadOnlyLineTests</c>.
/// </summary>
public class MarkOnReadOnlyLineTests : IDisposable
{
    private readonly List<CodeEditorViewModel> made = [];
    private readonly ILocalizationService localization = Substitute.For<ILocalizationService>();
    private readonly IStatusBarService statusBar = Substitute.For<IStatusBarService>();
    private readonly BreakpointService breakpoints = new();
    private readonly BookmarkService bookmarks = new();

    public MarkOnReadOnlyLineTests()
    {
        localization.GetString("Str.CodeEditor.Msg.NoMarkOnReadOnlyLine").Returns("no mark on line {0}");
    }

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    // Header: lines 1-9. Code: Option Explicit on 10, the Sub on 12.
    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"OK\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Command1_Click()\r\n" +
        "    Command1.Enabled = False\r\n" +
        "End Sub\r\n";

    private const string ClassCode =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Currency\r\n" +
        "Attribute Total.VB_Description = \"The order total\"\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n";

    private CodeEditorViewModel NewEditor()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), statusBar, bookmarks, breakpoints,
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<IRunScope>(), localization);
        made.Add(vm);
        return vm;
    }

    private CodeEditorViewModel OpenForm() =>
        NewEditor().Initialize(new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Frm, NullSink.Instance)!);

    private CodeEditorViewModel OpenClass()
    {
        var module = TestHelpers.CreateModule(name: "Order", kind: ModuleKind.ClassModule);
        module.UpdateCode(ClassCode);
        return NewEditor().Initialize(module);
    }

    private static int LineOf(CodeEditorViewModel vm, string text) =>
        vm.Document.GetLineByOffset(vm.Document.Text.IndexOf(text, StringComparison.Ordinal)).LineNumber;

    // ── Breakpoints ─────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void PressingF9OnAHeaderLineSetsNoBreakpointAndSaysWhy()
    {
        // The interpreter-debugger delta's scenario.
        var vm = OpenForm();

        vm.ToggleBreakpoint(3).Should().BeFalse();

        breakpoints.GetBreakpoints(vm.Identity).Should().BeEmpty();
        statusBar.Received(1).SetTemporaryMessage("no mark on line 3", Arg.Any<TimeSpan>());
    }

    [AvaloniaFact]
    public void TheAttributeRunAtTheTopOfAFormsCodeIsHeaderToo()
    {
        // The header straddles the model's split: these two lines are the first of the form's Code. A test made
        // against the prefix alone would let them through.
        var vm = OpenForm();

        vm.ToggleBreakpoint(LineOf(vm, "Attribute VB_PredeclaredId")).Should().BeFalse();

        breakpoints.GetBreakpoints(vm.Identity).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ALineOfCodeTakesABreakpointAndGivesItBack()
    {
        var vm = OpenForm();
        var line = LineOf(vm, "Command1.Enabled");

        vm.ToggleBreakpoint(line).Should().BeTrue();
        breakpoints.GetBreakpoints(vm.Identity).Should().Equal(line);

        vm.ToggleBreakpoint(line).Should().BeTrue();
        breakpoints.GetBreakpoints(vm.Identity).Should().BeEmpty();
        statusBar.DidNotReceiveWithAnyArgs().SetTemporaryMessage(default!, default);
    }

    [AvaloniaFact]
    public void ADeclarationAMembersAttributeDescribesTakesABreakpoint()
    {
        // The trap 3.11 left. Asking the edit question about this line refuses it, because its line break is the
        // one the attribute run hangs from.
        var vm = OpenClass();
        var declaration = LineOf(vm, "Public Function Total");

        vm.ToggleBreakpoint(declaration).Should().BeTrue();
        vm.ToggleBreakpoint(declaration + 1).Should().BeFalse("that is the member's attribute line");

        breakpoints.GetBreakpoints(vm.Identity).Should().Equal(declaration);
    }

    [AvaloniaFact]
    public void ABreakpointAlreadyOnAHeaderLineCanBeRemoved()
    {
        // An edit or a reload can still leave one there, since a mark does not follow its line
        // (debugger-vb6-divergences D24). Refusing to take it away would leave a mark nothing in the window can clear.
        var vm = OpenForm();
        breakpoints.SetDocument(vm.Identity, [3]);

        vm.ToggleBreakpoint(3).Should().BeTrue();

        breakpoints.GetBreakpoints(vm.Identity).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void ABreakpointInAFormTheIdeCannotSaveFaithfully()
    {
        // The code-editor delta's scenario: being read-only as a whole is not a read-only region.
        var vm = OpenForm();
        vm.FormDefinition!.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        vm.IsReadOnly.Should().BeTrue("the fixture must actually be held read-only");
        var line = LineOf(vm, "Command1.Enabled");

        vm.ToggleBreakpoint(line).Should().BeTrue();

        breakpoints.GetBreakpoints(vm.Identity).Should().Equal([line], "it is set");
    }

    // ── Bookmarks ───────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TogglingABookmarkOnAHeaderLineSetsNone()
    {
        // The bookmarks delta's scenario.
        var vm = OpenForm();

        vm.ToggleBookmark(3).Should().BeFalse();

        bookmarks.GetBookmarks(vm.Identity).Should().BeEmpty();
        statusBar.Received(1).SetTemporaryMessage("no mark on line 3", Arg.Any<TimeSpan>());
    }

    [AvaloniaFact]
    public void ABookmarkIsTakenOneBasedAndStoredZeroBased()
    {
        var vm = OpenForm();
        var line = LineOf(vm, "Command1.Enabled");

        vm.ToggleBookmark(line).Should().BeTrue();

        bookmarks.GetBookmarks(vm.Identity).Should().Equal(line - 1);
    }

    [AvaloniaFact]
    public void ABookmarkAlreadyOnAHeaderLineCanBeRemoved()
    {
        var vm = OpenForm();
        bookmarks.SetBookmarks(vm.Identity, [2]);

        vm.ToggleBookmark(3).Should().BeTrue();

        bookmarks.GetBookmarks(vm.Identity).Should().BeEmpty();
    }

    // ── Numbering ───────────────────────────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void TheWindowNumbersItsBufferAsTheModelNumbersTheFile()
    {
        // One numbering whichever side answers: the automation tools ask the open window, the sidecar the model.
        var vm = OpenForm();

        vm.CodeWindowText.Text.Should().Be(CodeWindowText.Of(vm.Identity).Text);
        vm.CodeWindowText.HeaderLineCount.Should().Be(CodeWindowText.Of(vm.Identity).HeaderLineCount).And.Be(9);
    }
}
