using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Integration.Tests;
using HexIDE.Integration.Tests.Views;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Integration.Tests.Views;

// VB6 SP6 p-code/native measurements: https://github.com/hexide-io/HexIDE/issues/4604
// (oracle measuring PR #4613). Only Label Alignment is measured here.
public class LabelAlignmentTests : WindowTestFixture
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.Label Label1
              Caption = "Caption"
              Width = 2400
              Height = 375
           End
        End
        Attribute VB_Name = "Form1"
        """;

    [AvaloniaFact]
    public async Task Default_alignment_reads_as_Integer_zero()
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env,
            "Debug.Print TypeName(Label1.Alignment)\r\nDebug.Print Label1.Alignment\r\n");

        debug[0].Value.Should().Be("Integer");
        debug[1].Should().Be(new Vb6Value((short)0));
    }

    [AvaloniaTheory]
    [InlineData("2", 2)]
    [InlineData("vbRightJustify", 1)]
    [InlineData("\"2\"", 2)]
    [InlineData("0.5", 0)]
    [InlineData("1.5", 2)]
    [InlineData("2.5", 2)]
    [InlineData("-0.5", 0)]
    public async Task Assignment_converts_to_Integer_before_checking_the_range(string value, short expected)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env,
            $"Label1.Alignment = {value}\r\nDebug.Print Label1.Alignment\r\n");

        debug.Single().Should().Be(new Vb6Value(expected));
    }

    [AvaloniaTheory]
    [InlineData("3")]
    [InlineData("-1")]
    [InlineData("2.6")]
    public async Task Invalid_alignment_raises_380_and_keeps_the_old_value(string value)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);

        var debug = await ContainerRuntimeHarness.Run(ctx, env,
            $"Label1.Alignment = 1\r\nOn Error Resume Next\r\nLabel1.Alignment = {value}\r\n" +
            "Debug.Print Err.Number\r\nDebug.Print Err.Description\r\nDebug.Print Label1.Alignment\r\n");

        debug[0].Value.Should().Be(380);
        debug[1].Value.Should().Be("Invalid property value");
        debug[2].Should().Be(new Vb6Value((short)1));
    }
}
