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
/// blank line below it, and the caret is on the blank line inside (docs/vb6-fidelity-oracle/what-vb6s-add-form-saves.md, "What VB6's Add Form
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

        navigate!(new CreateOrNavigateToSubEvent(form, "Form_Load", "Form", "Load"));

        vm.Document.Text.Should().EndWith(Attributes + "Private Sub Form_Load()\r\n\r\nEnd Sub\r\n",
            "VB6 writes it Private, in CRLF, straight after the Attribute lines");
        vm.CaretOffset.Should().Be(BlankLineIn(vm, "Private Sub Form_Load()\r\n"));
    }

    [AvaloniaFact]
    public void A_double_click_on_a_form_with_code_adds_Form_Load_at_the_end_one_blank_line_below_it()
    {
        var form = new FormDeserializer().Deserialize(Project, FormWithCode, NullSink.Instance)!;
        var vm = Open(form);

        navigate!(new CreateOrNavigateToSubEvent(form, "Form_Load", "Form", "Load"));

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

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_double_click_skips_a_caption_naming_the_handler(bool handlerExists)
    {
        var source = FormWithCode.Replace("\"Command1\"", "\"sUb Command1_Click\"");
        if (handlerExists) source += "\r\nPrivate Sub Command1_Click()\r\n\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;
        var vm = Open(form);
        var before = vm.Document.Text;

        navigate!(new CreateOrNavigateToSubEvent(form, "Command1_Click", "Command1", "Click"));

        vm.CaretOffset.Should().Be(BlankLineIn(vm, "Private Sub Command1_Click()\r\n"));
        if (handlerExists) vm.Document.Text.Should().Be(before);
        else vm.Document.Text.Should().EndWith("Private Sub Command1_Click()\r\n\r\nEnd Sub\r\n");
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_Procedure_box_skips_a_caption_naming_the_handler(bool handlerExists)
    {
        var source = FormWithCode.Replace("\"Command1\"", "\"sUb Command1_Click\"");
        if (handlerExists) source += "\r\nPrivate Sub Command1_Click()\r\n\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;
        var vm = Open(form);
        var before = vm.Document.Text;

        vm.SelectedObject = "Command1";
        vm.SelectedProcedure = "Click";

        var declaration = vm.Document.Text.IndexOf("Private Sub Command1_Click()", StringComparison.Ordinal);
        declaration.Should().BeGreaterThan(0);
        vm.CaretOffset.Should().Be(handlerExists
            ? vm.Document.GetLineByOffset(declaration).EndOffset
            : BlankLineIn(vm, "Private Sub Command1_Click()\r\n"));
        if (handlerExists) vm.Document.Text.Should().Be(before);
        else vm.Document.Text.Should().EndWith("Private Sub Command1_Click()\r\n\r\nEnd Sub\r\n");
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Event_navigation_skips_a_member_attribute_naming_the_handler(bool dropdown, bool handlerExists)
    {
        var source = FormWithCode.Replace("Private Sub Zeta()\r\n",
            "Private Sub Zeta()\r\nAttribute Zeta.VB_Description = \"Undoes what Sub Command1_Click did\"\r\n");
        if (handlerExists) source += "\r\nPrivate Sub Command1_Click()\r\n\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;
        var vm = Open(form);
        var before = vm.Document.Text;

        if (dropdown)
        {
            vm.SelectedObject = "Command1";
            vm.SelectedProcedure = "Click";
        }
        else navigate!(new CreateOrNavigateToSubEvent(form, "Command1_Click", "Command1", "Click"));

        var declaration = vm.Document.Text.IndexOf("Private Sub Command1_Click()", StringComparison.Ordinal);
        declaration.Should().BeGreaterThan(0);
        vm.CaretOffset.Should().Be(dropdown && handlerExists
            ? vm.Document.GetLineByOffset(declaration).EndOffset
            : BlankLineIn(vm, "Private Sub Command1_Click()\r\n"));
        vm.IsReadOnlyRegion(vm.CaretOffset, 0).Should().BeFalse();
        if (handlerExists) vm.Document.Text.Should().Be(before);
        else vm.Document.Text.Should().EndWith("Private Sub Command1_Click()\r\n\r\nEnd Sub\r\n");
    }

    [AvaloniaFact]
    public void A_double_click_navigates_past_the_handlers_member_attributes()
    {
        var source = FormWithCode + "\r\nPrivate Sub Command1_Click()\r\n"
            + "Attribute Command1_Click.VB_Description = \"Clicks\"\r\n"
            + "    Debug.Print 2\r\nEnd Sub\r\n";
        var form = new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;
        var vm = Open(form);

        navigate!(new CreateOrNavigateToSubEvent(form, "Command1_Click", "Command1", "Click"));

        vm.CaretOffset.Should().Be(vm.Document.Text.IndexOf("    Debug.Print 2", StringComparison.Ordinal));
        vm.IsReadOnlyRegion(vm.CaretOffset, 0).Should().BeFalse();
    }

    // VB6 SP6 IDE measurements recorded in hexide-io/HexIDE#4205 (oracle PR #4358).
    [AvaloniaTheory]
    [InlineData(false, "Click", "Index As Integer")]
    [InlineData(false, "MouseDown", "Index As Integer, Button As Integer, Shift As Integer, X As Single, Y As Single")]
    [InlineData(true, "Click", "Index As Integer")]
    public void An_array_elements_stub_puts_Index_first(bool doubleClick, string eventName, string arguments)
    {
        var source = FormWithCode.Replace("      Caption", "      Index = 0\r\n      Caption");
        var form = new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;
        var vm = Open(form);
        if (doubleClick) navigate!(new CreateOrNavigateToSubEvent(form, "Command1_Click", "Command1", "Click"));
        else
        {
            vm.SelectedObject = "Command1";
            vm.SelectedProcedure = eventName;
        }
        vm.Document.Text.ReplaceLineEndings("\n").Should().Contain(
            $"Private Sub Command1_{eventName}({arguments})\n\nEnd Sub");
    }

    private static FormDefinition FormWithArray(string secondName = "Command1")
    {
        var source = FormWithCode.Replace("      Caption", "      Index = 0\r\n      Caption")
            .Replace("   End\r\nEnd\r\n", "   End\r\n"
                + $"   Begin VB.CommandButton {secondName} \r\n      Index = 1\r\n   End\r\nEnd\r\n");
        return new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;
    }

    // The Object box lists an array once (hexide-io/HexIDE#4557).
    [AvaloniaTheory]
    [InlineData("Command1")]
    [InlineData("cOMMAND1")]
    public void The_Object_box_lists_an_array_once_preserving_the_first_names_spelling(string secondName)
    {
        var vm = Open(FormWithArray(secondName));

        vm.ObjectNames.Should().Equal("(General)", "Form", "Command1");
    }

    [AvaloniaFact]
    public void Choosing_an_array_event_lists_its_events_and_writes_one_indexed_stub()
    {
        var vm = Open(FormWithArray());
        vm.SelectedObject = "Command1";
        vm.ProcedureNames.Should().Contain("Click");

        vm.SelectedProcedure = "Click";
        vm.SelectedObject = "Form";
        vm.SelectedObject = "Command1";
        vm.SelectedProcedure = "Click";

        vm.Document.Text.ReplaceLineEndings("\n").Should()
            .Contain("Private Sub Command1_Click(Index As Integer)\n\nEnd Sub");
        vm.Document.Text.Split("Private Sub Command1_Click(").Should().HaveCount(2);
    }

    [AvaloniaFact]
    public void The_Object_box_keeps_every_non_array_control_in_component_order()
    {
        var source = FormWithCode.Replace("   End\r\nEnd\r\n", "   End\r\n"
            + "   Begin VB.TextBox Text1 \r\n   End\r\n"
            + "   Begin VB.CommandButton Command2 \r\n   End\r\nEnd\r\n");
        var vm = Open(new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!);

        vm.ObjectNames.Should().Equal("(General)", "Form", "Command1", "Text1", "Command2");
    }

    [AvaloniaFact]
    public void An_elements_Index_is_retained_when_its_frm_is_opened_from_disk()
    {
        var path = Path.Combine(AppContext.BaseDirectory, $"element-{Guid.NewGuid():N}.frm");
        try
        {
            File.WriteAllText(path, FormWithCode.Replace("      Caption", "      Index = 0\r\n      Caption"));
            var form = new FormDeserializer().Deserialize(Project, File.ReadAllText(path), NullSink.Instance)!;
            var vm = Open(form);
            vm.SelectedObject = "Command1";
            vm.SelectedProcedure = "Click";
            vm.Document.Text.ReplaceLineEndings("\n").Should().Contain("Private Sub Command1_Click(Index As Integer)\n");
        }
        finally { File.Delete(path); }
    }

    [AvaloniaFact]
    public void A_double_click_carries_a_control_name_containing_an_underscore()
    {
        var form = new FormDeserializer().Deserialize(Project,
            FormWithCode.Replace("Command1", "Command_one").Replace("      Caption", "      Index = 0\r\n      Caption"), NullSink.Instance)!;
        var vm = Open(form);
        navigate!(new CreateOrNavigateToSubEvent(form, "Command_one_Click", "Command_one", "Click"));
        vm.Document.Text.ReplaceLineEndings("\n").Should().Contain("Private Sub Command_one_Click(Index As Integer)\n");
    }

    // A UserControl's root offers its own events, with the arguments VB6's stubs declare (measured on VB6 SP6, #1632). It
    // offered a form's five. The name before the event is #937's: the code window lists the root under its own name.
    [AvaloniaFact]
    public void A_UserControl_s_Procedure_box_offers_its_own_events_with_VB6_s_arguments()
    {
        var control = new FormDeserializer().Deserialize(Project,
            "VERSION 5.00\r\nBegin VB.UserControl UserControl1 \r\n   ClientHeight    =   3600\r\n   ClientLeft      =   0\r\n"
          + "   ClientTop       =   0\r\n   ClientWidth     =   4800\r\nEnd\r\nAttribute VB_Name = \"UserControl1\"\r\n", NullSink.Instance)!;
        var vm = Open(control);

        vm.SelectedObject = "UserControl1";

        vm.ProcedureNames.Should().HaveCount(35).And.Contain(["Initialize", "InitProperties", "ReadProperties", "WriteProperties"]);
        vm.ProcedureNames.Should().NotContain("Load", "a UserControl has no Load");
        vm.SelectedProcedure = "ReadProperties";
        vm.Document.Text.Should().Contain("_ReadProperties(PropBag As PropertyBag)\r\n\r\nEnd Sub\r\n");
    }
}
