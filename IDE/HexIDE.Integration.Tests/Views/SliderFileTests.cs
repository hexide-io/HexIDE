using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;

namespace HexIDE.Integration.Tests.Views;

public class SliderFileTests
{
    [AvaloniaTheory]
    [InlineData("Value = 30\nMax = 50", "0", "50", "30", "30", "0", "0")]
    [InlineData("Max = 50\nValue = 99", "0", "50", "50", "50", "0", "0")]
    [InlineData("Min = 10\nMax = 10", "10", "10", "10", "10", "0", "0")]
    [InlineData("Min = 20", "20", "10", "20", "20", "0", "0")]
    [InlineData("Value = 6\nSelStart = 2\nSelLength = 3", "0", "10", "6", "6", "0", "0")]
    [InlineData("SelectRange = -1\nSelStart = 8\nSelLength = 10", "0", "10", "0", "8", "2", "0")]
    [InlineData("TickStyle = 7", "0", "10", "0", "0", "0", "0")]
    [InlineData("Orientation = 1\nText = \"ignored\"", "0", "10", "0", "0", "0", "0")]
    public async Task File_properties_use_the_measured_load_rules(string properties, params string[] expected)
    {
        var form = $"VERSION 5.00\nBegin VB.Form Form1\nBegin MSComctlLib.Slider Slider1\nWidth = 4500\nHeight = 675\n{properties}\nEnd\nEnd\nAttribute VB_Name = \"Form1\"\n";
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env,
            "Debug.Print Slider1.Min, Slider1.Max, Slider1.Value, Slider1.SelStart, Slider1.SelLength, Slider1.TickStyle\r\nDebug.Print Slider1.Text, Slider1.Width, Slider1.Height");
        printed.Select(v => Convert.ToString(v.Value, System.Globalization.CultureInfo.InvariantCulture) ?? "")
            .Should().Equal(expected.Concat(new[] { "", "4500", "675" }));
    }
}
