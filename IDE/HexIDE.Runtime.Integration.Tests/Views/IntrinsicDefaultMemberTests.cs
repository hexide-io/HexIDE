using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// Part of #790; measured in docs/vb6-fidelity-oracle/a-control-where-a-value-is-wanted-is-its-default-member.md,
// "Each intrinsic control's default member" (SP6, native and p-code).
public class IntrinsicDefaultMemberTests
{
    private static async Task<List<Vb6Value>> Run(
        string kind, string properties, string code)
    {
        return await Run($"Begin VB.{kind} Ctl\n{properties}\nEnd", code);
    }

    private static async Task<List<Vb6Value>> Run(string controls, string code)
    {
        var form = $"VERSION 5.00\nBegin VB.Form Form1\n{controls}\nEnd\nAttribute VB_Name = \"Form1\"\n";
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

    [AvaloniaFact]
    public async Task Debug_Print_refuses_an_unmodeled_default()
    {
        var act = () => Run("PictureBox", "", "Debug.Print Ctl");
        (await act.Should().ThrowAsync<System.NotImplementedException>()).WithMessage("*#790*");
    }

    [AvaloniaFact]
    public async Task A_control_on_the_left_of_concatenation_uses_its_default()
    {
        var values = await Run("Label", "Caption = \"L1\"", "Debug.Print Ctl & \"\"");
        values.Should().ContainSingle().Which.Value.Should().Be("L1");
    }

    [AvaloniaTheory]
    [InlineData("? Ctl", "L1")]
    [InlineData("? Ctl; Ctl", "L1L1")]
    public async Task Immediate_print_reads_control_defaults(string line, string expected)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(
            "VERSION 5.00\nBegin VB.Form Form1\nBegin VB.Label Ctl\nCaption = \"L1\"\nEnd\nEnd\nAttribute VB_Name = \"Form1\"\n"
                .ReplaceLineEndings("\r\n"));
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
