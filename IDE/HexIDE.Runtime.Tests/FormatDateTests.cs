using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 3.7.3 — Format date/time masks. Every expected value pinned against vb6.exe. Custom tokens render to
/// digits, English month/day names, or AM/PM, all of which agree across en-* and Invariant on CI. The named
/// (locale) formats route through the culture and are covered structurally, not by exact string.
/// </summary>
public class FormatDateTests : BaseVBTestFixture
{
    // #3/5/2020 2:07:09 PM# is a Thursday.
    private const string D1 = "Dim d As Date\nd = #3/5/2020 2:07:09 PM#\n";
    private const string D2 = "Dim e As Date\ne = #1/2/2020 9:05:06 AM#\n";

    [Fact]
    public async Task DayMonthYearTokens()
    {
        await Run(D1 +
            "Debug.Print Format(d, \"d\")\n" +      // 5
            "Debug.Print Format(d, \"dd\")\n" +     // 05
            "Debug.Print Format(d, \"ddd\")\n" +    // Thu
            "Debug.Print Format(d, \"dddd\")\n" +   // Thursday
            "Debug.Print Format(d, \"m\")\n" +      // 3
            "Debug.Print Format(d, \"mm\")\n" +     // 03
            "Debug.Print Format(d, \"mmm\")\n" +    // Mar
            "Debug.Print Format(d, \"mmmm\")\n" +   // March
            "Debug.Print Format(d, \"yy\")\n" +     // 20
            "Debug.Print Format(d, \"yyyy\")\n");   // 2020
        AssertDebugLog(["5", "05", "Thu", "Thursday", "3", "03", "Mar", "March", "20", "2020"]);
    }

    [Fact]
    public async Task TimeTokens_And_AmPm()
    {
        await Run(D1 +
            "Debug.Print Format(d, \"h\")\n" +        // 14 (24-hour without AM/PM)
            "Debug.Print Format(d, \"hh\")\n" +       // 14
            "Debug.Print Format(d, \"n\")\n" +        // 7
            "Debug.Print Format(d, \"nn\")\n" +       // 07
            "Debug.Print Format(d, \"s\")\n" +        // 9
            "Debug.Print Format(d, \"ss\")\n" +       // 09
            "Debug.Print Format(d, \"AM/PM\")\n" +    // PM
            "Debug.Print Format(d, \"am/pm\")\n" +    // pm
            "Debug.Print Format(d, \"A/P\")\n" +      // P
            "Debug.Print Format(d, \"a/p\")\n" +      // p
            "Debug.Print Format(d, \"h AM/PM\")\n");  // 2 PM (12-hour once AM/PM present)
        AssertDebugLog(["14", "14", "7", "07", "9", "09", "PM", "pm", "P", "p", "2 PM"]);
    }

    [Fact]
    public async Task SingleDigit_And_MorningValues()
    {
        await Run(D2 +
            "Debug.Print Format(e, \"d\")\n" +      // 2
            "Debug.Print Format(e, \"dd\")\n" +     // 02
            "Debug.Print Format(e, \"h\")\n" +      // 9
            "Debug.Print Format(e, \"hh\")\n" +     // 09
            "Debug.Print Format(e, \"AM/PM\")\n");  // AM
        AssertDebugLog(["2", "02", "9", "09", "AM"]);
    }

    [Fact]
    public async Task MinuteVsMonth_Ambiguity()
    {
        await Run(D1 +
            "Debug.Print Format(d, \"hh:mm\")\n" +     // 14:07  (mm after hh = minute)
            "Debug.Print Format(d, \"hh:mm:ss\")\n" +  // 14:07:09
            "Debug.Print Format(d, \"mm\")\n" +        // 03     (standalone = month)
            "Debug.Print Format(d, \"m/d/yy\")\n");    // 3/5/20 (m = month here)
        AssertDebugLog(["14:07", "14:07:09", "03", "3/5/20"]);
    }

    [Fact]
    public async Task CompositeMasks_And_CalendarParts()
    {
        await Run(D1 +
            "Debug.Print Format(d, \"yyyy-mm-dd hh:nn:ss\")\n" +  // 2020-03-05 14:07:09
            "Debug.Print Format(d, \"mm/dd/yyyy\")\n" +          // 03/05/2020
            "Debug.Print Format(d, \"mmm d, yyyy\")\n" +         // Mar 5, 2020
            "Debug.Print Format(d, \"h:nn AM/PM\")\n" +          // 2:07 PM
            "Debug.Print Format(d, \"q\")\n" +                   // 1
            "Debug.Print Format(d, \"w\")\n" +                   // 5 (Thursday, vbSunday base)
            "Debug.Print Format(d, \"ww\")\n" +                  // 10
            "Debug.Print Format(d, \"y\")\n");                   // 65 (day of year)
        AssertDebugLog(["2020-03-05 14:07:09", "03/05/2020", "Mar 5, 2020", "2:07 PM", "1", "5", "10", "65"]);
    }

    [Fact]
    public async Task NumberUnderDateMask_IsTreatedAsSerial()
    {
        await Run(
            "Debug.Print Format(0, \"yyyy-mm-dd\")\n" +   // 1899-12-30 (OLE serial 0)
            "Debug.Print Format(1, \"yyyy-mm-dd\")\n");   // 1899-12-31
        AssertDebugLog(["1899-12-30", "1899-12-31"]);
    }
}
