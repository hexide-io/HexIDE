using System.Globalization;
using System.Text.RegularExpressions;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A String read as a Date, by <c>CDate</c>, by a store into a Date, and by <c>IsDate</c> (#967, #800). Each row is the line
/// <c>vb6.exe</c> printed under en-GB, native and p-code agreeing, from <see cref="Program"/> run with that string (the
/// oracle's "A String read as a Date"). VB6 gives CDate and the store one answer for every string measured.
/// </summary>
public class StringToDateTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    // The probe's own procedure: what CDate gives, then a store into a Date, then IsDate, each as TypeName, CStr and CDbl.
    private const string Program =
        "Private Function Show(ByVal v As Variant) As String\n" +
        "    Show = TypeName(v) & \" \" & CStr(v) & \" (\" & CStr(CDbl(v)) & \")\"\n" +
        "End Function\n" +
        "Private Sub Row(ByVal s As String)\n" +
        "    Dim v As Variant, d As Date, r As String\n" +
        "    On Error Resume Next\n" +
        "    r = \"[\" & s & \"]\"\n" +
        "    Err.Clear\n" +
        "    v = Empty\n" +
        "    v = CDate(s)\n" +
        "    If Err.Number <> 0 Then r = r & \" CDate:\" & Err.Number Else r = r & \" CDate:\" & Show(v)\n" +
        "    Err.Clear\n" +
        "    d = #1/1/1990#\n" +
        "    d = s\n" +
        "    If Err.Number <> 0 Then r = r & \" | store:\" & Err.Number Else r = r & \" | store:\" & Show(d)\n" +
        "    Err.Clear\n" +
        "    r = r & \" | IsDate:\" & IsDate(s)\n" +
        "    Debug.Print r\n" +
        "End Sub\n";

    // The year the rows were measured in. A string with no year in it is a date in the year it is read.
    private const int MeasuredIn = 2026;

    private static readonly Regex Serial = new(@" \([^)]*\)", RegexOptions.CultureInvariant);

    public static TheoryData<string, string> Measured => new()
    {
        { "1/2/2003", "[1/2/2003] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "abc", "[abc] CDate:13 | store:13 | IsDate:False" },
        { "36526", "[36526] CDate:Date 01/01/2000 (36526) | store:Date 01/01/2000 (36526) | IsDate:False" },
        { "36526.5", "[36526.5] CDate:Date 01/01/2000 12:00:00 (36526.5) | store:Date 01/01/2000 12:00:00 (36526.5) | IsDate:False" },
        { " 2 ", "[ 2 ] CDate:Date 01/01/1900 (2) | store:Date 01/01/1900 (2) | IsDate:False" },
        { "-1", "[-1] CDate:Date 29/12/1899 (-1) | store:Date 29/12/1899 (-1) | IsDate:False" },
        { "1.2.2003", "[1.2.2003] CDate:13 | store:13 | IsDate:False" },
        { "13.1.2003", "[13.1.2003] CDate:13 | store:13 | IsDate:False" },
        { "1-2-2003", "[1-2-2003] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "2003-01-02", "[2003-01-02] CDate:Date 02/01/2003 (37623) | store:Date 02/01/2003 (37623) | IsDate:True" },
        { "1 Feb 2003", "[1 Feb 2003] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "Feb 1, 2003", "[Feb 1, 2003] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "10:30", "[10:30] CDate:Date 10:30:00 (0.4375) | store:Date 10:30:00 (0.4375) | IsDate:True" },
        { "1/2/2003 10:30", "[1/2/2003 10:30] CDate:Date 01/02/2003 10:30:00 (37653.4375) | store:Date 01/02/2003 10:30:00 (37653.4375) | IsDate:True" },
        { "", "[] CDate:13 | store:13 | IsDate:False" },
        { "  ", "[  ] CDate:13 | store:13 | IsDate:False" },
        { "&H10", "[&H10] CDate:Date 15/01/1900 (16) | store:Date 15/01/1900 (16) | IsDate:False" },
        { "1e3", "[1e3] CDate:Date 26/09/1902 (1000) | store:Date 26/09/1902 (1000) | IsDate:False" },
        { "3000000", "[3000000] CDate:13 | store:13 | IsDate:False" },
        { "1,5", "[1,5] CDate:Date 01/05/2026 (46143) | store:Date 01/05/2026 (46143) | IsDate:True" },
        { "True", "[True] CDate:13 | store:13 | IsDate:False" },
        { "12/31/2003", "[12/31/2003] CDate:Date 31/12/2003 (37986) | store:Date 31/12/2003 (37986) | IsDate:True" },
        { "0", "[0] CDate:Date 00:00:00 (0) | store:Date 00:00:00 (0) | IsDate:False" },
        { "0.5", "[0.5] CDate:Date 00:05:00 (3.47222222222222E-03) | store:Date 00:05:00 (3.47222222222222E-03) | IsDate:True" },
        { "31/12/2003", "[31/12/2003] CDate:Date 31/12/2003 (37986) | store:Date 31/12/2003 (37986) | IsDate:True" },
        { "2003/1/2", "[2003/1/2] CDate:Date 02/01/2003 (37623) | store:Date 02/01/2003 (37623) | IsDate:True" },
        { "1/2", "[1/2] CDate:Date 01/02/2026 (46054) | store:Date 01/02/2026 (46054) | IsDate:True" },
        { "1.5", "[1.5] CDate:Date 01:05:00 (4.51388888888889E-02) | store:Date 01:05:00 (4.51388888888889E-02) | IsDate:True" },
        { "$5", "[$5] CDate:13 | store:13 | IsDate:False" },
        { "(3)", "[(3)] CDate:Date 27/12/1899 (-3) | store:Date 27/12/1899 (-3) | IsDate:False" },
        { "2.", "[2.] CDate:Date 01/01/1900 (2) | store:Date 01/01/1900 (2) | IsDate:False" },
        { "  36526  ", "[  36526  ] CDate:Date 01/01/2000 (36526) | store:Date 01/01/2000 (36526) | IsDate:False" },
        { "1.30", "[1.30] CDate:Date 01:30:00 (0.0625) | store:Date 01:30:00 (0.0625) | IsDate:True" },
        { "23.59", "[23.59] CDate:Date 23:59:00 (0.999305555555556) | store:Date 23:59:00 (0.999305555555556) | IsDate:True" },
        { "24.0", "[24.0] CDate:Date 23/01/1900 (24) | store:Date 23/01/1900 (24) | IsDate:False" },
        { "25.5", "[25.5] CDate:Date 24/01/1900 12:00:00 (25.5) | store:Date 24/01/1900 12:00:00 (25.5) | IsDate:False" },
        { "1.60", "[1.60] CDate:Date 31/12/1899 14:24:00 (1.6) | store:Date 31/12/1899 14:24:00 (1.6) | IsDate:False" },
        { "1.5.3", "[1.5.3] CDate:Date 01:05:03 (4.51736111111111E-02) | store:Date 01:05:03 (4.51736111111111E-02) | IsDate:True" },
        { "1.5 PM", "[1.5 PM] CDate:Date 13:05:00 (0.545138888888889) | store:Date 13:05:00 (0.545138888888889) | IsDate:True" },
        { "10.30.15", "[10.30.15] CDate:Date 10:30:15 (0.437673611111111) | store:Date 10:30:15 (0.437673611111111) | IsDate:True" },
        { "0.25", "[0.25] CDate:Date 00:25:00 (1.73611111111111E-02) | store:Date 00:25:00 (1.73611111111111E-02) | IsDate:True" },
        { "1.2.3", "[1.2.3] CDate:Date 01:02:03 (4.30902777777778E-02) | store:Date 01:02:03 (4.30902777777778E-02) | IsDate:True" },
        { "100.5", "[100.5] CDate:Date 09/04/1900 12:00:00 (100.5) | store:Date 09/04/1900 12:00:00 (100.5) | IsDate:False" },
        { "-1.5", "[-1.5] CDate:Date 29/12/1899 12:00:00 (-1.5) | store:Date 29/12/1899 12:00:00 (-1.5) | IsDate:False" },
        { "1.5e1", "[1.5e1] CDate:Date 14/01/1900 (15) | store:Date 14/01/1900 (15) | IsDate:False" },
        { "1.05", "[1.05] CDate:Date 01:05:00 (4.51388888888889E-02) | store:Date 01:05:00 (4.51388888888889E-02) | IsDate:True" },
        { "12.5.30", "[12.5.30] CDate:Date 12:05:30 (0.503819444444444) | store:Date 12:05:30 (0.503819444444444) | IsDate:True" },
        { "1,2003", "[1,2003] CDate:Date 01/01/2003 (37622) | store:Date 01/01/2003 (37622) | IsDate:True" },
        { "1,5,2003", "[1,5,2003] CDate:Date 01/05/2003 (37742) | store:Date 01/05/2003 (37742) | IsDate:True" },
        { "13,1", "[13,1] CDate:Date 13/01/2026 (46035) | store:Date 13/01/2026 (46035) | IsDate:True" },
        { "1,13", "[1,13] CDate:Date 13/01/2026 (46035) | store:Date 13/01/2026 (46035) | IsDate:True" },
        { "1,5,3", "[1,5,3] CDate:Date 01/05/2003 (37742) | store:Date 01/05/2003 (37742) | IsDate:True" },
        { "2958465", "[2958465] CDate:Date 31/12/9999 (2958465) | store:Date 31/12/9999 (2958465) | IsDate:False" },
        { "2958466", "[2958466] CDate:13 | store:13 | IsDate:False" },
        { "-657434", "[-657434] CDate:Date 01/01/100 (-657434) | store:Date 01/01/100 (-657434) | IsDate:False" },
        { "-657435", "[-657435] CDate:13 | store:13 | IsDate:False" },
        { "2958465.5", "[2958465.5] CDate:Date 31/12/9999 12:00:00 (2958465.5) | store:Date 31/12/9999 12:00:00 (2958465.5) | IsDate:False" },
        { "1.5.2003 10:30", "[1.5.2003 10:30] CDate:13 | store:13 | IsDate:False" },
        { "10:30.5", "[10:30.5] CDate:Date 10:30:05 (0.43755787037037) | store:Date 10:30:05 (0.43755787037037) | IsDate:True" },
        { "1:2:3", "[1:2:3] CDate:Date 01:02:03 (4.30902777777778E-02) | store:Date 01:02:03 (4.30902777777778E-02) | IsDate:True" },
        { "1 2 2003", "[1 2 2003] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "1/2/3", "[1/2/3] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "12:00 PM", "[12:00 PM] CDate:Date 12:00:00 (0.5) | store:Date 12:00:00 (0.5) | IsDate:True" },
        { "13:00 PM", "[13:00 PM] CDate:Date 13:00:00 (0.541666666666667) | store:Date 13:00:00 (0.541666666666667) | IsDate:True" },
        { "1e400", "[1e400] CDate:13 | store:13 | IsDate:False" },
        { "1.", "[1.] CDate:Date 31/12/1899 (1) | store:Date 31/12/1899 (1) | IsDate:False" },
        { ".5", "[.5] CDate:Date 12:00:00 (0.5) | store:Date 12:00:00 (0.5) | IsDate:False" },
        { "1d2", "[1d2] CDate:Date 09/04/1900 (100) | store:Date 09/04/1900 (100) | IsDate:False" },
        { "&O17", "[&O17] CDate:Date 14/01/1900 (15) | store:Date 14/01/1900 (15) | IsDate:False" },
        { "1/2/2003 1.5", "[1/2/2003 1.5] CDate:Date 01/02/2003 01:05:00 (37653.0451388889) | store:Date 01/02/2003 01:05:00 (37653.0451388889) | IsDate:True" },
        { "Jan 2003", "[Jan 2003] CDate:Date 01/01/2003 (37622) | store:Date 01/01/2003 (37622) | IsDate:True" },
        { "2003", "[2003] CDate:Date 25/06/1905 (2003) | store:Date 25/06/1905 (2003) | IsDate:False" },
        { "30/2/2003", "[30/2/2003] CDate:13 | store:13 | IsDate:False" },
        { "13.1.2003", "[13.1.2003] CDate:13 | store:13 | IsDate:False" },
        { "1/2/2003", "[1/2/2003] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "1,5", "[1,5] CDate:Date 01/05/2026 (46143) | store:Date 01/05/2026 (46143) | IsDate:True" },
        { "1.5", "[1.5] CDate:Date 01:05:00 (4.51388888888889E-02) | store:Date 01:05:00 (4.51388888888889E-02) | IsDate:True" },
        { "1,000", "[1,000] CDate:Date 01/01/2000 (36526) | store:Date 01/01/2000 (36526) | IsDate:True" },
        { "1,000.5", "[1,000.5] CDate:Date 26/09/1902 12:00:00 (1000.5) | store:Date 26/09/1902 12:00:00 (1000.5) | IsDate:False" },
        { "12,345", "[12,345] CDate:Date 01/12/345 (-567616) | store:Date 01/12/345 (-567616) | IsDate:True" },
        { "123,456", "[123,456] CDate:Date 03/01/2238 (123456) | store:Date 03/01/2238 (123456) | IsDate:False" },
        { "1,5.5", "[1,5.5] CDate:Date 14/01/1900 12:00:00 (15.5) | store:Date 14/01/1900 12:00:00 (15.5) | IsDate:False" },
        { "£5", "[£5] CDate:13 | store:13 | IsDate:False" },
        { "5%", "[5%] CDate:13 | store:13 | IsDate:False" },
        { "+5", "[+5] CDate:Date 04/01/1900 (5) | store:Date 04/01/1900 (5) | IsDate:False" },
        { "5-", "[5-] CDate:Date 25/12/1899 (-5) | store:Date 25/12/1899 (-5) | IsDate:False" },
        { "1 000", "[1 000] CDate:Date 01/01/2000 (36526) | store:Date 01/01/2000 (36526) | IsDate:True" },
        { "1,00", "[1,00] CDate:Date 01/01/2000 (36526) | store:Date 01/01/2000 (36526) | IsDate:True" },
        { "31,12", "[31,12] CDate:Date 31/12/2026 (46387) | store:Date 31/12/2026 (46387) | IsDate:True" },
        { "31,13", "[31,13] CDate:Date 09/07/1908 (3113) | store:Date 09/07/1908 (3113) | IsDate:False" },
        { "0,5", "[0,5] CDate:Date 01/05/2000 (36647) | store:Date 01/05/2000 (36647) | IsDate:True" },
        { "1,5,2003 10:30", "[1,5,2003 10:30] CDate:Date 01/05/2003 10:30:00 (37742.4375) | store:Date 01/05/2003 10:30:00 (37742.4375) | IsDate:True" },
        { "Feb 1,2003", "[Feb 1,2003] CDate:Date 01/02/2003 (37653) | store:Date 01/02/2003 (37653) | IsDate:True" },
        { "1, 5", "[1, 5] CDate:Date 01/05/2026 (46143) | store:Date 01/05/2026 (46143) | IsDate:True" },
        { "1 ,5", "[1 ,5] CDate:Date 01/05/2026 (46143) | store:Date 01/05/2026 (46143) | IsDate:True" },
        { "2003,1,2", "[2003,1,2] CDate:Date 02/01/2003 (37623) | store:Date 02/01/2003 (37623) | IsDate:True" },
        { "1,2,3,4", "[1,2,3,4] CDate:Date 18/05/1903 (1234) | store:Date 18/05/1903 (1234) | IsDate:False" },
        { "10:30,5", "[10:30,5] CDate:13 | store:13 | IsDate:False" },
        { "5,1.5", "[5,1.5] CDate:Date 19/02/1900 12:00:00 (51.5) | store:Date 19/02/1900 12:00:00 (51.5) | IsDate:False" },
        { "1,5 PM", "[1,5 PM] CDate:13 | store:13 | IsDate:False" },
        // Two numbers, measured for #1447's review: which of day-month, month-day, month-year and year-month VB6 takes.
        { "31,2", "[31,2] CDate:Date 01/02/2031 (47880) | store:Date 01/02/2031 (47880) | IsDate:True" },
        { "31/2", "[31/2] CDate:Date 01/02/2031 (47880) | store:Date 01/02/2031 (47880) | IsDate:True" },
        { "32,12", "[32,12] CDate:Date 01/12/2032 (48549) | store:Date 01/12/2032 (48549) | IsDate:True" },
        { "5,0", "[5,0] CDate:Date 01/05/2000 (36647) | store:Date 01/05/2000 (36647) | IsDate:True" },
        { "0,13", "[0,13] CDate:Date 12/01/1900 (13) | store:Date 12/01/1900 (13) | IsDate:False" },
        { "13/2003", "[13/2003] CDate:13 | store:13 | IsDate:False" },
        { "2,31", "[2,31] CDate:Date 01/02/2031 (47880) | store:Date 01/02/2031 (47880) | IsDate:True" },
        { "2/31", "[2/31] CDate:Date 01/02/2031 (47880) | store:Date 01/02/2031 (47880) | IsDate:True" },
        { "12,99", "[12,99] CDate:Date 01/12/1999 (36495) | store:Date 01/12/1999 (36495) | IsDate:True" },
        { "1,50", "[1,50] CDate:Date 01/01/1950 (18264) | store:Date 01/01/1950 (18264) | IsDate:True" },
        { "6 99", "[6 99] CDate:Date 01/06/1999 (36312) | store:Date 01/06/1999 (36312) | IsDate:True" },
        { "13,5", "[13,5] CDate:Date 13/05/2026 (46155) | store:Date 13/05/2026 (46155) | IsDate:True" },
        { "31,1", "[31,1] CDate:Date 31/01/2026 (46053) | store:Date 31/01/2026 (46053) | IsDate:True" },
        { "45,6", "[45,6] CDate:Date 01/06/2045 (53114) | store:Date 01/06/2045 (53114) | IsDate:True" },
        { "99,12", "[99,12] CDate:Date 01/12/1999 (36495) | store:Date 01/12/1999 (36495) | IsDate:True" },
        { "100,5", "[100,5] CDate:Date 01/05/100 (-657314) | store:Date 01/05/100 (-657314) | IsDate:True" },
        { "5,100", "[5,100] CDate:Date 01/05/100 (-657314) | store:Date 01/05/100 (-657314) | IsDate:True" },
        { "0,0", "[0,0] CDate:Date 00:00:00 (0) | store:Date 00:00:00 (0) | IsDate:False" },
        { "0/1", "[0/1] CDate:Date 01/01/2000 (36526) | store:Date 01/01/2000 (36526) | IsDate:True" },
        { "1/0", "[1/0] CDate:Date 01/01/2000 (36526) | store:Date 01/01/2000 (36526) | IsDate:True" },
        { "13,13", "[13,13] CDate:Date 05/08/1903 (1313) | store:Date 05/08/1903 (1313) | IsDate:False" },
        { "29,2", "[29,2] CDate:Date 01/02/2029 (47150) | store:Date 01/02/2029 (47150) | IsDate:True" },
        { "2,29", "[2,29] CDate:Date 01/02/2029 (47150) | store:Date 01/02/2029 (47150) | IsDate:True" },
        { "30 2", "[30 2] CDate:Date 01/02/2030 (47515) | store:Date 01/02/2030 (47515) | IsDate:True" },
    };

    private async Task<string> RowOf(string text)
    {
        using (VbLocale.Use(EnGb))
            await Run(Program + "Row \"" + text + "\"\n");
        return (string)debug.Single().Value!;
    }

    [Theory]
    [MemberData(nameof(Measured))]
    public async Task A_String_reads_as_a_Date_as_VB6_reads_it(string text, string vb6)
    {
        var printed = await RowOf(text);

        // A date in the year it is read is compared in the measured year, and without the serials, which follow the year.
        if (vb6.Contains("/" + MeasuredIn, StringComparison.Ordinal))
        {
            vb6 = Serial.Replace(vb6, "");
            printed = Serial.Replace(printed.Replace("/" + DateTime.Now.Year, "/" + MeasuredIn, StringComparison.Ordinal), "");
        }
        printed.Should().Be(vb6);
    }

    // Each a statement from the same probe, and the line vb6.exe printed for it.
    public static TheoryData<string, string> MeasuredThroughFunctions => new()
    {
        { "Err.Clear: a(0) = \"1/2/2003\": a(1) = \"36526\": Debug.Print \"array \" & Show(a(0)) & \" / \" & Show(a(1)) & \" err \" & Err.Number", "array Date 01/02/2003 (37653) / Date 01/01/2000 (36526) err 0" },
        { "Err.Clear: v = Empty: v = CVDate(\"36526\"): Debug.Print \"CVDate(36526) \" & Show(v) & \" err \" & Err.Number", "CVDate(36526) Date 01/01/2000 (36526) err 0" },
        { "Err.Clear: v = Empty: v = CVDate(\"1.5\"): Debug.Print \"CVDate(1.5) \" & Show(v) & \" err \" & Err.Number", "CVDate(1.5) Date 01:05:00 (4.51388888888889E-02) err 0" },
        { "Err.Clear: v = Empty: v = DateAdd(\"d\", 1, \"36526\"): Debug.Print \"DateAdd 36526 \" & Show(v) & \" err \" & Err.Number", "DateAdd 36526 Date 02/01/2000 (36527) err 0" },
        { "Err.Clear: v = Empty: v = DateAdd(\"d\", 1, \"1.5\"): Debug.Print \"DateAdd 1.5 \" & Show(v) & \" err \" & Err.Number", "DateAdd 1.5 Date 31/12/1899 01:05:00 (1.04513888888889) err 0" },
        { "Err.Clear: v = Empty: v = DateAdd(\"d\", 1, \"1,000\"): Debug.Print \"DateAdd 1,000 \" & Show(v) & \" err \" & Err.Number", "DateAdd 1,000 Date 02/01/2000 (36527) err 0" },
        { "Err.Clear: v = Empty: v = DateAdd(\"d\", 1, \"2958466\"): Debug.Print \"DateAdd 2958466 err \" & Err.Number", "DateAdd 2958466 err 13" },
        { "Err.Clear: v = Empty: v = Year(\"36526\"): Debug.Print \"Year 36526 \" & v & \" err \" & Err.Number", "Year 36526 2000 err 0" },
        { "Err.Clear: v = Empty: v = Year(\"1,000\"): Debug.Print \"Year 1,000 \" & v & \" err \" & Err.Number", "Year 1,000 2000 err 0" },
        { "Err.Clear: v = Empty: v = Month(\"0,5\"): Debug.Print \"Month 0,5 \" & v & \" err \" & Err.Number", "Month 0,5 5 err 0" },
        { "Err.Clear: v = Empty: v = Hour(\"1.5\"): Debug.Print \"Hour 1.5 \" & v & \" err \" & Err.Number", "Hour 1.5 1 err 0" },
        { "Err.Clear: v = Empty: v = Weekday(\"36526\"): Debug.Print \"Weekday 36526 \" & v & \" err \" & Err.Number", "Weekday 36526 7 err 0" },
        { "Err.Clear: v = Empty: v = DateDiff(\"d\", \"36526\", \"36527\"): Debug.Print \"DateDiff \" & v & \" err \" & Err.Number", "DateDiff 1 err 0" },
        { "Err.Clear: v = Empty: v = DatePart(\"yyyy\", \"1,000\"): Debug.Print \"DatePart 1,000 \" & v & \" err \" & Err.Number", "DatePart 1,000 2000 err 0" },
        { "Err.Clear: v = Empty: v = DateValue(\"36526\"): Debug.Print \"DateValue 36526 \" & Show(v) & \" err \" & Err.Number", "DateValue 36526 Empty  (0) err 13" },
        { "Err.Clear: v = Empty: v = DateValue(\"1/2/2003 10:30\"): Debug.Print \"DateValue 1/2/2003 10:30 \" & Show(v) & \" err \" & Err.Number", "DateValue 1/2/2003 10:30 Date 01/02/2003 (37653) err 0" },
        { "Err.Clear: v = Empty: v = TimeValue(\"1.5\"): Debug.Print \"TimeValue 1.5 \" & Show(v) & \" err \" & Err.Number", "TimeValue 1.5 Date 01:05:00 (4.51388888888889E-02) err 0" },
        { "Err.Clear: v = Empty: v = TimeValue(\"36526.5\"): Debug.Print \"TimeValue 36526.5 \" & Show(v) & \" err \" & Err.Number", "TimeValue 36526.5 Empty  (0) err 13" },
        { "Err.Clear: v = Empty: v = Format(\"36526\", \"dd/mm/yyyy\"): Debug.Print \"Format 36526 \" & v & \" err \" & Err.Number", "Format 36526 01/01/2000 err 0" },
        { "Err.Clear: v = Empty: v = Format(\"1.5\", \"hh:nn\"): Debug.Print \"Format 1.5 \" & v & \" err \" & Err.Number", "Format 1.5 01:05 err 0" },
    };

    [Theory]
    [MemberData(nameof(MeasuredThroughFunctions))]
    public async Task A_date_function_given_a_String_reads_it_as_CDate_does_but_DateValue_and_TimeValue_take_no_serial(
        string statement, string vb6)
    {
        // HexIDE gave 13 for every serial here, and had no CVDate; DateValue and TimeValue are 13 for a serial in VB6 too.
        using (VbLocale.Use(EnGb))
            await Run(Program + "Dim a(1) As Date, v As Variant\nOn Error Resume Next\n" + statement + "\n");

        ((string)debug.Single().Value!).Should().Be(vb6);
    }
}
