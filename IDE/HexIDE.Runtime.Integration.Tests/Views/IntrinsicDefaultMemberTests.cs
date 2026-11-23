using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Part of #790; measured in docs/vb6-fidelity-oracle/a-control-where-a-value-is-wanted-is-its-default-member.md,
// "Each intrinsic control's default member" (SP6, native and p-code).
public class IntrinsicDefaultMemberTests
{
    private static async Task<System.Collections.Generic.List<HexIDE.Runtime.Interpreter.Vb6Value>> Run(
        string kind, string properties, string code)
    {
        var form = $"VERSION 5.00\nBegin VB.Form Form1\nBegin VB.{kind} Ctl\n{properties}\nEnd\nEnd\nAttribute VB_Name = \"Form1\"\n";
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form.ReplaceLineEndings("\r\n"));
        return await ContainerRuntimeHarness.Run(ctx, env, code);
    }

    [AvaloniaTheory]
    [InlineData("Label", "Caption = \"L1\"", "L1", "String")]
    [InlineData("Frame", "Caption = \"F1\"", "F1", "String")]
    [InlineData("CommandButton", "", false, "Boolean")]
    [InlineData("CheckBox", "", (short)0, "Integer")]
    [InlineData("OptionButton", "", false, "Boolean")]
    [InlineData("ListBox", "", "", "String")]
    [InlineData("ComboBox", "Text = \"B1\"\nStyle = 0", "B1", "String")]
    [InlineData("ComboBox", "Style = 2", "", "String")]
    [InlineData("HScrollBar", "", (short)0, "Integer")]
    [InlineData("VScrollBar", "", (short)0, "Integer")]
    [InlineData("Shape", "", (short)0, "Integer")]
    public async Task Reads_into_Variant_and_String(string kind, string properties, object expected, string type)
    {
        var values = await Run(kind, properties,
            "Dim v As Variant\nv = Ctl\nDebug.Print v\nDebug.Print TypeName(v)\nDim s As String\ns = Ctl\nDebug.Print s");
        values[0].Value.Should().Be(expected);
        values[1].Value.Should().Be(type);
        values[2].Value.Should().Be(expected.ToString());
    }

    [AvaloniaTheory]
    [InlineData("TextBox", "Ctl = \"y\"", "Text", "y")]
    [InlineData("Label", "Ctl = \"x\"", "Caption", "x")]
    [InlineData("Frame", "Ctl = \"f\"", "Caption", "f")]
    [InlineData("CommandButton", "Ctl = True", "Value", false)]
    [InlineData("CheckBox", "Ctl = 1", "Value", (short)1)]
    [InlineData("OptionButton", "Ctl = True", "Value", true)]
    [InlineData("ListBox", "Ctl.AddItem \"a\"\nCtl.AddItem \"b\"\nCtl = \"b\"", "Text", "b")]
    [InlineData("ComboBox", "Ctl = \"z\"", "Text", "z")]
    [InlineData("HScrollBar", "Ctl = 5", "Value", (short)5)]
    [InlineData("Shape", "Dim o As Object\nSet o = Ctl\no = 3", "Shape", (short)3)]
    public async Task Writes_the_named_property(string kind, string code, string property, object expected)
    {
        var values = await Run(kind, "", code + "\nDebug.Print Ctl." + property
            + (kind == "ListBox" ? "\nDebug.Print Ctl.ListIndex" : ""));
        values[0].Value.Should().Be(expected);
        if (kind == "ListBox") values[1].Value.Should().Be((short)1);
    }

    [AvaloniaTheory]
    [InlineData("Timer", "Dim v\nv = Ctl")]
    [InlineData("Line", "Dim v\nv = Ctl")]
    [InlineData("PictureBox", "Dim v\nv = Ctl")]
    [InlineData("Image", "Dim v\nv = Ctl")]
    [InlineData("Shape", "Ctl = 3")]
    [InlineData("Timer", "Ctl = False")]
    public async Task Out_of_scope_defaults_remain_refused(string kind, string code)
    {
        var act = () => Run(kind, "", code);
        (await act.Should().ThrowAsync<System.NotImplementedException>()).WithMessage("*#790*");
    }

    [AvaloniaFact]
    public async Task TextBox_reads_already_work()
    {
        var values = await Run("TextBox", "Text = \"T1\"",
            "Dim v As Variant\nv = Ctl\nDebug.Print v\nDebug.Print TypeName(v)\nDim s As String\ns = Ctl\nDebug.Print s");
        values.ConvertAll(v => v.Value).Should().Equal("T1", "String", "T1");
    }

    [AvaloniaFact]
    public async Task Set_keeps_a_Label_reference()
    {
        var values = await Run("Label", "", "Dim v\nSet v = Ctl\nv.Caption = \"x\"\nDebug.Print Ctl.Caption");
        values.Should().ContainSingle().Which.Value.Should().Be("x");
    }
}
