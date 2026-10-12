using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Control, property, method and form names in any case, as VB6 accepts them (#811). Each row was measured on vb6.exe.
/// </summary>
public class IdentifierCaseFormTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.CommandButton Cmd
              Caption         =   "Cmd0"
              Index           =   0
              Left            =   2160
              Top             =   1200
              Width           =   1200
              Height          =   375
           End
           Begin VB.ListBox List1
              Left            =   120
              Top             =   1200
              Width           =   1800
              Height          =   840
           End
           Begin VB.TextBox Text1
              Text            =   "Text1"
              Left            =   120
              Top             =   120
              Width           =   1800
              Height          =   375
           End
           Begin VB.Label Label1
              Caption         =   "Label1"
              Left            =   2160
              Top             =   120
              Width           =   1200
              Height          =   375
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaTheory]
    [InlineData("text1.text = \"lower\"\r\nDebug.Print Text1.Text\r\n", "lower")]
    [InlineData("LABEL1.CAPTION = \"UP\"\r\nDebug.Print Label1.Caption\r\n", "UP")]
    [InlineData("cmd(0).caption = \"c0\"\r\nDebug.Print Cmd(0).Caption\r\n", "c0")]
    // Not here: `form1.caption` and `me.CAPTION`, measured too. Me and the form's own name do not resolve in form
    // code in any case yet; that is G2's "a real Me", and these rows belong with it.
    public async Task A_control_or_property_named_in_another_case_is_the_same_one(string code, string expected)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env, code);

        debug[0].Should().Be(new Vb6Value(expected));
    }

    [AvaloniaFact]
    public async Task A_control_method_named_in_another_case_is_called()
    {
        // Read from the list itself rather than ListCount, which HexIDE does not have yet (G18).
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        await ContainerRuntimeHarness.Run(ctx, env, "list1.additem \"a\"\r\nLIST1.ADDITEM \"b\"\r\n");

        ((Avalonia.Controls.ItemsControl)ContainerRuntimeHarness.Child(canvas, "List1")).Items.Cast<object?>()
            .Should().Equal("a", "b");
    }
}
