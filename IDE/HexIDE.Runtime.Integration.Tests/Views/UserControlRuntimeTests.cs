using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

public partial class UserControlKeyTests
{
    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("", 425)]
    [InlineData("InvisibleAtRuntime = -1\nControlContainer = -1", 425)]
    [InlineData("ControlContainer = -1", 0)]
    public void Container_assignment_obeys_the_UserControl_setting(string settings, int error)
    {
        using var run = new Run(settings, formCode:
            "Private Sub Probe()\nOn Error Resume Next\nSet Command1.Container = uc1\nDebug.Print Err.Number\n"
            + (error == 0 ? "Debug.Print Command1.Left\nDebug.Print Command1.Top\nDebug.Print Command1.Container Is uc1\n" : "")
            + "End Sub\n");
        run.Context.ExecuteSub("Probe");
        run.Lib.Printed.First().Should().Be(new Vb6Value((long)error));
        if (error == 0)
            run.Lib.Printed.Skip(1).Should().Equal(new Vb6Value(120f), new Vb6Value(1200f), new Vb6Value(true));
    }

    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("", 0)]
    [InlineData("ControlContainer = -1", 1)]
    public async Task ContainedControls_counts_placed_children(string settings, int count)
    {
        using var run = new Run(settings, child: count == 0 ? "" : Block("VB.CommandButton", "Inner", 2),
            controlCode: "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)\nDebug.Print CStr(UserControl.ContainedControls.Count)\nEnd Sub\n");
        ((VBUserControl)run.Named("uc1")).AccessKey(113);
        run.Lib.Printed.Should().Equal(new Vb6Value(count == 0 ? "0" : "1"));
        if (count == 1)
        {
            await run.Code("Debug.Print Inner.Container Is uc1\nDebug.Print Inner.Left\nDebug.Print Inner.Top\n");
            run.Lib.Printed.Skip(1).Should().Equal(new Vb6Value(true), new Vb6Value(120f), new Vb6Value(1200f));
        }
    }

    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaFact]
    public void Align_bottom_uses_the_forms_full_width_and_keeps_height()
    {
        using var run = new Run("Alignable = -1", formCode: "Private Sub Probe()\nDebug.Print uc1.Align\nuc1.Align = 2\nDebug.Print uc1.Left\nDebug.Print uc1.Top\nDebug.Print uc1.Width\nDebug.Print uc1.Height\nOn Error Resume Next\nDebug.Print uc1.Negotiate\nDebug.Print Err.Number\nEnd Sub\n");
        run.Window.Width = 600;
        run.Window.Height = 400;
        run.Named("uc1").Height = 60;
        run.Context.ExecuteSub("Probe");
        run.Lib.Printed.Should().Equal(new Vb6Value((short)0), new Vb6Value(0f), new Vb6Value(5100f),
            new Vb6Value(9000f), new Vb6Value(900f), new Vb6Value(393L));
    }

    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("", true)]
    [InlineData("BackStyle = 0", true)]
    [InlineData("Windowless = -1", false)]
    public void BackStyle_controls_the_runtime_background(string settings, bool transparent)
    {
        using var run = new Run(settings, controlCode:
            "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)\nUserControl.BackStyle = 0\nEnd Sub\n");
        var uc = (VBUserControl)run.Named("uc1");
        if (settings.Length == 0)
            uc.AccessKey(113);
        ((Panel)uc.Child!).Background.Should().BeAssignableTo<ISolidColorBrush>().Which.Color.A.Should().Be(transparent ? (byte)0 : (byte)255);
    }

    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("", "enter,form,got")]
    [InlineData("Windowless = -1", "form,enter,got")]
    public void Focus_events_follow_the_measured_order(string settings, string order)
    {
        using var run = new Run(settings,
            formCode: "Private Sub uc1_GotFocus()\nDebug.Print \"form\"\nEnd Sub\nPrivate Sub uc1_LostFocus()\nDebug.Print \"formlost\"\nEnd Sub\n",
            controlCode: "Private Sub UserControl_EnterFocus()\nDebug.Print \"enter\"\nEnd Sub\nPrivate Sub UserControl_GotFocus()\nDebug.Print \"got\"\nEnd Sub\nPrivate Sub UserControl_LostFocus()\nDebug.Print \"lost\"\nEnd Sub\nPrivate Sub UserControl_ExitFocus()\nDebug.Print \"exit\"\nEnd Sub\n");
        run.Named("Text1").Focus().Should().BeTrue();
        run.Lib.Printed.Clear();
        run.Named("uc1").Focus().Should().BeTrue();
        run.Lib.Printed.Select(v => v.Value).Should().Equal(order.Split(','));
        run.Lib.Printed.Clear();
        run.Named("Text1").Focus().Should().BeTrue();
        run.Lib.Printed.Select(v => v.Value).Should().Equal("lost", "exit", "formlost");
    }
    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaTheory]
    [InlineData("", "initialize,read,resize,load,show,hide,terminate")]
    [InlineData("InvisibleAtRuntime = -1", "initialize,read,load,terminate")]
    public async Task Lifecycle_omits_visual_events_for_invisible_controls(string settings, string expected)
    {
        var controlCode = string.Concat(new[] { "Initialize", "ReadProperties", "Resize", "Show", "Hide", "Terminate" }
            .Select(name => $"Private Sub UserControl_{name}({(name == "ReadProperties" ? "PropBag As PropertyBag" : "")})\nDebug.Print \"{(name == "ReadProperties" ? "read" : name.ToLowerInvariant())}\"\nEnd Sub\n"));
        using var run = new Run(settings, formCode: "Private Sub Form_Load()\nDebug.Print \"load\"\nEnd Sub\n", controlCode: controlCode);
        await run.Context.UnloadAsync(1);
        run.Lib.Printed.Select(v => v.Value).Should().Equal(expected.Split(','));
    }

    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaFact]
    public void Entering_another_UserControl_enters_it_before_leaving_the_old_one()
    {
        using var run = new Run("", plain: true,
            formCode: "Private Sub uc1_LostFocus()\nDebug.Print \"formlost\"\nEnd Sub\nPrivate Sub ucPlain_GotFocus()\nDebug.Print \"formgot\"\nEnd Sub\n",
            controlCode: "Private Sub UserControl_EnterFocus()\nDebug.Print \"enter\"\nEnd Sub\nPrivate Sub UserControl_GotFocus()\nDebug.Print \"got\"\nEnd Sub\nPrivate Sub UserControl_LostFocus()\nDebug.Print \"lost\"\nEnd Sub\nPrivate Sub UserControl_ExitFocus()\nDebug.Print \"exit\"\nEnd Sub\n");
        run.Named("uc1").Focus().Should().BeTrue();
        run.Lib.Printed.Clear();
        run.Named("ucPlain").Focus().Should().BeTrue();
        run.Lib.Printed.Select(v => v.Value).Should().Equal("enter", "lost", "exit", "formlost", "formgot", "got");
    }

    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaFact]
    public async Task Visible_is_false_in_Load_and_true_after_Show()
    {
        using var run = new Run("", formCode: "Private Sub Form_Load()\nDebug.Print uc1.Visible\nEnd Sub\n");
        await run.Code("Debug.Print uc1.Visible\n");
        run.Lib.Printed.Should().Equal(new Vb6Value(false), new Vb6Value(true));
    }

    // #1631 part C: vb6.exe SP6 native/p-code, issuecomment-6425849231.
    [AvaloniaFact]
    public void Windowless_controls_come_last_in_the_Controls_collection()
    {
        using var run = new Run("Windowless = -1", plain: true, blocks:
            Block("VB.TextBox", "Text1", 0) + Block("P.Dial", "uc1", 1)
            + Block("VB.CommandButton", "Command1", 2) + Block("P.Plain", "ucPlain", 3));
        run.Context.FormInstance!.Controls!.All.Select(VBProps.GetName).Should().Equal("Text1", "Command1", "ucPlain", "uc1");
    }

    // #4337: vb6.exe SP6 native/p-code measurements in the issue's first comment (oracle PR #4341).
    private static string ArrayBlock => Block("P.Dial", "uc1", 1, extra: "Index = 0\nValue = 42");
    private const string ArrayCode = "Private id As Integer\n"
        + "Private Sub UserControl_Initialize()\nid = Form1.Current\nDebug.Print \"initialize\" & id\nEnd Sub\n"
        + "Private Sub UserControl_InitProperties()\nDebug.Print \"initproperties\"\nEnd Sub\n"
        + "Private Sub UserControl_Resize()\nDebug.Print \"resize\" & id\nIf id <> 0 Then Debug.Print Form1.uc1(id).Width\nEnd Sub\n"
        + "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print \"read\" & id\nEnd Sub\n"
        + "Private Sub UserControl_AccessKeyPress(KeyAscii As Integer)\nDebug.Print \"key\" & id\nEnd Sub\n"
        + "Private Sub UserControl_Show()\nDebug.Print \"show\" & id\nEnd Sub\n"
        + "Private Sub UserControl_Hide()\nDebug.Print \"hide\" & id\nEnd Sub\n"
        + "Private Sub UserControl_Terminate()\nDebug.Print \"terminate\" & id\nEnd Sub\n";

    [AvaloniaFact]
    public async Task Loaded_UserControl_attaches_the_same_module_with_its_own_instance()
    {
        using var run = new Run("", blocks: ArrayBlock, formCode: "Public Current As Integer\n", controlCode: ArrayCode);
        run.Lib.Printed.Clear();
        await run.Code("Current = 1\nLoad uc1(1)\n");
        var elements = run.Context.FormInstance!.Controls!.All.OfType<VBUserControl>().ToArray();
        elements[1].AccessKey(113);
        elements[0].AccessKey(113);
        run.Lib.Printed.Select(v => v.Value).Should().EndWith(new object[] { "key1", "key0" });
    }

    [AvaloniaFact]
    public async Task Loaded_UserControl_runs_Initialize_Resize_ReadProperties_inside_Load()
    {
        using var run = new Run("", blocks: ArrayBlock, formCode: "Public Current As Integer\n", controlCode: ArrayCode);
        run.Lib.Printed.Clear();
        await run.Code("Current = 1\nLoad uc1(1)\nDebug.Print \"returned\"\n");
        run.Lib.Printed.Select(v => v.Value).Should().Equal("initialize1", "resize1", 0f, "read1", "returned");
    }

    // A loaded element is attached as its template was (#4149's Ambient): it reads the form's BackColor, measured in #4149,
    // which an element attached with no form would refuse.
    [AvaloniaFact]
    public async Task Loaded_UserControl_reads_Ambient_from_its_templates_form()
    {
        using var run = new Run("", blocks: ArrayBlock, formSettings: "BackColor = &H00C0FFFF&\n",
            controlCode: "Private Sub UserControl_ReadProperties(PropBag As PropertyBag)\nDebug.Print CStr(Ambient.BackColor)\nEnd Sub\n");
        run.Lib.Printed.Clear();
        await run.Code("Load uc1(1)\n");
        run.Lib.Printed.Should().Equal(new Vb6Value("12648447"));
    }

    [AvaloniaFact]
    public async Task Loaded_UserControl_has_the_templates_geometry_and_starts_hidden()
    {
        using var run = new Run("", blocks: ArrayBlock, formCode: "Public Current As Integer\n", controlCode: ArrayCode);
        run.Lib.Printed.Clear();
        await run.Code("Current = 1\nLoad uc1(1)\nDebug.Print uc1(1).Width\nDebug.Print uc1(1).Height\nDebug.Print uc1(1).Top\nDebug.Print uc1(1).Visible\n");
        run.Lib.Printed.Select(v => v.Value).Should().Equal("initialize1", "resize1", 0f, "read1", 1200f, 375f, 600f, false);
    }

    [AvaloniaFact]
    public void Loaded_UserControls_show_in_index_order_before_Activate()
    {
        using var run = new Run("", blocks: ArrayBlock, controlCode: ArrayCode, activate: true,
            formCode: "Public Current As Integer\nPrivate Sub Form_Load()\nCurrent = 2\nLoad uc1(2)\nuc1(2).Visible = True\nCurrent = 1\nLoad uc1(1)\nuc1(1).Visible = True\nCurrent = 3\nLoad uc1(3)\nDebug.Print \"loaded\"\nEnd Sub\nPrivate Sub Form_Activate()\nDebug.Print \"activate\"\nEnd Sub\n");
        run.Lib.Printed.Select(v => v.Value).Should().EndWith(new object[] { "loaded", "show0", "show1", "show2", "activate" });
    }

    [AvaloniaFact]
    public void Loaded_UserControl_Show_waits_until_the_Visible_statement_returns()
    {
        using var run = new Run("", blocks: ArrayBlock, controlCode: ArrayCode,
            formCode: "Public Current As Integer\nPrivate Sub Probe()\nCurrent = 1\nLoad uc1(1)\nuc1(1).Visible = True\nDebug.Print \"returned\"\nEnd Sub\n");
        run.Lib.Printed.Clear();
        run.Context.ExecuteSub("Probe");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        run.Lib.Printed.Select(v => v.Value).Should().Equal("initialize1", "resize1", 0f, "read1", "returned", "show1");
    }

    [AvaloniaFact]
    public void Loaded_UserControl_Unload_Me_includes_elements_in_index_order()
    {
        using var run = new Run("", blocks: ArrayBlock, controlCode: ArrayCode,
            formCode: "Public Current As Integer\nPrivate Sub Probe()\nCurrent = 2\nLoad uc1(2)\nCurrent = 1\nLoad uc1(1)\nUnload Me\nEnd Sub\nPrivate Sub Form_Unload(Cancel As Integer)\nDebug.Print \"unload\"\nEnd Sub\n");
        run.Lib.Printed.Clear();
        run.Context.ExecuteSub("Probe");
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
        run.Lib.Printed.Select(v => v.Value).Should().EndWith(new object[] { "unload", "hide0", "terminate0", "hide1", "terminate1", "hide2", "terminate2" });
    }

}
