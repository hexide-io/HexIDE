using System;
using System.Globalization;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Group 1 of #1095, native and p-code agreeing. This slice tests state, not a placed or drawn control.
public class MonthViewStateTests
{
    private readonly MonthViewState state = new();
    private static Vb6Value Date(int year, int month, int day, int hour = 0) =>
        new(VbDate.FromDateTime(new DateTime(year, month, day, hour, 0, 0)));

    private Vb6Value Get(string name)
    {
        state.TryGetProperty(name, out var value).Should().BeTrue();
        return value;
    }

    private void Set(string name, Vb6Value value) => state.TrySetProperty(name, value).Should().BeTrue();

    private void Error(string name, Vb6Value value, int number)
    {
        Action act = () => Set(name, value);
        act.Should().Throw<VBRunTimeException>().Which.Error.ErrNo.Should().Be(number);
    }

    [Theory]
    [InlineData("Appearance", 1, 0)]
    [InlineData("BorderStyle", 0, 1)]
    [InlineData("MousePointer", 0, 2)]
    [InlineData("OLEDropMode", 0, 1)]
    [InlineData("ScrollRate", 0, 100)]
    public void Scalar_defaults_and_measured_writes(string name, int initial, int setting)
    {
        Get(name).Value.Should().Be(initial);
        Set(name, new Vb6Value(setting));
        Get(name).Value.Should().Be(setting);
    }

    [Theory]
    [InlineData("Appearance", 2)]
    [InlineData("Appearance", -1)]
    [InlineData("BorderStyle", 2)]
    [InlineData("BorderStyle", -1)]
    [InlineData("MousePointer", -1)]
    [InlineData("OLEDropMode", 2)]
    [InlineData("ScrollRate", -1)]
    public void Scalar_measured_errors_preserve_defaults(string name, int setting)
    {
        var before = Get(name);
        Error(name, new Vb6Value(setting), 380);
        Get(name).Should().Be(before);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(-1)]
    public void Enabled_accepts_measured_numbers(int setting)
    {
        Get("Enabled").Should().Be(new Vb6Value(true));
        Set("Enabled", new Vb6Value(setting));
        Get("Enabled").Should().Be(new Vb6Value(true));
    }

    [Theory]
    [InlineData("MousePointer", 3)]
    [InlineData("OLEDropMode", -1)]
    [InlineData("Enabled", 0)]
    public void Unmeasured_scalar_values_are_refused(string name, int setting)
    {
        var before = Get(name);
        Action write = () => Set(name, new Vb6Value(setting));
        write.Should().Throw<NotImplementedException>();
        Get(name).Should().Be(before);
    }

    [Fact]
    public void Defaults_are_today_without_time_and_the_measured_selection_and_layout()
    {
        var before = DateTime.Today;
        var fresh = new MonthViewState();
        fresh.TryGetProperty("Value", out var today).Should().BeTrue();
        ((VbDate)today.Value!).Instant.Should().BeOneOf(before, DateTime.Today);
        today.Type.Should().Be(Vb6Value.ValueType.Date);
        fresh.TryGetProperty("SelStart", out var start).Should().BeTrue();
        fresh.TryGetProperty("SelEnd", out var end).Should().BeTrue();
        start.Should().Be(today);
        end.Should().Be(today);
        Get("MinDate").Should().Be(Date(1753, 1, 1));
        Get("MaxDate").Should().Be(Date(9999, 12, 31));
        Get("MaxSelCount").Should().Be(new Vb6Value(7));
        Get("MultiSelect").Should().Be(new Vb6Value(false));
        Get("MonthRows").Should().Be(new Vb6Value(1));
        Get("MonthColumns").Should().Be(new Vb6Value(1));
        Get("StartOfWeek").Should().Be(new Vb6Value(1L));
        Get("ShowToday").Should().Be(new Vb6Value(true));
        Get("ShowWeekNumbers").Should().Be(new Vb6Value(false));
    }

    [Fact]
    public void Value_keeps_time_but_collapses_selection_to_the_day()
    {
        Set("vAlUe", Date(2001, 2, 3, 4));
        Get("_Value").Should().Be(Date(2001, 2, 3, 4));
        Get("SelStart").Should().Be(Date(2001, 2, 3));
        Get("SelEnd").Should().Be(Date(2001, 2, 3));
        Get("Year").Should().Be(new Vb6Value(2001));
        Get("Month").Should().Be(new Vb6Value(2L));
        Get("Day").Should().Be(new Vb6Value(3));
    }

    [Theory]
    [InlineData(37000.0, 0)]
    [InlineData(37000.75, 18)]
    public void Value_converts_measured_serials(double serial, int hour)
    {
        Set("Value", new Vb6Value(serial));
        Get("Value").Should().Be(Date(2001, 4, 19, hour));
    }

    [Theory]
    [InlineData("2002-03-04", 2002, 3, 4)]
    [InlineData("13/1/2001", 2001, 1, 13)]
    public void Value_uses_VBs_date_conversion(string text, int year, int month, int day)
    {
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
            Set("Value", new Vb6Value(text));
        Get("Value").Should().Be(Date(year, month, day));
    }

    [Theory]
    [InlineData("x")]
    [InlineData("2001-02-30")]
    public void Invalid_strings_raise_13_and_preserve_Value(string text)
    {
        Set("Value", Date(2001, 6, 15));
        Error("Value", new Vb6Value(text), 13);
        Get("Value").Should().Be(Date(2001, 6, 15));
    }

    [Fact]
    public void Value_distinguishes_Empty_True_and_Null()
    {
        Set("Value", Vb6Value.Variant);
        Get("Value").Should().Be(Date(1899, 12, 30));
        Set("Value", new Vb6Value(true));
        Get("Value").Should().Be(Date(1899, 12, 29));
        Error("Value", new Vb6Value((string?)null), 13);
        Get("Value").Should().Be(Date(1899, 12, 29));
    }

    [Fact]
    public void Value_accepts_the_endpoints_and_refuses_dates_below_MinDate()
    {
        Set("Value", Date(1753, 1, 1));
        Error("Value", Date(1752, 12, 31), 35773);
        Get("Value").Should().Be(Date(1753, 1, 1));
        Set("Value", Date(9999, 12, 31));
        Get("Value").Should().Be(Date(9999, 12, 31));
    }

    [Fact]
    public void Bounds_pull_Value_and_keep_a_MinDate_time()
    {
        Set("Value", Date(2001, 6, 15));
        Set("MinDate", Date(2001, 6, 1));
        Get("Value").Should().Be(Date(2001, 6, 15));
        Set("MinDate", Date(2001, 7, 1));
        Get("Value").Should().Be(Date(2001, 7, 1));
        Set("MinDate", Date(2001, 6, 15, 15));
        Get("MinDate").Should().Be(Date(2001, 6, 15, 15));
        Set("MaxDate", Date(2001, 6, 20));
        Get("Value").Should().Be(Date(2001, 6, 20));
        Error("MaxDate", Date(2001, 6, 1), 35774);
        Get("MaxDate").Should().Be(Date(2001, 6, 20));
    }

    [Theory]
    [InlineData("SelStart", 35786)]
    [InlineData("SelEnd", 35785)]
    public void Selection_writes_require_MultiSelect(string name, int number) => Error(name, Date(2001, 6, 10), number);

    [Fact]
    public void Selection_follows_start_rejects_long_ranges_and_collapses_past_end()
    {
        Set("Value", Date(2001, 6, 15));
        Set("MultiSelect", new Vb6Value(true));
        Set("SelEnd", Date(2001, 6, 18));
        Get("Value").Should().Be(Date(2001, 6, 15));
        Set("SelStart", Date(2001, 6, 12));
        Get("Value").Should().Be(Date(2001, 6, 12));
        Error("SelEnd", Date(2001, 6, 30), 35770);
        Get("SelEnd").Should().Be(Date(2001, 6, 18));
        Set("SelStart", Date(2001, 6, 20));
        Get("SelEnd").Should().Be(Date(2001, 6, 20));
        Get("Value").Should().Be(Date(2001, 6, 20));
        Set("Value", Date(2001, 6, 25));
        Get("SelStart").Should().Be(Date(2001, 6, 25));
        Get("SelEnd").Should().Be(Date(2001, 6, 25));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(31)]
    [InlineData(32767)]
    [InlineData(3)]
    public void MaxSelCount_accepts_measured_counts(int count)
    {
        Set("MaxSelCount", new Vb6Value(count));
        Get("MaxSelCount").Should().Be(new Vb6Value(count));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void MaxSelCount_rejects_nonpositive_counts(int count)
    {
        Error("MaxSelCount", new Vb6Value(count), 380);
        Get("MaxSelCount").Should().Be(new Vb6Value(7));
    }

    [Theory]
    [InlineData(0, 35776)]
    [InlineData(13, 35776)]
    [InlineData(5, 35777)]
    public void Month_counts_validate_each_dimension_and_the_product(int rows, int error)
    {
        Set("MonthColumns", new Vb6Value(4));
        Error("MonthRows", new Vb6Value(rows), error);
        Get("MonthRows").Should().Be(new Vb6Value(1));
        Set("MonthRows", new Vb6Value(3));
        Get("MonthColumns").Should().Be(new Vb6Value(4));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(8)]
    public void StartOfWeek_rejects_outside_one_to_seven(int day)
    {
        Error("StartOfWeek", new Vb6Value(day), 380);
        Get("StartOfWeek").Should().Be(new Vb6Value(1L));
        Set("StartOfWeek", new Vb6Value(7));
        Get("StartOfWeek").Should().Be(new Vb6Value(7L));
    }

    [Theory]
    [InlineData("Month", 2001, 1, 31, 2)]
    [InlineData("Day", 2001, 2, 1, 29)]
    [InlineData("Day", 2001, 2, 1, 31)]
    [InlineData("Day", 2001, 2, 1, 0)]
    [InlineData("Year", 2000, 2, 29, 2001)]
    public void Invalid_date_parts_do_not_clamp_or_change_Value(string part, int year, int month, int day, int setting)
    {
        Set("Value", Date(year, month, day));
        Error(part, new Vb6Value(setting), 380);
        Get("Value").Should().Be(Date(year, month, day));
    }

    [Theory]
    [InlineData(1, 10)]
    [InlineData(7, 16)]
    [InlineData(2, 11)]
    public void DayOfWeek_moves_within_the_Sunday_week(int weekday, int day)
    {
        Set("Value", Date(2001, 6, 15));
        Set("DayOfWeek", new Vb6Value(weekday));
        Get("Value").Should().Be(Date(2001, 6, day));
        Get("DayOfWeek").Should().Be(new Vb6Value((long)weekday));
    }

    [Fact]
    public void Display_switches_are_independent()
    {
        Set("ShowToday", new Vb6Value(false));
        Set("ShowWeekNumbers", new Vb6Value(true));
        Get("ShowToday").Should().Be(new Vb6Value(false));
        Get("ShowWeekNumbers").Should().Be(new Vb6Value(true));
    }

    [Theory]
    [InlineData("Week", 0)]
    [InlineData("Week", 54)]
    [InlineData("DayOfWeek", 0)]
    [InlineData("DayOfWeek", 8)]
    public void Measured_invalid_week_parts_raise_380(string name, int number)
    {
        Set("Value", Date(2001, 6, 15));
        Error(name, new Vb6Value(number), 380);
        Get("Value").Should().Be(Date(2001, 6, 15));
    }

    [Fact]
    public void VisibleDays_is_read_only() => Error("VisibleDays", Date(2001, 6, 15), 383);

    [Fact]
    public void Successful_month_part_write_moves_Value()
    {
        Set("Value", Date(2001, 6, 15));
        Set("Month", new Vb6Value(7));
        Get("Value").Should().Be(Date(2001, 7, 15));
    }

    [Fact]
    public void Unmeasured_conversions_and_selection_edges_leave_state_unchanged()
    {
        Set("Value", Date(2001, 6, 15));
        Set("MultiSelect", new Vb6Value(true));
        Action conversion = () => Set("MultiSelect", new Vb6Value(1));
        conversion.Should().Throw<NotImplementedException>();
        Action outsideBounds = () => Set("SelStart", Date(1601, 1, 1));
        outsideBounds.Should().Throw<NotImplementedException>();
        Get("SelEnd").Should().Be(Date(2001, 6, 15));
        Get("MultiSelect").Should().Be(new Vb6Value(true));
        Action sameDayTime = () => Set("MinDate", Date(2001, 6, 15, 15));
        sameDayTime.Should().Throw<NotImplementedException>();
        Action laterBound = () => Set("MaxDate", Date(9999, 12, 31, 12));
        laterBound.Should().Throw<NotImplementedException>();
        Action timeAtBound = () => Set("Value", Date(1753, 1, 1, 12));
        timeAtBound.Should().Throw<NotImplementedException>();
        Get("Value").Should().Be(Date(2001, 6, 15));
        Get("MinDate").Should().Be(Date(1753, 1, 1));
        Get("MaxDate").Should().Be(Date(9999, 12, 31));
        Set("SelEnd", Date(2001, 6, 18));
        Action movePart = () => Set("Month", new Vb6Value(7));
        movePart.Should().Throw<NotImplementedException>();
        Get("SelStart").Should().Be(Date(2001, 6, 15));
        Get("SelEnd").Should().Be(Date(2001, 6, 18));
        Get("MultiSelect").Should().Be(new Vb6Value(true));
        Set("Value", Date(9999, 12, 31));
        Action beyondCalendar = () => Set("DayOfWeek", new Vb6Value(7));
        beyondCalendar.Should().Throw<NotImplementedException>();
        Get("Value").Should().Be(Date(9999, 12, 31));
    }

    [Theory]
    [InlineData(1, 2004, 12, 31, 52)]
    [InlineData(2, 2004, 12, 31, 53)]
    [InlineData(3, 2005, 1, 1, 53)]
    [InlineData(4, 2004, 12, 31, 1)]
    [InlineData(7, 2005, 1, 1, 1)]
    [InlineData(1, 1753, 1, 1, 1)]
    [InlineData(1, 9999, 12, 31, 52)]
    public void Week_uses_the_week_holding_January_four(int first, int y, int m, int d, int week)
    {
        Set("Value", Date(y, m, d));
        Set("StartOfWeek", new Vb6Value(first));
        Get("Week").Should().Be(new Vb6Value(week));
    }

    [Theory]
    [InlineData(2005, 1, 1, 1, 2004, 1, 10)]
    [InlineData(2001, 6, 15, 53, 2002, 1, 4)]
    public void Week_writes_use_the_week_year(int y, int m, int d, int week, int ey, int em, int ed)
    {
        Set("Value", Date(y, m, d));
        Set("Week", new Vb6Value(week));
        Get("Value").Should().Be(Date(ey, em, ed));
    }

    [Fact]
    public void Reversed_end_and_disabling_multiselect_collapse_to_the_start()
    {
        Set("Value", Date(2001, 6, 15));
        Set("MultiSelect", new Vb6Value(true));
        Set("SelEnd", Date(2001, 6, 12));
        Get("Value").Should().Be(Date(2001, 6, 12));
        Get("SelStart").Should().Be(Get("SelEnd"));
        Set("SelEnd", Date(2001, 6, 18));
        Error("MaxSelCount", new Vb6Value(3), 35770);
        Get("MaxSelCount").Should().Be(new Vb6Value(7));
        Get("SelEnd").Should().Be(Date(2001, 6, 18));
        Set("MultiSelect", new Vb6Value(false));
        Get("SelEnd").Should().Be(Date(2001, 6, 12));
    }

    [Fact]
    public void Bounds_inside_selection_preserve_end_or_collapse_when_Value_moves()
    {
        Set("Value", Date(2001, 6, 20));
        Set("MultiSelect", new Vb6Value(true));
        Set("SelEnd", Date(2001, 6, 22));
        Set("MaxDate", Date(2001, 6, 21));
        Get("SelEnd").Should().Be(Date(2001, 6, 22));
        Error("SelEnd", Date(2001, 6, 22), 35770);
        Set("SelEnd", Date(2001, 6, 21));
        Set("MinDate", Date(2001, 6, 21));
        Get("Value").Should().Be(Date(2001, 6, 21));
        Get("SelEnd").Should().Be(Get("SelStart"));
    }

    [Theory]
    [InlineData("MinDate", 1752, 12, 31, 35772)]
    [InlineData("MinDate", 1601, 1, 1, 35772)]
    [InlineData("MinDate", 2001, 7, 1, 35775)]
    public void Bound_errors_preserve_the_bound(string name, int y, int m, int d, int error)
    {
        Set("MaxDate", Date(2001, 6, 30));
        var before = Get(name);
        Error(name, Date(y, m, d), error);
        Get(name).Should().Be(before);
    }
}
