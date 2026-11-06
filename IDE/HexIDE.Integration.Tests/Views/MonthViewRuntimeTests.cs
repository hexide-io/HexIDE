using System;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Headless.XUnit;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.IDE;

namespace HexIDE.Integration.Tests.Views;

// #1095, object-model and saved-block measurements, native matching p-code.
public class MonthViewRuntimeTests
{
    private const string Form = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientHeight = 4500
           ClientWidth = 6000
           Begin MSComCtl2.MonthView MonthView1
              Left = 1500
              Top = 120
              Height = 2370
              Width = 2700
              _Version = 393216
           End
        End
        Attribute VB_Name = "Form1"
        """;

    private static async Task<string[]> Run(string code, string properties = "")
    {
        var (_, _, ctx, env) = ContainerRuntimeHarness.Spawn(Form.Replace("_Version = 393216", properties, StringComparison.Ordinal));
        var printed = await ContainerRuntimeHarness.Run(ctx, env, "On Error Resume Next\n" + code);
        return printed.Select(v => Convert.ToString(v.Value, CultureInfo.InvariantCulture) ?? "").ToArray();
    }

    [AvaloniaFact]
    public async Task Saved_date_and_week_start_load_and_Value_is_the_default_member()
    {
        (await Run("""
            Dim v
            v = MonthView1
            Debug.Print v = #6/15/2001#, TypeName(v), MonthView1.Value = v
            Debug.Print MonthView1.StartOfWeek, TypeName(MonthView1.StartOfWeek)
            Debug.Print TypeOf MonthView1 Is MSComCtl2.MonthView
            MonthView1 = #7/15/2001#
            v = MonthView1
            Debug.Print v = #7/15/2001#, MonthView1.Month, TypeName(MonthView1.Month)
            """, "CurrentDate = 37057\nStartOfWeek = 110493698")).Should().Equal("True", "Date", "True", "2", "Long", "True", "True", "7", "Long");
    }

    [AvaloniaFact]
    public async Task A_missing_date_loads_today_and_a_missing_week_start_is_Sunday()
    {
        (await Run("""
            Debug.Print MonthView1.Value = Date, MonthView1.SelStart = Date, MonthView1.SelEnd = Date
            Debug.Print MonthView1.StartOfWeek, MonthView1.MultiSelect, MonthView1.MaxSelCount
            Debug.Print MonthView1.MinDate = #1/1/1753#, MonthView1.MaxDate = #12/31/9999#
            """)).Should().Equal("True", "True", "True", "1", "False", "7", "True", "True");
    }

    [AvaloniaFact]
    public async Task Saved_selection_settings_and_bounds_feed_the_runtime_state()
    {
        (await Run("""
            Debug.Print MonthView1.MultiSelect, MonthView1.MaxSelCount, MonthView1.ShowToday, MonthView1.ShowWeekNumbers
            MonthView1.SelEnd = #6/18/2001#
            Debug.Print MonthView1.SelEnd = #6/18/2001#
            MonthView1.SelEnd = #6/30/2001#
            Debug.Print Err.Number, MonthView1.SelEnd = #6/18/2001#
            """, "CurrentDate = 37057\nMultiSelect = -1\nMaxSelCount = 14\nShowToday = 0\nShowWeekNumbers = -1\nMinDate = 36526\nMaxDate = 47848"))
            .Should().Equal("True", "14", "False", "True", "True", "35770", "True");
    }

    [AvaloniaFact]
    public async Task Saved_scalar_settings_and_runtime_writes_use_the_state_model()
    {
        (await Run("""
            Debug.Print MonthView1.Appearance, MonthView1.BorderStyle, MonthView1.MousePointer, MonthView1.OLEDropMode
            Debug.Print MonthView1.ScrollRate, MonthView1.MonthRows, MonthView1.MonthColumns
            MonthView1.Month = 7
            MonthView1.Day = 20
            MonthView1.Year = 2002
            Debug.Print MonthView1.Value = #7/20/2002#
            MonthView1.MonthRows = 13
            Debug.Print Err.Number, MonthView1.MonthRows
            """, "CurrentDate = 37057\nAppearance = 0\nBorderStyle = 1\nMousePointer = 2\nOLEDropMode = 1\nScrollRate = 3\nMonthRows = 2\nMonthColumns = 2"))
            .Should().Equal("0", "1", "2", "1", "3", "2", "2", "True", "35776", "2");
    }

    [AvaloniaTheory]
    [InlineData("Width")]
    [InlineData("Height")]
    [InlineData("Left")]
    [InlineData("Top")]
    [InlineData("Font")]
    [InlineData("BackColor")]
    [InlineData("ForeColor")]
    [InlineData("MonthBackColor")]
    [InlineData("TitleBackColor")]
    [InlineData("TitleForeColor")]
    [InlineData("TrailingForeColor")]
    [InlineData("hWnd")]
    [InlineData("MouseIcon")]
    [InlineData("DayBold(#6/15/2001#)")]
    public async Task Pending_members_refuse_explicitly(string member)
    {
        (await Run("Debug.Print MonthView1.MaxSelCount")).Should().Equal("7");
        var act = () => Run($"Debug.Print MonthView1.{member}");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*MonthView*not implemented*1095*");
    }

    [AvaloniaFact]
    public async Task Refresh_can_be_called_and_unimplemented_methods_refuse()
    {
        (await Run("MonthView1.Refresh\nDebug.Print Err.Number")).Should().Equal("0");
        var act = () => Run("MonthView1.OLEDrag");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*MonthView*not implemented*1095*");
    }

    [AvaloniaTheory]
    [InlineData("ComputeControlSize 1, 1, 0, 0")]
    [InlineData("HitTest 0, 0, 0")]
    [InlineData("AboutBox")]
    public async Task Pending_methods_refuse_explicitly(string method)
    {
        (await Run("Debug.Print MonthView1.MaxSelCount")).Should().Equal("7");
        var act = () => Run($"MonthView1.{method}");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*MonthView*not implemented*1095*");
    }

    [AvaloniaFact]
    public async Task Unsupported_saved_own_properties_are_not_silently_ignored()
    {
        var act = () => Run("", "TitleBackColor = 65535");
        await act.Should().ThrowAsync<NotImplementedException>().WithMessage("*MonthView*TitleBackColor*not implemented*1095*");
    }

    [AvaloniaTheory]
    [InlineData("1.5", 5, 28)]
    [InlineData("2.5", 5, 28)]
    [InlineData("42.5", 7, 7)]
    [InlineData("\"2\"", 5, 28)]
    public async Task VisibleDays_rounds_the_index_and_returns_a_Date(string index, int month, int day)
    {
        (await Run($"Dim v\n v = MonthView1.VisibleDays({index})\n Debug.Print Int(CDbl(v)) = CDbl(DateSerial(2001, {month}, {day})), TypeName(v)",
            "CurrentDate = 37057")).Should().Equal("True", "Date");
    }

    [AvaloniaTheory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("43")]
    [InlineData("32767")]
    [InlineData("0.5")]
    [InlineData("42.6")]
    public async Task VisibleDays_rejects_indexes_outside_the_grid(string index)
    {
        (await Run($"Dim v\n v = MonthView1.VisibleDays({index})\n Debug.Print Err.Number",
            "CurrentDate = 37057")).Should().Equal("380");
    }

    [AvaloniaFact]
    public async Task Twelve_months_show_Values_calendar_year()
    {
        (await Run("""
            MonthView1.MonthRows = 3
            MonthView1.MonthColumns = 4
            Debug.Print Int(CDbl(MonthView1.VisibleDays(1))) = CDbl(#12/31/2000#)
            Debug.Print Int(CDbl(MonthView1.VisibleDays(371))) = CDbl(#1/5/2002#)
            MonthView1.Value = #1/1/2005#
            Debug.Print MonthView1.Week, TypeName(MonthView1.Week)
            MonthView1.Week = 1
            Debug.Print MonthView1.Value = #1/10/2004#
            MonthView1.VisibleDays(1) = #6/15/2001#
            Debug.Print Err.Number
            """, "CurrentDate = 37057")).Should().Equal("True", "True", "52", "Integer", "True", "383");
    }

    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    [AvaloniaFact]
    public async Task The_designer_keeps_the_block_view_only_and_verbatim_when_the_form_changes()
    {
        // Spawn first: this regression must prove the carried block can also run.
        (await Run("Debug.Print MonthView1.MaxSelCount")).Should().Equal("7");
        var definition = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"), Form, new NullSink())!;
        var block = definition.UnknownChildSubtreeTexts.Single();
        definition.Components.Single().SetProperty(HexIDE.Runtime.Components.VBProperties.CaptionProperty, "Changed");
        new FormSerializer().Serialize(definition, "Form1.frm").Item1.ReplaceLineEndings()
            .Should().Contain(block.ReplaceLineEndings());
        FormDeserializer.RunsWhenCarried("MSComCtl2.MonthView", false, false).Should().BeTrue();
        FormDeserializer.RunsWhenCarried("MSComCtl2.MonthView", false, true).Should().BeFalse();
    }
}
