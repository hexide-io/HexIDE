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

    public static IEnumerable<object[]> UnmeasuredRows()
    {
        foreach (var statement in new[] { "Date", "Time", "Date$", "Time$" })
            foreach (var value in new[] { "CInt(1)", "CLng(1)", "CCur(1)", "Empty", "True", "CSng(1)", "CDec(1)", "New Collection" })
                yield return [statement, value];
    }

    [Theory]
    [MemberData(nameof(UnmeasuredRows))]
    public async Task UnmeasuredTypesExplicitlyRefuse(string statement, string value)
    {
        var run = () => Run($"{statement} = {value}");
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*#4681*");
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
