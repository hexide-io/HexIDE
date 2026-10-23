using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The procedure the code window adds when the developer asks for an event: a double-click on the form or a control,
/// or an event picked in the Procedure box (hexide-io/HexIDE#1532).
/// </summary>
/// <remarks>
/// Measured in VB6's IDE: a double-click on a form with no code writes <c>Private Sub Form_Load()</c>, a blank line
/// and <c>End Sub</c>, CRLF, straight after the <c>Attribute</c> lines. On a form with code it goes at the end, one
/// blank line below it, and the caret is on the blank line inside (docs/vb6-fidelity-oracle.md, "What VB6's Add Form
/// saves"). Since a new form no longer starts with a <c>Form_Load</c>, this is how every new form gets one.
/// </remarks>
public class EventStubTests : IDisposable
{
    private readonly List<CodeEditorViewModel> made = [];
    private Action<CreateOrNavigateToSubEvent>? navigate;

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "Project1");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Attributes =
        "Attribute VB_Name = \"Form1\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = False\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Attribute VB_Exposed = False\r\n";

    private const string FormWithCode =
        "VERSION 5.00\r\n" +
        "Begin VB.Form Form1 \r\n" +
        "   Caption         =   \"Form1\"\r\n" +
        "   Begin VB.CommandButton Command1 \r\n" +
        "      Caption         =   \"Command1\"\r\n" +
        "   End\r\n" +
        "End\r\n" +
        Attributes +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Zeta()\r\n" +
        "    Debug.Print 1\r\n" +
        "End Sub\r\n";

    private CodeEditorViewModel Open(FormDefinition form)
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe(Arg.Do<Action<CreateOrNavigateToSubEvent>>(a => navigate = a)).Returns(Substitute.For<IDisposable>());
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

    /// <summary>Where the blank line inside the procedure just added starts.</summary>
    private static int BlankLineIn(CodeEditorViewModel vm, string declaration) =>
        vm.Document.Text.IndexOf(declaration, StringComparison.Ordinal) + declaration.Length;

    [AvaloniaFact]
    public void A_double_click_on_a_new_form_adds_Form_Load_as_VB6_writes_it()
    {
        var form = FormDefinition.NewForm(Project, FormComponentClass.Instance, "Form1");
        var vm = Open(form);

        navigate!(new CreateOrNavigateToSubEvent(form, "Form_Load"));

        vm.Document.Text.Should().EndWith(Attributes + "Private Sub Form_Load()\r\n\r\nEnd Sub\r\n",
            "VB6 writes it Private, in CRLF, straight after the Attribute lines");
        vm.CaretOffset.Should().Be(BlankLineIn(vm, "Private Sub Form_Load()\r\n"));
    }

    [AvaloniaFact]
    public void A_double_click_on_a_form_with_code_adds_Form_Load_at_the_end_one_blank_line_below_it()
    {
        var form = new FormDeserializer().Deserialize(Project, FormWithCode, NullSink.Instance)!;
        var vm = Open(form);

        navigate!(new CreateOrNavigateToSubEvent(form, "Form_Load"));

        vm.Document.Text.Should().EndWith(
            "Private Sub Zeta()\r\n    Debug.Print 1\r\nEnd Sub\r\n\r\nPrivate Sub Form_Load()\r\n\r\nEnd Sub\r\n");
        vm.CaretOffset.Should().Be(BlankLineIn(vm, "Private Sub Form_Load()\r\n"));
    }

    [AvaloniaFact]
    public void An_event_picked_in_the_Procedure_box_is_added_as_a_double_click_adds_it()
    {
        var form = new FormDeserializer().Deserialize(Project, FormWithCode, NullSink.Instance)!;
        var vm = Open(form);

        vm.SelectedObject = "Command1";
        vm.SelectedProcedure = "Click";

        vm.Document.Text.Should().EndWith(
            "End Sub\r\n\r\nPrivate Sub Command1_Click()\r\n\r\nEnd Sub\r\n");
        vm.CaretOffset.Should().Be(BlankLineIn(vm, "Private Sub Command1_Click()\r\n"));
    }
}
