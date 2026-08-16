using Avalonia.Controls;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Tests;

/// <summary>
/// Regression for the adversarial bug-hunt HIGH: assigning an Integer to a double-typed control property
/// (Left/Top/Width/Height) threw "Invalid value type" and was silently swallowed, so `Command1.Left = 100` never
/// moved the control. VB6 coerces any numeric to a numeric property; AvaloniaInteroperability now re-boxes across
/// numeric CLR types before its exact type check.
/// </summary>
public class ControlPropertyCoercionTests
{
    public ControlPropertyCoercionTests() => AvaloniaTestSetup.EnsureInitialized();

    [Theory]
    [InlineData(100)]        // the ubiquitous Integer literal case
    [InlineData(30000)]      // still Integer
    [InlineData(40000)]      // a Long (magnitude > Int16)
    public void SetLeft_WithIntegerValue_CoercesToDoubleAndApplies(int value)
    {
        var control = new Control();

        AvaloniaInteroperability.TrySet(control, VBProperties.LeftProperty, new Vb6Value(value)).Should().BeTrue();

        AvaloniaInteroperability.TryGet(control, VBProperties.LeftProperty, out var read).Should().BeTrue();
        read.Should().Match<Vb6Value>(v => System.Convert.ToDouble(v.Value) == value);
    }

    [Fact]
    public void SetWidth_WithSingleValue_CoercesToDouble()
    {
        var control = new Control();

        AvaloniaInteroperability.TrySet(control, VBProperties.WidthProperty, new Vb6Value(3000.0f)).Should().BeTrue();

        AvaloniaInteroperability.TryGet(control, VBProperties.WidthProperty, out var read).Should().BeTrue();
        System.Convert.ToDouble(read.Value).Should().Be(3000.0);
    }
}
