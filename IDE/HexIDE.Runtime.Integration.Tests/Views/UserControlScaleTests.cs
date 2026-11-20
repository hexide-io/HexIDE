using System.Linq;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.BuiltinControls;
using Avalonia.Headless.XUnit;

namespace HexIDE.Runtime.Integration.Tests.Views;

public partial class UserControlKeyTests
{
    // Measured on SP6, native and p-code alike, in the coordinate oracle's #4693 sections (oracle PRs #4723 and #4751).
    // Each row runs the successive steps of the probe.
    private static readonly string[] ScaleSteps =
    [
        "", "", "", "", "ScaleMode = 3", "Label1.Left = 20", "Scale (0, 0)-(100, 50)",
        "Scale (10, 50)-(110, 0)", "Scale", "ScaleMode = 7",
    ];

    [AvaloniaTheory]
    [InlineData(0, "CStr(ScaleMode) & \" \" & TypeName(ScaleMode) & \"|\" & CStr(ScaleWidth) & \" \" & TypeName(ScaleWidth) & \"|\" & CStr(ScaleHeight) & \"|\" & CStr(ScaleLeft) & \"|\" & CStr(ScaleTop)", "1 Integer|2400 Single|1800|0|0")]
    [InlineData(1, "CStr(Label1.Left) & \"|\" & CStr(Label1.Top) & \"|\" & CStr(Label1.Width) & \"|\" & CStr(Label1.Height)", "150|300|600|255")]
    [InlineData(2, "CStr(ScaleX(1, vbInches, vbTwips)) & \" \" & TypeName(ScaleX(1, vbInches, vbTwips))", "1440 Single")]
    [InlineData(3, "CStr(ScaleX(1440)) & \"|\" & CStr(ScaleY(15, vbPixels, vbTwips))", "816.378|225")]
    [InlineData(4, "CStr(ScaleWidth) & \"|\" & CStr(ScaleHeight) & \"|\" & CStr(Label1.Left) & \"|\" & CStr(Label1.Top) & \"|\" & CStr(Label1.Width) & \"|\" & CStr(Label1.Height)", "160|120|10|20|40|17")]
    [InlineData(5, "CStr(Label1.Left) & \"|\" & CStr(ScaleX(1440))", "20|54.4252")]
    [InlineData(6, "CStr(ScaleMode) & \"|\" & CStr(ScaleWidth) & \"|\" & CStr(ScaleHeight) & \"|\" & CStr(ScaleLeft) & \"|\" & CStr(Label1.Left) & \"|\" & CStr(Label1.Top) & \"|\" & CStr(Label1.Width) & \"|\" & CStr(Label1.Height)", "0|100|50|0|12.5|8.333333|25|7.083333")]
    [InlineData(7, "CStr(ScaleTop) & \"|\" & CStr(ScaleHeight) & \"|\" & CStr(Label1.Left) & \"|\" & CStr(Label1.Top) & \"|\" & CStr(Label1.Height)", "50|-50|22.5|41.66667|7.083333")]
    [InlineData(8, "CStr(ScaleMode) & \"|\" & CStr(ScaleWidth) & \"|\" & CStr(ScaleLeft) & \"|\" & CStr(Label1.Left)", "1|2400|0|300")]
    [InlineData(9, "CStr(ScaleWidth) & \"|\" & CStr(Label1.Left)", "4.233337|0.5291672")]
    public void UserControl_owns_its_coordinate_system(int row, string expression, string expected)
    {
        using var run = ScaleRun("", "150", "300", "600", "255",
            string.Join("\n", ScaleSteps.Take(row + 1)) + "\nDebug.Print " + expression);
        run.Faults.Should().BeEmpty();
        run.RunTimeErrors.Should().BeEmpty();
        run.Lib.Printed.Select(v => v.Value).Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("", "CStr(ScaleMode) & \" \" & TypeName(ScaleMode) & \"|\" & CStr(ScaleWidth) & \"|\" & CStr(ScaleHeight) & \"|\" & CStr(ScaleLeft) & \"|\" & CStr(ScaleTop)", "3 Integer|160|120|0|0")]
    [InlineData("", "CStr(Label1.Left) & \"|\" & CStr(Label1.Top) & \"|\" & CStr(Label1.Width) & \"|\" & CStr(Label1.Height)", "0.6666667|1.333333|2.666667|1.133333")]
    [InlineData("", "CStr(ScaleX(1440)) & \"|\" & CStr(ScaleX(1, vbInches, vbTwips))", "54.4252|1440")]
    [InlineData("ScaleMode = 1", "CStr(ScaleWidth) & \"|\" & CStr(ScaleHeight) & \"|\" & CStr(Label1.Left) & \"|\" & CStr(Label1.Top) & \"|\" & CStr(Label1.Width) & \"|\" & CStr(Label1.Height)", "2400|1800|10|20|40|17")]
    public void Pixel_saved_UserControl_children_are_still_saved_as_twips(string setup, string expression, string expected)
    {
        using var run = ScaleRun("ScaleMode = 3\nScaleWidth = 160\nScaleHeight = 120", "10", "20", "40", "17", setup + "\nDebug.Print " + expression);
        run.Faults.Should().BeEmpty();
        run.RunTimeErrors.Should().BeEmpty();
        run.Lib.Printed.Select(v => v.Value).Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("UserControl.", "ScaleMode = 3", "160|1440")]
    [InlineData("UserControl.", "Scale (0, 0)-(100, 50)", "100|1440")]
    public void Qualified_UserControl_coordinates_use_the_own_root(string target, string statement, string expected)
    {
        using var run = ScaleRun("", "150", "300", "600", "255",
            target + statement + $"\nDebug.Print CStr({target}ScaleWidth) & \"|\" & CStr({target}ScaleX(1, vbInches, vbTwips))");
        run.Faults.Should().BeEmpty();
        run.RunTimeErrors.Should().BeEmpty();
        run.Lib.Printed.Select(v => v.Value).Should().Equal(expected);
    }

    [AvaloniaTheory]
    [InlineData("o.Scale (0, 0)-(10, 10)")]
    [InlineData("o.ScaleX 1, vbInches, vbTwips")]
    [InlineData("o.ScaleY 1, vbInches, vbTwips")]
    [InlineData("Dim v\nv = o.ScaleX(1, vbInches, vbTwips)")]
    [InlineData("Dim v\nv = o.ScaleY(1, vbInches, vbTwips)")]
    public void Late_bound_UserControl_extender_has_no_scale(string statement)
    {
        using var run = new Run("", formCode: "Private Sub Form_Load()\nDim o As Object\nSet o = uc1\nOn Error Resume Next\n" + statement + "\nDebug.Print Err.Number\nEnd Sub");
        run.Faults.Should().BeEmpty();
        run.Lib.Printed.Select(v => v.Value).Should().Equal(438L);
    }

    // Preserve HexIDE's existing refusal, not a claimed VB6 measurement: the extender's scale properties
    // are unmeasured and must not acquire the root's mapping when the root gains a RuntimeScale (#4693).
    [AvaloniaFact]
    public async Task Unmeasured_extender_scale_properties_keep_their_previous_refusal()
    {
        using var run = new Run("");
        await run.Code("Dim o As Object\nSet o = uc1");
        var read = () => run.Code("Debug.Print o.ScaleMode");
        await read.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
        var write = () => run.Code("o.ScaleMode = 3");
        await write.Should().ThrowAsync<VBRunTimeException>();
    }

    [AvaloniaFact]
    public async Task Unmeasured_own_root_Object_alias_keeps_its_previous_refusal()
    {
        using var run = new Run("");
        var context = ((VBUserControl)run.Named("uc1")).Context!;
        Task Own(string code) => new StatementExecutor(context.Interpreter!, context.InstanceEnv, context.FormInstance!.ClassDef)
            .Visit(context.Interpreter!.TryParseStatement(code)!);
        await Own("Dim o As Object");
        await Own("Set o = UserControl");
        var read = () => Own("Debug.Print o.ScaleWidth");
        await read.Should().ThrowAsync<NotImplementedException>().WithMessage("*through an Object variable*#4693*");
        var write = () => Own("o.ScaleWidth = 100");
        await write.Should().ThrowAsync<NotImplementedException>().WithMessage("*through an Object variable*#4693*");
    }

    // #4693 measures the no-extents default for forms and PictureBoxes only. These are engine refusals,
    // not VB6 value assertions: an unmeasured saved UserControl mapping must not inherit that default.
    [AvaloniaTheory]
    [InlineData("ScaleLeft")]
    [InlineData("ScaleTop")]
    [InlineData("ScaleWidth")]
    [InlineData("ScaleHeight")]
    public async Task A_saved_UserControl_User_scale_without_extents_remains_refused(string member)
    {
        using var run = new Run("ScaleMode = 0");
        var context = ((VBUserControl)run.Named("uc1")).Context!;
        var read = () => new StatementExecutor(context.Interpreter!, context.InstanceEnv, context.FormInstance!.ClassDef)
            .Visit(context.Interpreter!.TryParseStatement("Debug.Print UserControl." + member)!);
        await read.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4693*");
    }

    private static Run ScaleRun(string settings, string left, string top, string width, string height, string code)
    {
        var run = new Run("ClientWidth = 2400\nClientHeight = 1800\n" + settings
            + $"\nBegin VB.Label Label1\nLeft = {left}\nTop = {top}\nWidth = {width}\nHeight = {height}\nEnd",
            controlCode: "Public Sub Probe()\n" + code + "\nEnd Sub",
            blocks: "Begin P.Dial uc1\nWidth = 2400\nHeight = 1800\nEnd\n");
        // The oracle calls the Public Sub after loading the form. Enter the same control context after load;
        // form-to-UserControl public-method dispatch is a separate, existing refusal in HexIDE.
        ((VBUserControl)run.Named("uc1")).ExecuteSub("Probe");
        return run;
    }
}
