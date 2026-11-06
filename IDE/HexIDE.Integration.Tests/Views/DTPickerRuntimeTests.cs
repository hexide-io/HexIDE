using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Integration.Tests.Views;

// VB6 SP6 native and p-code, #1094 groups 1–3.
public class DTPickerRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 3015
           ClientWidth = 4560
           Begin MSComCtl2.DTPicker Picker1
              Left = 120
              Top = 120
              Width = 1500
              Height = 750
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



    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task Format_preserves_Date_and_time(int format)
    {
        (await Run($"Picker1.Value = #2/3/2001 4:05:06 AM#\r\nPicker1.Format = {format}\r\nDebug.Print TypeName(Picker1.Value), Picker1.Year, Picker1.Month, Picker1.Day, Picker1.Hour, Picker1.Minute, Picker1.Second, TypeName(Picker1.Day)"))
            .Should().Equal("Date", "2001", "2", "3", "4", "5", "6", "Integer");
    }

    [AvaloniaFact]
    public async Task Null_and_hidden_date()
    {
        (await Run("""
            Picker1.Value = #2/3/2001 4:05:06 AM#
            Picker1.CheckBox = True
            Picker1.Value = Null
            Debug.Print IsNull(Picker1.Value), IsNull(Picker1.Year), IsNull(Picker1.DayOfWeek)
            Picker1.CheckBox = False
            Debug.Print Picker1.Day, Picker1.Hour
            Picker1.Value = Null
            Debug.Print Err.Number
            """)).Should().Equal("True", "True", "True", "3", "4", "35787");
    }

    [AvaloniaTheory]
    [InlineData("Month = 2", "380")]
    [InlineData("Day = 0", "380")]
    [InlineData("Hour = 24", "380")]
    [InlineData("Minute = 60", "380")]
    [InlineData("DayOfWeek = 8", "380")]
    [InlineData("Value = #1/1/1500#", "35773")]
    [InlineData("Value = \"x\"", "380")]
    [InlineData("hWnd = 1", "383")]
    public async Task Measured_errors(string assignment, string error)
    {
        (await Run($"Picker1.Value = #1/31/2001#\r\nPicker1.{assignment}\r\nDebug.Print Err.Number")).Should().Equal(error);
    }

    [AvaloniaFact]
    public async Task Range_pull_preserves_time_and_boundary_assignment_is_ignored()
    {
        (await Run("""
            Picker1.Value = #2/3/2001 1:02:03 PM#
            Picker1.MaxDate = #2/2/2001#
            Debug.Print Picker1.Day, Picker1.Hour, Picker1.Minute, Picker1.Second
            Picker1.Value = #2/2/2001 2:00:00 PM#
            Debug.Print Err.Number, Picker1.Hour
            Picker1.MinDate = #2/3/2001#
            Debug.Print Err.Number
            """)).Should().Equal("2", "13", "2", "3", "0", "13", "35775");
    }

    [AvaloniaFact]
    public async Task Parts_round_and_move_within_week()
    {
        (await Run("""
            Picker1.Value = #2/3/2001 4:05:06 AM#
            Picker1.Day = 2.5
            Debug.Print Picker1.Day
            Picker1.DayOfWeek = 1
            Debug.Print Picker1.Day, Picker1.DayOfWeek
            Picker1.CustomFormat = "dd MMM yyyy"
            Debug.Print Picker1.Format, Picker1.CustomFormat, Picker1.UpDown
            """)).Should().Equal("2", "28", "1", "1", "dd MMM yyyy", "False");
    }

    [AvaloniaFact]
    public async Task Defaults_and_time_only_Date()
    {
        (await Run("""
            Debug.Print Picker1.Format, Picker1.CheckBox, Picker1.UpDown
            Debug.Print Year(Picker1.MinDate), Month(Picker1.MinDate), Day(Picker1.MinDate)
            Debug.Print Year(Picker1.MaxDate), Month(Picker1.MaxDate), Day(Picker1.MaxDate)
            Picker1.Value = #10:20:30 AM#
            Debug.Print Err.Number
            Picker1.MinDate = #1/1/100#
            Picker1.Value = #10:20:30 AM#
            Debug.Print Picker1.Year, Picker1.Month, Picker1.Day, Picker1.Hour
            """)).Should().Equal("1", "False", "False", "1601", "1", "1", "9999", "12", "31", "0", "1899", "12", "30", "10");
    }

    [AvaloniaFact]
    public async Task String_and_Boolean_dates()
    {
        (await Run("""
            Picker1.Value = "13/1/2001"
            Debug.Print Picker1.Year, Picker1.Month, Picker1.Day
            Picker1.Value = True
            Debug.Print Picker1.Year, Picker1.Month, Picker1.Day
            """)).Should().Equal("2001", "1", "13", "1899", "12", "29");
    }

    [AvaloniaFact]
    public async Task Code_assignments_do_not_raise_Change()
    {
        var (_, recorder, ctx, env) = ContainerRuntimeHarness.Recorded(Form);
        try
        {
            recorder.Calls.Clear();
            await ContainerRuntimeHarness.Run(ctx, env, """
                Picker1.Value = #2/3/2001#
                Picker1.Day = 4
                Picker1.MaxDate = #2/2/2001#
                Picker1.CheckBox = True
                Picker1.Value = Null
                """);
            recorder.Calls.Should().BeEmpty();
        }
        finally
        {
            (Avalonia.Controls.TopLevel.GetTopLevel(recorder) as Avalonia.Controls.Window)?.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("CalendarBackColor", 255)]
    [InlineData("CalendarForeColor", 65280)]
    [InlineData("CalendarTitleBackColor", 16711680)]
    [InlineData("CalendarTitleForeColor", 65535)]
    [InlineData("CalendarTrailingForeColor", 8421504)]
    public async Task Assigned_calendar_colours_read_as_Long(string member, int colour)
    {
        (await Run($"Picker1.{member} = {colour}\r\nDebug.Print Picker1.{member}, TypeName(Picker1.{member})"))
            .Should().Equal(colour.ToString(CultureInfo.InvariantCulture), "Long");
    }

    // docs/vb6-fidelity-oracle/in-box-controls-the-dtpicker.md, object model (native and p-code).
    [AvaloniaTheory]
    [InlineData("CalendarBackColor", -2147483643L)]
    [InlineData("CalendarForeColor", -2147483630L)]
    [InlineData("CalendarTitleBackColor", -2147483633L)]
    [InlineData("CalendarTitleForeColor", -2147483630L)]
    [InlineData("CalendarTrailingForeColor", -2147483631L)]
    public async Task Calendar_defaults_are_system_colour_Longs(string member, long colour)
    {
        (await Run($"Debug.Print Picker1.{member}, TypeName(Picker1.{member})"))
            .Should().Equal(colour.ToString(CultureInfo.InvariantCulture), "Long");
    }

    [AvaloniaFact]
    public async Task Measured_pointer_and_drop_mode_values()
    {
        (await Run("""
            Debug.Print Picker1.MousePointer, Picker1.OLEDropMode
            Picker1.MousePointer = 11
            Picker1.OLEDropMode = 1
            Debug.Print Picker1.MousePointer, Picker1.OLEDropMode
            Picker1.MousePointer = 99
            Debug.Print Picker1.MousePointer
            Picker1.MousePointer = 100
            Debug.Print Err.Number, Picker1.MousePointer
            """)).Should().Equal("0", "0", "11", "1", "99", "380", "99");
    }

    [AvaloniaFact]
    public async Task Default_member_reads_and_writes_Value()
    {
        (await Run("""
            Picker1 = #2/3/2001#
            Dim v As Variant
            v = Picker1
            Debug.Print TypeName(v), Day(v)
            Debug.Print VarType(Picker1)
            """)).Should().Equal("Date", "3", "7");
    }

    [AvaloniaFact]
    public async Task Numeric_dates_preserve_fractional_time()
    {
        (await Run("""
            Picker1.Value = 37000
            Debug.Print Picker1.Year, Picker1.Month, Picker1.Day
            Picker1.Value = 37000.75
            Debug.Print Picker1.Hour
            """)).Should().Equal("2001", "4", "19", "18");
    }

    [AvaloniaFact]
    public async Task Measured_part_conversions_and_range_error()
    {
        (await Run("""
            Picker1.Value = #1/31/2001#
            Picker1.Day = "7"
            Debug.Print Picker1.Day
            Picker1.Year = "x"
            Debug.Print Err.Number
            Err.Clear
            Picker1.Year = Null
            Debug.Print Err.Number
            Err.Clear
            Picker1.Year = 1500
            Debug.Print Err.Number
            """)).Should().Equal("7", "380", "380", "380");
    }

    [AvaloniaFact]
    public async Task Enabled_and_TabStop_default_to_True()
    {
        (await Run("Debug.Print Picker1.Enabled, Picker1.TabStop")).Should().Equal("True", "True");
    }

    [AvaloniaTheory]
    [InlineData("MaxDate = #1/1/1500#", "35774", "A value was specified for the MaxDate property that is lower than the current value of MinDate.")]
    [InlineData("MinDate = #12/31/9999 1:00 PM#", "35775", "A value was specified for the MinDate property that is higher than the current value of MaxDate.")]
    public async Task Range_errors_have_measured_descriptions(string assignment, string number, string description)
    {
        (await Run($"Picker1.{assignment}\r\nDebug.Print Err.Number, Err.Description"))
            .Should().Equal(number, description);
    }

    [AvaloniaFact]
    public async Task Format_is_Long()
    {
        (await Run("Debug.Print TypeName(Picker1.Format)")).Should().Equal("Long");
    }

    [AvaloniaFact]
    public void Minimal_carried_picker_runs_but_nested_and_arrayed_pickers_do_not()
    {
        HexIDE.Runtime.Serialization.FormDeserializer.RunsWhenCarried("MSComCtl2.DTPicker", false, false).Should().BeTrue();
        HexIDE.Runtime.Serialization.FormDeserializer.RunsWhenCarried("MSComCtl2.DTPicker", true, false).Should().BeFalse();
        HexIDE.Runtime.Serialization.FormDeserializer.RunsWhenCarried("MSComCtl2.DTPicker", false, true).Should().BeFalse();
    }

    [AvaloniaTheory]
    [InlineData("Debug.Print Picker1.Font")]
    [InlineData("Debug.Print Picker1.MouseIcon")]
    [InlineData("Debug.Print Picker1.hWnd")]
    [InlineData("Picker1.Value = CByte(42)")]
    [InlineData("Picker1.CheckBox = 1")]
    [InlineData("Picker1.CustomFormat = 1")]
    public async Task Unimplemented_members_refuse(string code)
    {
        var act = () => Run(code);
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*DTPicker*not implemented*");
    }
}
