using System;
using System.Globalization;
using System.Collections.Generic;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Integration.Tests.Views;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;

namespace HexIDE.Runtime.Integration.Tests.Views;

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
    public async Task Unmeasured_members_refuse(string code)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*Slider*not implemented*");
    }

    // Measured native and p-code: #1091's latest conversion tables, oracle PR #5506.
    public static IEnumerable<object[]> LongConversions()
    {
        foreach (var property in new[] { "Min", "Max", "Value", "SmallChange", "LargeChange", "TickFrequency", "SelStart", "SelLength" })
        foreach (var (expression, converted, conversionError) in new[]
        {
            ("Empty", 0, 0), ("True", -1, 0), ("False", 0, 0), ("\"7\"", 7, 0),
            ("\"abc\"", 0, 13), ("CByte(7)", 7, 0), ("CCur(7.5)", 8, 0),
            ("CDate(7.5)", 8, 0), ("CDec(7.5)", 8, 0), ("7.5", 8, 0), ("CSng(2.5)", 2, 0),
            ("Null", 0, 94),
        })
        {
            var initial = property switch { "Max" => 10, "SmallChange" or "TickFrequency" => 1, "LargeChange" => 5, _ => 0 };
            var error = conversionError != 0 ? conversionError
                : property == "Max" && converted <= 0 || property is not ("Min" or "Max" or "Value") && converted < 0 ? 380 : 0;
            var result = error != 0 ? initial : property == "Value" && converted < 0 ? 0 : converted;
            yield return new object[] { property, expression, error.ToString(CultureInfo.InvariantCulture), result.ToString(CultureInfo.InvariantCulture) };
        }
    }

    [AvaloniaTheory]
    [MemberData(nameof(LongConversions))]
    public async Task Long_stores_coerce_then_validate(string property, string expression, string error, string expected)
    {
        (await Run($"Slider1.SelectRange = True\r\nSlider1.{property} = {expression}\r\nDebug.Print Err.Number, Slider1.{property}"))
            .Should().Equal(error, expected);
    }

    public static IEnumerable<object[]> BooleanConversions()
    {
        foreach (var property in new[] { "Enabled", "SelectRange" })
        foreach (var (expression, error, expected) in new[]
        {
            ("0", "0", "False"), ("Empty", "0", "False"), ("\"False\"", "0", "False"),
            ("1", "0", "True"), ("-1", "0", "True"), ("2", "0", "True"), ("1.5", "0", "True"),
            ("CByte(1)", "0", "True"), ("CCur(1)", "0", "True"), ("\"True\"", "0", "True"),
            ("\"abc\"", "13", "True"), ("Null", "94", "True"),
        })
            yield return new object[] { property, expression, error, expected };
    }

    [AvaloniaTheory]
    [MemberData(nameof(BooleanConversions))]
    public async Task Boolean_stores_coerce_before_changing(string property, string expression, string error, string expected)
    {
        (await Run($"Slider1.{property} = True\r\nSlider1.{property} = {expression}\r\nDebug.Print Err.Number, Slider1.{property}"))
            .Should().Equal(error, expected);
    }

    [AvaloniaTheory]
    [InlineData("5", "0", "5")]
    [InlineData("True", "0", "True")]
    [InlineData("2.5", "0", "2.5")]
    [InlineData("CCur(3)", "0", "3")]
    [InlineData("Empty", "0", "")]
    [InlineData("Null", "94", "before")]
    public async Task Text_stores_coerce_before_changing(string expression, string error, string expected)
    {
        (await Run($"Slider1.Text = \"before\"\r\nSlider1.Text = {expression}\r\nDebug.Print Err.Number, Slider1.Text"))
            .Should().Equal(error, expected);
    }

    [AvaloniaFact]
    public async Task Date_text_uses_the_existing_CStr_conversion()
    {
        (await Run("Slider1.Text = CDate(2)\r\nDebug.Print Slider1.Text = CStr(CDate(2))")).Should().Equal("True");
    }

    [AvaloniaTheory]
    [InlineData("Min")]
    [InlineData("Max")]
    [InlineData("Value")]
    [InlineData("SmallChange")]
    [InlineData("LargeChange")]
    [InlineData("TickFrequency")]
    [InlineData("SelStart")]
    [InlineData("SelLength")]
    [InlineData("Enabled")]
    [InlineData("SelectRange")]
    [InlineData("Text")]
    public async Task Object_Null_stores_raise_13_without_changing(string property)
    {
        (await Run($"Dim o As Object\r\nSet o = Slider1\r\nDim before\r\nbefore = o.{property}\r\no.{property} = Null\r\nDebug.Print Err.Number, o.{property} = before"))
            .Should().Equal("13", "True");
    }

    [AvaloniaTheory]
    [InlineData("Slider1", "94")]
    [InlineData("o", "13")]
    public async Task Null_Value_store_preserves_a_nonzero_value(string target, string error)
    {
        (await Run($"Dim o As Object\r\nSet o = Slider1\r\nSlider1.Value = 4\r\n{target}.Value = Null\r\nDebug.Print Err.Number, Slider1.Value"))
            .Should().Equal(error, "4");
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    [InlineData(15)]
    [InlineData(99)]
    public async Task MousePointer_sets_the_stored_and_visible_cursor(int pointer)
    {
        var (_, canvas, ctx, env) = ContainerRuntimeHarness.Spawn(Form);
        await ContainerRuntimeHarness.Run(ctx, env, $"Slider1.MousePointer = {pointer}");
        var slider = canvas.Children.OfType<VBSlider>().Single();
        AttachedProperties.GetMousePointer(slider).Should().Be((VBCursorType)pointer);
        VBCursorTypeExtensions.PointerOf(slider.Cursor).Should().Be((VBCursorType)pointer);
    }

    [AvaloniaTheory]
    [InlineData(-1)]
    [InlineData(98)]
    [InlineData(100)]
    public async Task Invalid_MousePointer_preserves_the_previous_value(int pointer)
    {
        (await Run($"Slider1.MousePointer = 2\r\nSlider1.MousePointer = {pointer}\r\nDebug.Print Err.Number, Slider1.MousePointer"))
            .Should().Equal("380", "2");
    }

    [AvaloniaTheory]
    [InlineData("\n")]
    [InlineData("\r\n")]
    public void Slider_forms_can_save_while_own_properties_remain_view_only(string lineEnding)
    {
        var project = new HexIDE.Runtime.ProjectElements.ProjectDefinition(
            HexIDE.Runtime.ProjectElements.VBProjectType.EXE, "Test");
        var sink = NSubstitute.Substitute.For<HexIDE.Runtime.Serialization.IDeserializeErrorSink>();
        var form = new HexIDE.Runtime.Serialization.FormDeserializer().Deserialize(project, Form.ReplaceLineEndings(lineEnding), sink)!;
        form.Components.Single(c => c.GetPropertyOrDefault(HexIDE.Runtime.Components.VBProperties.NameProperty) == "Slider1").BaseClass.ClassName.Should().Be("Slider");
        form.CanSaveFaithfully.Should().BeTrue();
        form.Components.Single(c => c.BaseClass.ClassName == "Slider").BaseClass.Properties
            .Should().NotContain(p => p.Name == "Value");
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
