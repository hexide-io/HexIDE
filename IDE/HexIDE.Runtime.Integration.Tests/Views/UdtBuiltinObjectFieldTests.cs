using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// vb6.exe SP6 native/p-code, 2026-11-20: https://github.com/hexide-io/HexIDE/issues/3924, A.
public class UdtBuiltinObjectFieldTests
{
    private const string Form = "VERSION 5.00\nBegin VB.Form Form1\n Begin VB.TextBox Text1\n End\n Begin VB.TextBox Text2\n End\nEnd\nAttribute VB_Name = \"Form1\"\n";

    [AvaloniaTheory]
    [InlineData("StdFont")]
    [InlineData("TextBox")]
    [InlineData("Control")]
    [InlineData("Form")]
    [InlineData("StdPicture")]
    public async Task Fields_start_as_Nothing(string type)
    {
        await Check($"Private Type T\nObj As {type}\nEnd Type\nDim u As T\nDebug.Print u.Obj Is Nothing\n", "True");
    }

    [AvaloniaFact]
    public async Task Font_field_takes_Set_shares_on_copy_and_assigns_its_default()
    {
        await Check("""
            Private Type T
                f As StdFont
            End Type
            Dim u As T
            Dim w As T
            Set u.f = New StdFont
            u.f.Size = 12
            Debug.Print u.f.Size, TypeName(u.f)
            w = u
            Debug.Print w.f Is u.f
            w.f.Size = 20
            Debug.Print u.f.Size
            Set u.f = New StdFont
            u.f = "Arial"
            Debug.Print u.f.Name
            Set u.f = Nothing
            Debug.Print u.f Is Nothing
            On Error Resume Next
            u.f = "Arial"
            Debug.Print Err.Number
            """, "12", "Font", "True", "20.25", "Arial", "True", "91");
    }

    [AvaloniaFact]
    public async Task TextBox_Control_Form_and_Picture_fields_take_the_measured_objects()
    {
        await Check("""
            Private Type T
                t As TextBox
                c As Control
                fm As Form
                p As StdPicture
            End Type
            Dim u As T
            Set u.t = Form1.Text1
            u.t.Text = "x"
            Debug.Print Text1.Text
            u.t = "y"
            Debug.Print Text1.Text
            Set u.c = Form1.Text2
            Debug.Print TypeName(u.c)
            Set u.fm = Form1
            Debug.Print u.fm Is Form1, TypeName(u.fm)
            Set u.p = LoadPicture()
            Debug.Print TypeName(u.p)
            Set u.p = Form1.Icon
            Debug.Print u.p.Type
            Set u.t = Nothing
            Set u.c = Nothing
            Set u.fm = Nothing
            Set u.p = Nothing
            Debug.Print u.t Is Nothing, u.c Is Nothing, u.fm Is Nothing, u.p Is Nothing
            On Error Resume Next
            u.t = "y"
            Debug.Print Err.Number
            """, "x", "y", "TextBox", "True", "Form1", "Picture", "3", "True", "True", "True", "True", "91");
    }

    [AvaloniaTheory]
    [InlineData("StdFont", "New StdFont", "New Collection")]
    [InlineData("StdFont", "New StdFont", "Form1")]
    [InlineData("TextBox", "Form1.Text1", "Form1")]
    [InlineData("TextBox", "Form1.Text1", "New StdFont")]
    [InlineData("Control", "Form1.Text2", "Form1")]
    [InlineData("Control", "Form1.Text2", "New StdFont")]
    [InlineData("Form", "Form1", "Form1.Text1")]
    [InlineData("StdPicture", "LoadPicture()", "New Collection")]
    public async Task Wrong_type_is_13_and_preserves_the_field(string type, string original, string wrong)
    {
        await Check($"""
            Private Type T
                Obj As {type}
            End Type
            Dim u As T
            Dim held As Object
            Set held = {original}
            Set u.Obj = held
            On Error Resume Next
            Set u.Obj = {wrong}
            Debug.Print Err.Number, u.Obj Is held
            """, "13", "True");
    }

    private static async Task Check(string code, params string[] expected)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var values = new List<string?>();
        var interpreter = new BasicInterpreter(new Capture(values), ctx, env, code,
            primaryName: "Form1", primaryKind: InterpreterModuleKind.Form);
        var form = interpreter.NewFormInstance(interpreter.PrimaryModule);
        form.FormLoaded = true;
        var window = new Avalonia.Controls.Window();
        new HexIDE.Runtime.AvaloniaInterop.FormIcon(HexIDE.Runtime.AvaloniaInterop.FormIconKind.Default, null).Apply(window);
        form.FormWindow = window;
        ctx.AllocVariable(env, "Me", Vb6Value.NewObject(form));
        await interpreter.Execute();
        values.Should().Equal(expected);
    }

    private sealed class Capture(List<string?> values) : IBasicStandardLibrary
    {
        public void DebugPrint(Vb6Value value) => values.Add(System.Convert.ToString(value.Value, System.Globalization.CultureInfo.InvariantCulture));
        public Task<MessageBoxResult> MsgBox(string text, string? caption, MessageBoxButtons buttons, MessageBoxIcon icon) => Task.FromResult(default(MessageBoxResult));
        public Task<string?> InputBox(string prompt, string? title, string defaultText) => Task.FromResult<string?>(null);
    }
}
