using System;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A control where a value is wanted stands for its default member in VB6: <c>s = Text1</c> reads Text1.Text,
/// <c>v = Command1</c> its Value (measured, <c>docs/vb6-fidelity-oracle.md</c>, "A control where a value is wanted").
/// HexIDE has no default members yet (#790), and used to store the reference: a String took the .NET type name and
/// a Variant kept the control. Until #790 a Let of one is refused, and everything that does not read a default
/// member is left working.
/// </summary>
public class LetOfAReferenceTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Caption         =   "Form1"
           Begin VB.CheckBox Check1
              Caption         =   "Check1"
              Left            =   120
              Top             =   1800
              Width           =   1215
              Height          =   375
           End
           Begin VB.CommandButton Command1
              Caption         =   "Command1"
              Left            =   120
              Top             =   1200
              Width           =   1215
              Height          =   495
           End
           Begin VB.TextBox Text1
              Text            =   "42"
              Left            =   120
              Top             =   600
              Width           =   1215
              Height          =   375
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<System.Collections.Generic.List<Vb6Value>> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        return await ContainerRuntimeHarness.Run(ctx, env, code);
    }

    [AvaloniaTheory]
    [InlineData("Dim s As String\r\ns = Text1")]          // VB6: "42", its Text
    [InlineData("Dim v As Variant\r\nv = Text1")]         // VB6: "42", a String
    [InlineData("Dim v\r\nv = Command1")]                 // VB6: False, its Value
    [InlineData("Dim n As Long\r\nn = Check1")]           // VB6: 0, its Value
    [InlineData("Dim i As Integer\r\ni = Text1")]         // VB6: 42
    [InlineData("Dim d As Double\r\nd = Text1")]          // VB6: 42
    [InlineData("Dim b As Boolean\r\nb = Command1")]      // VB6: False
    [InlineData("x = Text1")]                             // an undeclared Variant
    [InlineData("Let x = Text1")]                         // Let written out
    [InlineData("Dim a(1)\r\na(0) = Text1")]              // an array element
    [InlineData("Dim v\r\nSet v = Text1\r\nDim s As String\r\ns = v")]   // a Variant holding the control
    public async Task A_Let_of_a_control_is_refused_until_default_members_are_implemented(string code)
    {
        // Under On Error Resume Next too: the refusal ends the run, rather than letting the next line see a value
        // that is not VB6's.
        var act = () => Run("On Error Resume Next\r\n" + code + "\r\n");

        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#790");
    }

    [AvaloniaFact]
    public async Task Set_still_keeps_the_reference()
    {
        var debug = await Run("Dim v\r\nSet v = Text1\r\nv.Text = \"changed\"\r\nDebug.Print Text1.Text\r\n");

        debug[0].Value.Should().Be("changed");
    }

    [AvaloniaFact]
    public async Task Naming_the_property_still_reads_it()
    {
        var debug = await Run("Dim s As String\r\ns = Text1.Text\r\nDebug.Print s\r\n");

        debug[0].Value.Should().Be("42");
    }

    [AvaloniaFact]
    public async Task For_Each_still_takes_each_control()
    {
        var debug = await Run(
            "Dim a(1)\r\nSet a(0) = Text1\r\nSet a(1) = Command1\r\n"
          + "Dim c, n\r\nFor Each c In a\r\nn = n + 1\r\nNext\r\nDebug.Print n\r\n");

        Convert.ToInt32(debug[0].Value).Should().Be(2);
    }

    [AvaloniaFact]
    public async Task A_control_passed_as_an_argument_still_arrives_as_the_control()
    {
        var debug = await Run(
            "Sub Rename(target)\r\n    target.Text = \"renamed\"\r\nEnd Sub\r\n\r\n"
          + "Rename Text1\r\nDebug.Print Text1.Text\r\n");

        debug[0].Value.Should().Be("renamed");
    }

    [AvaloniaTheory]
    [InlineData("If Check1 Then c = 1")]
    [InlineData("If Check1 Then\r\nc = 1\r\nEnd If")]
    [InlineData("Do While Check1\r\nc = 1\r\nExit Do\r\nLoop")]
    public async Task A_control_as_a_condition_is_refused_until_default_members_are_implemented(string code)
    {
        // VB6 reads the default member, Check1.Value. A condition used to give a trappable 13, which On Error Resume
        // Next stepped over, so the statement silently did nothing (#847's review).
        var act = () => Run("Dim c\r\nOn Error Resume Next\r\n" + code + "\r\n");

        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#790");
    }
}
