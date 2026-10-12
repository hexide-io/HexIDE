using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control converted to a number stands for its default member in VB6: measured, <c>CInt(Text1)</c> is 12 where
/// Text1 holds "12", and <c>RGB(Text1, 0, 0)</c> is 12. HexIDE has no default members for controls yet (#790), so it
/// refuses, untrappably, rather than read 0 or raise an error <c>On Error Resume Next</c> would step over.
/// </summary>
public class ControlAsNumberTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.TextBox Text1
              Text            =   "12"
              Left            =   120
              Top             =   120
              Width           =   1800
              Height          =   375
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaTheory]
    [InlineData("CInt(Text1)")]
    [InlineData("RGB(Text1, 0, 0)")]
    [InlineData("QBColor(Text1)")]
    [InlineData("MsgBox(\"probe\", Text1)")]
    public async Task A_control_converted_to_a_number_is_refused_naming_790(string expression)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var run = () => ContainerRuntimeHarness.Run(ctx, env, $"On Error Resume Next\r\nDim c\r\nc = {expression}\r\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#790*");
    }
}
