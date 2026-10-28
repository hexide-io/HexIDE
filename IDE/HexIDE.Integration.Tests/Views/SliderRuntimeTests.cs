using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;

namespace HexIDE.Integration.Tests.Views;

public class SliderRuntimeTests
{
    private const string Form = """
            VERSION 5.00
            Begin VB.Form Form1
               Begin MSComctlLib.Slider Slider1
                  Height = 400
                  Width = 3000
               End
            End
            Attribute VB_Name = "Form1"
            """;

    private static async Task<string[]> Run(string code)
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\r\n" + code);
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public async Task Defaults_and_default_member_are_measured()
    {
        (await Run("""
            Dim v
            v = Slider1
            Debug.Print TypeName(Slider1), v, TypeName(v)
            Debug.Print TypeOf Slider1 Is MSComctlLib.Slider, TypeOf Slider1 Is Slider
            Debug.Print Slider1.Min, Slider1.Max, Slider1.SmallChange, Slider1.LargeChange
            Debug.Print Slider1.TickStyle, Slider1.TickFrequency, Slider1.GetNumTicks
            Debug.Print Slider1.Orientation, Slider1.TextPosition, Slider1.BorderStyle, Slider1.MousePointer, Slider1.OLEDropMode
            Debug.Print Slider1.SelectRange, Slider1.SelStart, Slider1.SelLength, Slider1.Text, Slider1.Enabled, Slider1.TabStop
            Slider1 = 4
            Debug.Print Slider1.Value
            """)).Should().Equal("Slider", "0", "Long", "True", "True", "0", "10", "1", "5", "0", "1", "11",
                "0", "0", "0", "0", "0", "False", "0", "0", "", "True", "True", "4");
    }

    [AvaloniaTheory]
    [InlineData("Value = 11", "Value", "0", "10")]
    [InlineData("Value = -1", "Value", "0", "0")]
    [InlineData("Value = 2.5", "Value", "0", "2")]
    [InlineData("Value = 3.5", "Value", "0", "4")]
    [InlineData("Value = \"7\"", "Value", "0", "7")]
    [InlineData("Value = \"x\"", "Value", "13", "0")]
    [InlineData("Min = 10", "Min", "380", "0")]
    [InlineData("Max = 0", "Max", "380", "10")]
    [InlineData("Min = 11", "Min", "380", "0")]
    [InlineData("Max = -1", "Max", "380", "10")]
    [InlineData("SmallChange = -1", "SmallChange", "380", "1")]
    [InlineData("LargeChange = -1", "LargeChange", "380", "5")]
    [InlineData("TickFrequency = -1", "TickFrequency", "380", "1")]
    [InlineData("SmallChange = 0", "SmallChange", "0", "0")]
    [InlineData("LargeChange = 20", "LargeChange", "0", "20")]
    [InlineData("TickStyle = 4", "TickStyle", "380", "0")]
    [InlineData("TickStyle = 3", "TickStyle", "0", "3")]
    [InlineData("Orientation = 2", "Orientation", "380", "0")]
    [InlineData("TextPosition = -1", "TextPosition", "380", "0")]
    [InlineData("BorderStyle = 2", "BorderStyle", "380", "0")]
    [InlineData("OLEDropMode = 2", "OLEDropMode", "380", "0")]
    [InlineData("SelStart = 20", "SelStart", "380", "0")]
    [InlineData("SelLength = 4", "SelLength", "380", "0")]
    [InlineData("GetNumTicks = 3", "GetNumTicks", "450", "11")]
    public async Task Setters_validate_before_changing(string assignment, string read, string error, string value)
    {
        (await Run($"Slider1.{assignment}\r\nDebug.Print Err.Number, Slider1.{read}")).Should().Equal(error, value);
    }

    [AvaloniaFact]
    public async Task Selection_is_independent_only_with_SelectRange()
    {
        (await Run("""
            Slider1.Value = 5
            Debug.Print Slider1.SelStart
            Slider1.SelStart = 3
            Debug.Print Slider1.Value
            Slider1.SelectRange = True
            Slider1.SelStart = 2
            Slider1.SelLength = 3
            Slider1.Value = 8
            Debug.Print Slider1.SelStart, Slider1.SelLength
            Slider1.SelStart = 8
            Debug.Print Slider1.SelLength
            Slider1.SelLength = 3
            Debug.Print Err.Number
            Slider1.SelStart = 2
            Slider1.SelLength = 3
            Slider1.Max = 1
            Debug.Print Slider1.SelStart, Slider1.SelLength, Slider1.Value
            Slider1.ClearSel
            Debug.Print Slider1.SelStart, Slider1.SelLength, Slider1.SelectRange
            Slider1.SelectRange = False
            Debug.Print Slider1.SelStart, Slider1.SelLength
            """)).Should().Equal("5", "3", "2", "3", "2", "380", "2", "-1", "1", "-1", "0", "True", "1", "0");
    }

    [AvaloniaFact]
    public async Task Tick_count_and_orientation_follow_the_object_model()
    {
        (await Run("""
            Slider1.TickFrequency = 3
            Debug.Print Slider1.GetNumTicks
            Slider1.Min = -10
            Debug.Print Slider1.GetNumTicks
            Slider1.TickFrequency = 0
            Debug.Print Slider1.GetNumTicks
            Slider1.Orientation = 1
            Debug.Print Slider1.Width, Slider1.Height
            Slider1.Value = 5
            Slider1.Min = 7
            Debug.Print Slider1.Value
            Slider1.Min = 0
            Slider1.Max = 4
            Debug.Print Slider1.Value
            """)).Should().Equal("5", "8", "2", "405", "3000", "7", "4");
    }
    [AvaloniaFact]
    public async Task Code_raises_Change_only_for_Value_and_DoClick_raises_Click()
    {
        var (root, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        var recorder = new ContainerRuntimeHarness.RecordingRoot { Child = root };
        await ContainerRuntimeHarness.Run(ctx, env, """
            Slider1.Value = 5
            Slider1.Value = 5
            Slider1.Value = 11
            Slider1.Value = -1
            Slider1.Value = 5
            Slider1.Min = 7
            Slider1.Min = 0
            Slider1.Max = 4
            Slider1.SelStart = 3
            Slider1.ClearSel
            Slider1.Refresh
            Slider1.DoClick
            """);
        recorder.Calls.Select(c => c.Name).Should().Equal("Slider1_Change", "Slider1_Change", "Slider1_Change",
            "Slider1_Change", "Slider1_Click");
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print Slider1.hWnd")]
    [InlineData("Debug.Print Slider1.MouseIcon")]
    [InlineData("Slider1.OLEDrag")]
    [InlineData("Slider1.Value = Null")]
    [InlineData("Slider1.Enabled = 5")]
    [InlineData("Slider1.Text = 5")]
    public async Task Unmeasured_members_and_conversions_refuse(string code)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*Slider*not implemented*");
    }

    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Slider_forms_stay_read_only_until_designer_edits_can_be_saved(string lineEnding)
    {
        var project = new HexIDE.Runtime.ProjectElements.ProjectDefinition(
            HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "Test");
        var sink = NSubstitute.Substitute.For<HexIDE.Runtime.Serialization.IDeserializeErrorSink>();
        var form = new HexIDE.Runtime.Serialization.FormDeserializer().Deserialize(project, Form.ReplaceLineEndings(lineEnding), sink)!;
        form.Components.Single(c => c.GetPropertyOrDefault(HexIDE.Runtime.Components.VBProperties.NameProperty) == "Slider1").BaseClass.ClassName.Should().Be("Slider");
        form.CanSaveFaithfully.Should().BeFalse();
    }

    [AvaloniaFact]
    public async Task Measured_nondefault_properties_are_available_from_code()
    {
        (await Run("""
            Slider1.SmallChange = 20
            Slider1.LargeChange = 0
            Slider1.TickFrequency = 20
            Slider1.TickStyle = 2
            Slider1.Text = "the tip"
            Slider1.TextPosition = 1
            Slider1.BorderStyle = 1
            Slider1.OLEDropMode = 1
            Slider1.MousePointer = 99
            Slider1.Enabled = False
            Debug.Print Slider1.SmallChange, Slider1.LargeChange, Slider1.GetNumTicks, Slider1.TickStyle
            Debug.Print Slider1.Text, Slider1.TextPosition, Slider1.BorderStyle, Slider1.OLEDropMode, Slider1.MousePointer, Slider1.Enabled
            Slider1.Min = -100
            Slider1.Max = 100000
            Debug.Print Slider1.Min, Slider1.Max, TypeName(Slider1.Max)
            """)).Should().Equal("20", "0", "2", "2", "the tip", "1", "1", "1", "99", "False", "-100", "100000", "Long");
    }

}
