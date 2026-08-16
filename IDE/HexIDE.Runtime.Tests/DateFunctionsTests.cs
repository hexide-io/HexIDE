using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3 — Date/Time intrinsics. Every non-clock value pinned against vb6.exe. Parts are extracted with
/// Year/Month/Day/... so the assertions stay culture- and format-independent; clock functions (Now/Date/Timer)
/// are checked structurally. DateDiff returns Long, so its expectations use the L suffix.
/// </summary>
public class DateFunctionsTests : BaseVBTestFixture
{
    [Fact]
    public async Task Parts_And_Weekday()
    {
        await Run(
            "Dim d As Date\n" +
            "d = #3/15/2020 2:30:45 PM#\n" +          // a Sunday
            "Debug.Print Year(d)\n" +
            "Debug.Print Month(d)\n" +
            "Debug.Print Day(d)\n" +
            "Debug.Print Hour(d)\n" +
            "Debug.Print Minute(d)\n" +
            "Debug.Print Second(d)\n" +
            "Debug.Print Weekday(d)\n" +              // vbSunday base -> Sunday = 1
            "Debug.Print Weekday(d, vbMonday)\n");    // Monday base -> Sunday = 7
        AssertDebugLog([2020, 3, 15, 14, 30, 45, 1, 7]);
    }

    [Fact]
    public async Task DateSerial_And_TimeSerial_RollOver()
    {
        await Run(
            "Debug.Print Year(DateSerial(2020, 13, 1))\n" +   // 2021
            "Debug.Print Month(DateSerial(2020, 13, 1))\n" +  // 1
            "Debug.Print Month(DateSerial(2020, 2, 30))\n" +  // 3 (Feb 30 -> Mar 1)
            "Debug.Print Day(DateSerial(2020, 2, 30))\n" +    // 1
            "Debug.Print Day(DateSerial(2020, 3, 0))\n" +     // 29 (day 0 -> last of prev month)
            "Debug.Print Hour(TimeSerial(25, 0, 0))\n");      // 1 (25h rolls over)
        AssertDebugLog([2021, 1, 3, 1, 29, 1]);
    }

    [Fact]
    public async Task DateAdd_MonthEndClamp_And_Intervals()
    {
        await Run(
            "Debug.Print Month(DateAdd(\"m\", 1, #1/31/2020#))\n" +    // 2
            "Debug.Print Day(DateAdd(\"m\", 1, #1/31/2020#))\n" +      // 29 (leap clamp)
            "Debug.Print Year(DateAdd(\"yyyy\", 1, #2/29/2020#))\n" +  // 2021
            "Debug.Print Day(DateAdd(\"yyyy\", 1, #2/29/2020#))\n" +   // 28
            "Debug.Print Day(DateAdd(\"d\", 40, #1/1/2020#))\n" +      // 10 (Feb 10)
            "Debug.Print Month(DateAdd(\"q\", 1, #1/15/2020#))\n");    // 4
        AssertDebugLog([2, 29, 2021, 28, 10, 4]);
    }

    [Fact]
    public async Task DateDiff_BoundaryCounting_ReturnsLong()
    {
        await Run(
            "Debug.Print DateDiff(\"d\", #1/1/2020#, #12/31/2020#)\n" +               // 365
            "Debug.Print DateDiff(\"m\", #1/15/2020#, #3/10/2020#)\n" +               // 2
            "Debug.Print DateDiff(\"yyyy\", #12/31/2020#, #1/1/2021#)\n" +            // 1
            "Debug.Print DateDiff(\"h\", #1/1/2020 8:30:00#, #1/1/2020 9:15:00#)\n" + // 1 (boundary)
            "Debug.Print DateDiff(\"n\", #1/1/2020 8:00:40#, #1/1/2020 8:01:20#)\n" + // 1 (boundary)
            "Debug.Print DateDiff(\"d\", #1/10/2020#, #1/1/2020#)\n");               // -9 (negative)
        AssertDebugLog([new Vb6Value(365L), new Vb6Value(2L), new Vb6Value(1L), new Vb6Value(1L), new Vb6Value(1L), new Vb6Value(-9L)]);
    }

    [Fact]
    public async Task DatePart_QuarterDayOfYearAndWeekOfYear()
    {
        await Run(
            "Debug.Print DatePart(\"q\", #3/15/2020#)\n" +   // 1
            "Debug.Print DatePart(\"y\", #2/1/2020#)\n" +    // 32 (day of year)
            "Debug.Print DatePart(\"ww\", #1/1/2020#)\n" +   // 1
            "Debug.Print DatePart(\"ww\", #12/31/2020#)\n"); // 53
        AssertDebugLog([1, 32, 1, 53]);
    }

    [Fact]
    public async Task MonthName_And_WeekdayName()
    {
        await Run(
            "Debug.Print MonthName(1)\n" +
            "Debug.Print MonthName(1, True)\n" +
            "Debug.Print MonthName(12)\n" +
            "Debug.Print WeekdayName(1, False, vbSunday)\n" +   // explicit fdow avoids the locale-default
            "Debug.Print WeekdayName(2, False, vbSunday)\n");
        AssertDebugLog(["January", "Jan", "December", "Sunday", "Monday"]);
    }

    [Fact]
    public async Task Clock_ReturnsRightTypes_AndIsCoherent()
    {
        await Run(
            "Debug.Print TypeName(Now)\n" +          // Date
            "Debug.Print TypeName(Timer)\n" +        // Single
            "Debug.Print (Year(Now) = Year(Date))\n");
        AssertDebugLog(["Date", "Single", true]);
    }
}
