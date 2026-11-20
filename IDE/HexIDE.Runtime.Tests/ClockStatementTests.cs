using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code, measured 2026-11-18: https://github.com/hexide-io/HexIDE/issues/4681.
// Error 91 text: docs/vb6-fidelity-oracle/a-member-used-on-something-that-is-not-an-object.md.
public class ClockStatementTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> MeasuredRows()
    {
        foreach (var statement in new[] { "Date", "Time", "Date$", "Time$" })
        {
            foreach (var (setup, value, error) in new (string, string, int)[]
            {
                ("", "Date", 70),
                ("", "CStr(Date)", 70),
                ("", "Now", 70),
                ("Dim v As Variant\nv = Date\n", "v", 70),
                ("Dim v As Variant\nv = CStr(Date)\n", "v", 70),
                ("", "CDbl(Date)", 13),
                ("", "\"garbage\"", 13),
                ("", "\"\"", 13),
                ("", "Null", 13),
                ("Dim a(1) As Integer\n", "a", 13),
                ("Dim o As Object\n", "o", 91)
            })
                yield return [statement, setup, value, error];
        }
    }

    // SP6 native and p-code, Basic User: https://github.com/hexide-io/HexIDE/issues/4733
    // Measurement: https://github.com/hexide-io/HexIDE/pull/4777.
    public static IEnumerable<object[]> AdditionalMeasuredRows()
    {
        foreach (var statement in new[] { "Date", "Time", "Date$", "Time$" })
            foreach (var (value, error) in new (string, int)[]
            {
                ("CInt(5)", 13), ("CLng(5)", 13), ("CByte(5)", 13), ("CSng(5)", 13),
                ("CLng(Date)", 13), ("CCur(CLng(Date))", 13), ("CDec(CLng(Date))", 13),
                ("-1", 13), ("True", 13), ("Empty", 13), ("CVErr(5)", 13),
                ("CInt(0)", 13), ("CSng(0.5)", 13), ("CCur(0.5)", 13),
                ("\"12/31/9999\"", 70), ("\"1/1/100\"", 70), ("\"1/1/99\"", 70),
                ("CDate(-1)", 70), ("\"1/1/10000\"", 13), ("\"25:00\"", 13)
            })
                yield return [statement, value, error];
    }

    [Theory]
    [MemberData(nameof(AdditionalMeasuredRows))]
    public async Task AdditionalMeasuredConversionBeforePermission(string statement, string value, int error)
    {
        await Run($"On Error Resume Next\n{statement} = {value}\nDebug.Print Err.Number");
        AssertDebugLog([new Vb6Value((long)error)]);
    }

    [Theory]
    [InlineData("Date", "ClockValue")]
    [InlineData("Date", "Object")]
    [InlineData("Time", "ClockValue")]
    [InlineData("Time", "Object")]
    [InlineData("Date$", "ClockValue")]
    [InlineData("Date$", "Object")]
    [InlineData("Time$", "ClockValue")]
    [InlineData("Time$", "Object")]
    public async Task DateDefaultMemberIsRead(string statement, string declaredType)
    {
        await RunClasses($"Dim c As {declaredType}\nSet c = New ClockValue\nOn Error Resume Next\n{statement} = c\nDebug.Print Err.Number",
            ("ClockValue", "Public Function Value() As Date\nAttribute Value.VB_UserMemId = 0\nValue = Date\nEnd Function\n"));
        AssertDebugLog([new Vb6Value(70L)]);
    }

    [Theory]
    [InlineData("Date")]
    [InlineData("Time")]
    [InlineData("Date$")]
    [InlineData("Time$")]
    public async Task UnmeasuredObjectsExplicitlyRefuse(string statement)
    {
        foreach (var body in new[] { "Public Tag As Long\n",
            "Public Function Value() As Long\nAttribute Value.VB_UserMemId = 0\nValue = 5\nEnd Function\n" })
        {
            var run = () => RunClasses($"Dim c As ClockValue\nSet c = New ClockValue\n{statement} = c",
                ("ClockValue", body));
            await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4733*");
        }
    }

    [Theory]
    [InlineData("Date")]
    [InlineData("Time")]
    [InlineData("Date$")]
    [InlineData("Time$")]
    public async Task UnmeasuredNestedDefaultExplicitlyRefuses(string statement)
    {
        var run = () => RunClasses($"Dim c As ClockValue\nSet c = New ClockValue\n{statement} = c",
            ("ClockValue", "Public Function Value() As Object\nAttribute Value.VB_UserMemId = 0\nSet Value = Nothing\nEnd Function\n"));
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4733*");
    }

    [Theory]
    [MemberData(nameof(MeasuredRows))]
    public async Task MeasuredConversionBeforePermission(string statement, string setup, string value, int error)
    {
        var before = DateTime.Now;
        await Run(setup + $"""
            On Error Resume Next
            {statement} = {value}
            Debug.Print Err.Number
            Debug.Print Err.Description
            Debug.Print CDbl(Date)
            Debug.Print CDbl(Time)
            """);
        var after = DateTime.Now;
        debug[0].Should().Be(new Vb6Value((long)error));
        debug[1].Should().Be(new Vb6Value(error == 70 ? "Permission denied" : error == 13 ? "Type mismatch" : "Object variable or With block variable not set"));
        ((double)debug[2].Value!).Should().BeOneOf(before.Date.ToOADate(), after.Date.ToOADate());
        var time = (double)debug[3].Value!;
        // Time reads whole seconds; allow the second containing either end, including midnight.
        var start = Math.Floor(before.TimeOfDay.TotalSeconds) / 86400;
        var end = Math.Floor(after.TimeOfDay.TotalSeconds) / 86400;
        (start <= end ? time >= start && time <= end : time >= start || time <= end).Should().BeTrue();
    }
}
