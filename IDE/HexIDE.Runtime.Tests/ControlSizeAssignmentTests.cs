using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Measured: #4071's table, oracle PRs #4069 and #4095 (native and p-code agree).
// Error 380's English description: docs/vb6-fidelity-oracle/strcomp-strconv-environ-switch-and-error.md.
public class ControlSizeAssignmentTests : BaseVBTestFixture
{
    private void Controls(double width = 81, double height = 33)
    {
        var canvas = new Canvas();
        foreach (var (name, control) in new (string, Control)[] {
            ("Command1", new VBCommandButton()), ("Text1", new VBTextBox()),
            ("Label1", new VBLabel()), ("Line1", new VBLine(0, 0, 0, 0)) })
        {
            control.Width = width;
            control.Height = height;
            canvas.Children.Add(control);
            if (control is VBLine line) line.Resolve((1, 1));
            context.AllocVariable(rootEnv, name, new Vb6Value(control));
        }
    }

    [Theory]
    [InlineData("Command1", "Width")]
    [InlineData("Command1", "Height")]
    [InlineData("Label1", "Width")]
    [InlineData("Label1", "Height")]
    public async Task Negative_property_is_380_and_preserves_size(string name, string dimension)
    {
        Controls();
        await Run($"""
            Dim before As Single
            before = {name}.{dimension}
            On Error Resume Next
            {name}.{dimension} = -10
            Debug.Print Err.Number, Err.Description, {name}.{dimension} = before
            """);
        AssertDebugLog([new(380L), new("Invalid property value"), new(true)]);
    }

    [Theory]
    [InlineData("Command1", "-10")]
    [InlineData("Command1", "1215, -10")]
    [InlineData("Label1", "-10")]
    [InlineData("Label1", "1215, -10")]
    public async Task Negative_Move_is_5_and_changes_nothing(string name, string sizes)
    {
        Controls();
        await Run($"""
            {name}.Left = 120
            {name}.Top = 240
            On Error Resume Next
            {name}.Move 0, 0, {sizes}
            Debug.Print Err.Number, {name}.Left, {name}.Top, {name}.Width, {name}.Height
            """);
        AssertDebugLog([new(5L), new(120f), new(240f), new(1215f), new(495f)]);
    }

    [Theory]
    [InlineData("Command1", "Width", 67, 75)]
    [InlineData("Command1", "Width", 68, 68)]
    [InlineData("Command1", "Height", 187, 195)]
    [InlineData("Command1", "Height", 188, 188)]
    [InlineData("Label1", "Width", 7, 15)]
    [InlineData("Label1", "Width", 8, 8)]
    [InlineData("Label1", "Height", 7, 15)]
    [InlineData("Label1", "Height", 8, 8)]
    [InlineData("Text1", "Width", 0, 150)]
    [InlineData("Text1", "Height", 0, 285)]
    public async Task Minimum_compares_rounded_pixels_and_keeps_other_twips(string name, string dimension, int value, int expected)
    {
        Controls();
        await Run($"{name}.{dimension} = {value}\nDebug.Print {name}.{dimension}");
        AssertDebugLog([new((float)expected)]);
    }

    [Theory]
    [InlineData("Width")]
    [InlineData("Height")]
    public async Task Either_assignment_enforces_both_dimensions(string dimension)
    {
        Controls(0, 0);
        await Run($"Command1.{dimension} = 0\nDebug.Print Command1.Width, Command1.Height");
        AssertDebugLog([new(75f), new(195f)]);
    }

    [Fact]
    public async Task Move_zero_enforces_both_dimensions()
    {
        Controls();
        await Run("Command1.Move 0, 0, 0, 0\nDebug.Print Command1.Width, Command1.Height");
        AssertDebugLog([new(75f), new(195f)]);
    }

    [Fact]
    public async Task Negative_positions_and_line_coordinates_are_unrestricted()
    {
        Controls();
        await Run("Command1.Left = -50\nCommand1.Top = -50\nLine1.X2 = -10\nDebug.Print Command1.Left, Command1.Top, Line1.X2");
        AssertDebugLog([new(-50f), new(-50f), new(-10f)]);
    }
}
