using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Part of #790; measured in docs/vb6-fidelity-oracle/a-control-where-a-value-is-wanted-is-its-default-member.md,
// "Each intrinsic control's default member" (SP6, native and p-code).
public class IntrinsicDefaultMemberTests
{
    private const string PictureBoxControl = "Begin VB.PictureBox Picture1\nEnd";

    private static async Task<List<Vb6Value>> Run(
        string kind, string properties, string code)
    {
        return await Run($"Begin VB.{kind} Ctl\n{properties}\nEnd", code);
    }

    private static async Task<List<Vb6Value>> Run(string controls, string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithControls(controls));
        return await ContainerRuntimeHarness.Run(ctx, env, code);
    }

    private static string FormWithControls(string controls) =>
        $"VERSION 5.00\nBegin VB.Form Form1\n{controls}\nEnd\nAttribute VB_Name = \"Form1\"\n".ReplaceLineEndings("\r\n");

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

    // VB6 SP6 native and p-code, #5504's 2026-11-23 measurements (oracle PR #5535).
    // Mod follows p-code where native code disagrees.
    [AvaloniaTheory]
    [InlineData("TextBox", "12", "Ctl + 2", "Double", 14d, 0)]
    [InlineData("TextBox", "12", "Ctl + \"1\"", "String", "121", 0)]
    [InlineData("TextBox", "12", "2 \\ Ctl", "Long", 0L, 0)]
    [InlineData("TextBox", "12", "Not Ctl", "Long", -13L, 0)]
    [InlineData("TextBox", "12", "Ctl > 2", "Boolean", true, 0)]
    [InlineData("TextBox", "12", "Ctl Like \"1*\"", "Boolean", true, 0)]
    [InlineData("TextBox", "T1", "Ctl + 2", "Empty", null, 13)]
    [InlineData("TextBox", "T1", "Ctl + \"1\"", "String", "T11", 0)]
    [InlineData("TextBox", "T1", "Ctl = 2", "Empty", null, 13)]
    [InlineData("TextBox", "T1", "o = 2", "Boolean", false, 0)]
    [InlineData("TextBox", "12", "2 * o", "Double", 24d, 0)]
    [InlineData("Label", "", "Ctl + 2", "Double", 5d, 0)]
    [InlineData("CommandButton", "", "Ctl + 2", "Integer", (short)2, 0)]
    [InlineData("CommandButton", "", "Not Ctl", "Boolean", true, 0)]
    [InlineData("CommandButton", "", "2 / Ctl", "Empty", null, 11)]
    [InlineData("CommandButton", "", "Ctl Eqv 2", "Integer", (short)-3, 0)]
    [InlineData("CheckBox", "", "Ctl + 2", "Integer", (short)3, 0)]
    [InlineData("CheckBox", "", "Not Ctl", "Integer", (short)-2, 0)]
    [InlineData("CheckBox", "", "Ctl Xor 2", "Integer", (short)3, 0)]
    [InlineData("CheckBox", "", "2 Imp Ctl", "Integer", (short)-3, 0)]
    [InlineData("CheckBox", "", "o + 2", "Integer", (short)3, 0)]
    [InlineData("OptionButton", "", "Ctl + 2", "Integer", (short)1, 0)]
    [InlineData("OptionButton", "", "Ctl Mod 2", "Integer", (short)-1, 0)]
    [InlineData("ListBox", "", "Ctl + 2", "Double", 7d, 0)]
    [InlineData("ComboBox", "", "Ctl + 2", "Double", 9d, 0)]
    [InlineData("HScrollBar", "", "Ctl + 2", "Integer", (short)-3, 0)]
    [InlineData("HScrollBar", "", "Ctl Mod 2", "Integer", (short)-1, 0)]
    [InlineData("HScrollBar", "", "-Ctl", "Integer", (short)5, 0)]
    [InlineData("VScrollBar", "", "Ctl + 2", "Integer", (short)10, 0)]
    [InlineData("Shape", "", "Ctl + 2", "Integer", (short)5, 0)]
    public async Task Control_operand_uses_its_default(string kind, string text, string expression,
        string type, object? expected, int error)
    {
        var properties = kind switch
        {
            "TextBox" => $"Text = \"{text}\"",
            "Label" => "Caption = \"3\"",
            "HScrollBar" => "Min = -10\nMax = 10",
            "VScrollBar" => "Max = 100",
            _ => "",
        };
        var setup = kind switch
        {
            "CheckBox" => "Ctl.Value = 1",
            "OptionButton" => "Ctl.Value = True",
            "ListBox" => "Ctl.AddItem \"5\"\nCtl.AddItem \"6\"\nCtl.ListIndex = 0",
            "ComboBox" => "Ctl.Text = \"7\"",
            "HScrollBar" => "Ctl.Value = -5",
            "VScrollBar" => "Ctl.Value = 8",
            "Shape" => "Ctl.Shape = 3",
            _ => "",
        };
        var values = await Run(kind, properties,
            $"{setup}\nDim o As Object\nSet o = Ctl\n{OperandResult(expression)}");
        values.ConvertAll(v => v.Value).Should().Equal(type, expected, error);
    }

    private static string OperandResult(string expression) =>
        $"On Error Resume Next\nDim v As Variant\nv = {expression}\nDebug.Print TypeName(v)\nDebug.Print v\nDebug.Print Err.Number";

    public static IEnumerable<object[]> BinaryControlOperandCases()
    {
        foreach (var op in new[] { "+", "-", "*", "/", "\\", "Mod", "^", "=", "<>", "<", ">", "<=", ">=", "And", "Or", "Xor", "Eqv", "Imp" })
        {
            yield return [$"Ctl {op} 2"];
            yield return [$"2 {op} Ctl"];
        }
        yield return ["\"12\" Like Ctl"];
    }

    [AvaloniaTheory]
    [MemberData(nameof(BinaryControlOperandCases))]
    public async Task Control_operand_matches_the_written_member_on_either_side(string expression)
    {
        // All 614 measured expressions match the member written out (#5504).
        var actual = await Run("TextBox", "Text = \"12\"", OperandResult(expression));
        var explicitMember = await Run("TextBox", "Text = \"12\"", OperandResult(expression.Replace("Ctl", "Ctl.Text")));
        actual.ConvertAll(v => v.Value).Should().Equal(explicitMember.ConvertAll(v => v.Value));
    }

    [AvaloniaTheory]
    [InlineData("Text1 + Label1", "String", "123")]
    [InlineData("Check1 + HScroll1", "Integer", (short)-4)]
    [InlineData("Text1 + Check1", "Double", 13d)]
    public async Task Two_control_operands_read_each_default(string expression, string type, object expected)
    {
        var values = await Run("""
            Begin VB.TextBox Text1
            Text = "12"
            End
            Begin VB.Label Label1
            Caption = "3"
            End
            Begin VB.CheckBox Check1
            End
            Begin VB.HScrollBar HScroll1
            Min = -10
            Max = 10
            End
            """, $"Check1.Value = 1\nHScroll1.Value = -5\n{OperandResult(expression)}");
        values.ConvertAll(v => v.Value).Should().Equal(type, expected, 0);
    }

    [AvaloniaFact]
    public async Task Frame_operand_matches_its_Caption()
    {
        var actual = await Run("Frame", "Caption = \"4\"", OperandResult("Ctl + 2"));
        var explicitMember = await Run("Frame", "Caption = \"4\"", OperandResult("Ctl.Caption + 2"));
        actual.ConvertAll(v => v.Value).Should().Equal(explicitMember.ConvertAll(v => v.Value));
    }

    [AvaloniaTheory]
    [InlineData("Object")]
    [InlineData("Control")]
    [InlineData("Variant")]
    public async Task Late_bound_control_operand_compares_as_a_Variant(string declaration)
    {
        var values = await Run("TextBox", "Text = \"T1\"",
            $"Dim o As {declaration}\nSet o = Ctl\n{OperandResult("o = 2")}");
        values.ConvertAll(v => v.Value).Should().Equal("Boolean", false, 0);
    }

    // #5549, a-control-as-an-operand.md and #5504's first comment (SP6, native and p-code).
    [AvaloniaTheory]
    [InlineData("Picture1 + 2", "Long", 2L, 0)]
    [InlineData("Not Picture1", "Long", -1L, 0)]
    [InlineData("Picture1 Xor 2", "Long", 2L, 0)]
    [InlineData("Picture1 Eqv 2", "Long", -3L, 0)]
    [InlineData("Picture1 Imp 2", "Long", -1L, 0)]
    [InlineData("Picture1 = 0", "Boolean", true, 0)]
    [InlineData("2 / Picture1", "Empty", null, 11)]
    [InlineData("2 \\ Picture1", "Empty", null, 11)]
    [InlineData("2 Mod Picture1", "Empty", null, 11)]
    [InlineData("Picture1 & \"\"", "String", "0", 0)]
    public async Task Empty_PictureBox_operand_has_measured_result(string expression, string type, object? expected, int error)
    {
        var values = await Run(PictureBoxControl, OperandResult(expression));
        values.ConvertAll(v => v.Value).Should().Equal(type, expected, error);
    }

    public static IEnumerable<object[]> PictureBoxOperandCases()
    {
        foreach (var row in BinaryControlOperandCases())
            yield return [((string)row[0]).Replace("Ctl", "Picture1")];
        yield return ["Not Picture1"];
        yield return ["-Picture1"];
        yield return ["Picture1 + \"1\""];
        yield return ["Picture1 Like \"1*\""];
    }

    [AvaloniaTheory]
    [MemberData(nameof(PictureBoxOperandCases))]
    public async Task Empty_PictureBox_operand_matches_a_Long_zero(string expression)
    {
        var actual = await Run(PictureBoxControl, OperandResult(expression));
        var handle = await Run(PictureBoxControl, OperandResult(expression.Replace("Picture1", "CLng(0)")));
        actual.ConvertAll(v => v.Value).Should().Equal(handle.ConvertAll(v => v.Value));
    }

    [AvaloniaTheory]
    [InlineData("o + 2", "Picture1 + 2")]
    [InlineData("2 * o", "2 * Picture1")]
    [InlineData("Not o", "Not Picture1")]
    [InlineData("o = 2", "Picture1 = 2")]
    public async Task Empty_PictureBox_operand_through_Object_matches_early_bound(string expression, string earlyBoundExpression)
    {
        var actual = await Run(PictureBoxControl, $"Dim o As Object\nSet o = Picture1\n{OperandResult(expression)}");
        var earlyBound = await Run(PictureBoxControl, OperandResult(earlyBoundExpression));
        actual.ConvertAll(v => v.Value).Should().Equal(earlyBound.ConvertAll(v => v.Value));
    }

    // #5549, a-control-where-a-value-is-wanted-is-its-default-member.md (SP6, native and p-code).
    [AvaloniaFact]
    public async Task Let_reads_an_empty_PictureBox_as_Long_zero()
    {
        var values = await Run(PictureBoxControl, "Dim v\nv = Picture1\nDebug.Print TypeName(v)\nDebug.Print v");
        values.ConvertAll(v => v.Value).Should().Equal("Long", 0L);
    }

    [AvaloniaFact]
    public async Task PictureBox_operand_refuses_its_unmodeled_handle()
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithControls(PictureBoxControl));
        var box = (VBPictureBox)ContainerRuntimeHarness.Child(canvas, "Picture1");
        box.PictureData = PictureBoxAutoSizeTests.Bitmap();
        box.Picture.Should().NotBeNull();
        var library = Substitute.For<IBasicStandardLibrary>();
        var act = () => new BasicInterpreter(library, ctx, env, OperandResult("Picture1 + 2")).Execute();
        (await act.Should().ThrowAsync<System.NotImplementedException>()).WithMessage("*`+`*#790*");
        library.DidNotReceiveWithAnyArgs().DebugPrint(default);
    }

    // VB6 SP6 native and p-code, issue #5385 (2026-11-23, oracle PR #5498).
    public static IEnumerable<object[]> ControlOutputCases()
    {
        (string Kind, string Setup, string Print, string Write, string? Join)[] rows =
        [
            ("TextBox", "Ctl.Text = \"T1\"", "T1", "\"T1\"", "T1"),
            ("Label", "Ctl.Caption = \"L1\"", "L1", "\"L1\"", "L1"),
            ("Frame", "Ctl.Caption = \"F1\"", "F1", "\"F1\"", "F1"),
            ("ComboBox", "Ctl.Text = \"K1\"", "K1", "\"K1\"", "K1"),
            ("ListBox", "", "", "\"\"", ""),
            ("ListBox", "Ctl.AddItem \"a\"\nCtl.AddItem \"b\"\nCtl.ListIndex = 1", "b", "\"b\"", null),
            ("CommandButton", "", "False", "#FALSE#", "False"),
            ("OptionButton", "Ctl.Value = True", "True", "#TRUE#", "True"),
            ("CheckBox", "Ctl.Value = 1", " 1 ", "\"1\"", "1"),
            ("HScrollBar", "Ctl.Value = 5", " 5 ", "\"5\"", "5"),
            ("HScrollBar", "Ctl.Value = -5", "-5 ", "\"-5\"", "-5"),
            ("VScrollBar", "Ctl.Value = 7", " 7 ", "\"7\"", "7"),
            ("Shape", "Ctl.Shape = 3", " 3 ", "\"3\"", "3"),
        ];
        foreach (var row in rows)
        foreach (var reference in new[] { "Ctl", "Object" })
        {
            yield return [row.Kind, row.Setup, reference, "Debug.Print", row.Print];
            yield return [row.Kind, row.Setup, reference, "Print #", row.Print];
            yield return [row.Kind, row.Setup, reference, "Write #", row.Write];
            if (row.Join is not null)
                yield return [row.Kind, row.Setup, reference, "&", row.Join];
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(ControlOutputCases))]
    public async Task Control_output_uses_its_default(string kind, string setup, string reference, string operation, string expected)
    {
        var properties = kind == "HScrollBar" ? "Min = -10" : "";
        var controls = $"Begin VB.{kind} Ctl\n{properties}\nEnd";
        var item = "Ctl";
        if (reference == "Object")
        {
            setup += "\nDim o As Object\nSet o = Ctl";
            item = "o";
        }
        if (operation is "Print #" or "Write #")
        {
            await AssertFileOutput(controls, setup, $"{operation}1, {item}", expected);
            return;
        }

        var expression = operation == "&" ? $"\"\" & {item}" : item;
        var values = await Run(controls, $"{setup}\nDebug.Print {expression}");
        var value = values.Should().ContainSingle().Which;
        if (operation == "&")
            value.Value.Should().Be(expected);
        else
            VbPrint.ItemText(value).Should().Be(expected);
    }

    [AvaloniaTheory]
    [InlineData("Label1; Text1", "L1T1")]
    [InlineData("Check1; Option1; HScroll1; Shape1", " 1 True 5  3 ")]
    public async Task Print_list_uses_each_default(string items, string expected)
    {
        await AssertFileOutput("""
            Begin VB.Label Label1
            Caption = "L1"
            End
            Begin VB.TextBox Text1
            Text = "T1"
            End
            Begin VB.CheckBox Check1
            End
            Begin VB.OptionButton Option1
            End
            Begin VB.HScrollBar HScroll1
            End
            Begin VB.Shape Shape1
            End
            """, "Check1.Value = 1\nOption1.Value = True\nHScroll1.Value = 5\nShape1.Shape = 3",
            $"Print #1, {items}", expected);
    }

    [AvaloniaTheory]
    [InlineData("Dim v As Variant\nSet v = Ctl", "v", "\"0\"")]
    [InlineData("Dim c As Control\nSet c = Ctl", "c", "\"0\"")]
    [InlineData("", "Ctl.Value", "0")]
    [InlineData("Dim v As Variant\nv = Ctl", "v", "0")]
    [InlineData("Dim i As Integer\ni = Ctl", "i", "0")]
    public async Task Write_distinguishes_control_references_from_numbers(string setup, string item, string expected)
    {
        await AssertFileOutput("Begin VB.CheckBox Ctl\nEnd", setup, $"Write #1, {item}", expected);
    }

    // #5549, the first form in a-control-where-a-value-is-wanted-is-its-default-member.md.
    [AvaloniaTheory]
    [InlineData("Print #1, Command1; Check1; Picture1", "False 0  0 ")]
    [InlineData("Write #1, Label1, Command1, Check1, Picture1", "\"L1\",#FALSE#,\"0\",\"0\"")]
    public async Task File_output_reads_the_empty_PictureBox_in_a_control_list(string statement, string expected)
    {
        await AssertFileOutput("""
            Begin VB.Label Label1
            End
            Begin VB.CommandButton Command1
            End
            Begin VB.CheckBox Check1
            End
            Begin VB.PictureBox Picture1
            End
            """, "Label1.Caption = \"L1\"", statement, expected);
    }

    [AvaloniaTheory]
    [InlineData("Dim c As Control", "c")]
    [InlineData("Dim o As Object", "o")]
    [InlineData("Dim v", "v")]
    public async Task Print_file_reads_an_empty_PictureBox_through_a_late_bound_reference(string declaration, string name)
    {
        await AssertFileOutput(PictureBoxControl, $"{declaration}\nSet {name} = Picture1", $"Print #1, {name}", " 0 ");
    }

    // #5549 also shares the new default with Debug.Print and the paused Immediate window.
    [AvaloniaTheory]
    [InlineData("PictureBox", false)]
    [InlineData("Object", true)]
    [InlineData("Control", true)]
    [InlineData("Variant", true)]
    public async Task Debug_Print_reads_the_PictureBox_default_with_its_binding(string declaration, bool lateBound)
    {
        var values = await Run(PictureBoxControl, $"Dim p As {declaration}\nSet p = Picture1\nDebug.Print p");
        var value = values.Should().ContainSingle().Which;
        value.Type.Should().Be(Vb6Value.ValueType.Long);
        value.Value.Should().Be(0L);
        value.HeldAsVariant.Should().Be(lateBound);
    }

    [AvaloniaFact]
    public async Task Debug_Print_refuses_an_unmodeled_default()
    {
        var act = () => Run("Image", "", "Debug.Print Ctl");
        (await act.Should().ThrowAsync<System.NotImplementedException>()).WithMessage("*#790*");
    }

    [AvaloniaFact]
    public async Task A_control_on_the_left_of_concatenation_uses_its_default()
    {
        var values = await Run("Label", "Caption = \"L1\"", "Debug.Print Ctl & \"\"");
        values.Should().ContainSingle().Which.Value.Should().Be("L1");
    }

    [AvaloniaTheory]
    [InlineData("Begin VB.Label Ctl\nCaption = \"L1\"\nEnd", "? Ctl", "L1")]
    [InlineData("Begin VB.Label Ctl\nCaption = \"L1\"\nEnd", "? Ctl; Ctl", "L1L1")]
    [InlineData(PictureBoxControl, "? Picture1", " 0 ")]
    public async Task Immediate_print_reads_control_defaults(string controls, string line, string expected)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(FormWithControls(controls));
        var debugger = new DebugController();
        var interpreter = new BasicInterpreter(Substitute.For<IBasicStandardLibrary>(), ctx, env, "Stop")
            { DebugController = debugger };
        var stopped = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        debugger.Stopped += _ => stopped.TrySetResult();
        var run = interpreter.Execute();
        try
        {
            await stopped.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            (await debugger.EvaluateAsync(line)).Should().Be(expected);
        }
        finally
        {
            debugger.Stop();
            await run.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        }
    }

    private static async Task AssertFileOutput(string controls, string setup, string statement, string expected)
    {
        var path = Path.GetTempFileName();
        try
        {
            await Run(controls, $"{setup}\nOpen \"{path}\" For Output As #1\n{statement}\nClose #1");
            File.ReadAllBytes(path).Should().Equal(Encoding.ASCII.GetBytes(expected + "\r\n"));
        }
        finally
        {
            File.Delete(path);
        }
    }
}
