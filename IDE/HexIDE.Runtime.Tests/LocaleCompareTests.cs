using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6, native and p-code: https://github.com/hexide-io/HexIDE/issues/4151,
// Oracle page: option-compare-text-and-vb6s-text-compare.md (measuring PR #4188).
public class LocaleCompareTests : BaseVBTestFixture
{
    public static IEnumerable<object[]> Searches()
    {
        int[] ids = [0, 1, 2, 3, 1033, 1031, 999999, -2];
        string[] sources = ["xxaBb", "Straße", "xÆy", "co-op", ""];
        string[] needles = ["b", "ss", "ae", "coop", "a"];
        int[][] results = [
            [5, 4, -5, 4, 4, 4, -5, -5],
            [0, 5, -5, 0, 5, 5, -5, -5],
            [0, 2, -5, 0, 2, 2, -5, -5],
            [0, 0, -5, 0, 0, 0, -5, -5],
            [0, 0, 0, 0, 0, 0, 0, 0]];
        for (var r = 0; r < sources.Length; r++)
            for (var c = 0; c < ids.Length; c++)
                yield return [$"InStr(1, \"{sources[r]}\", \"{needles[r]}\", {ids[c]})", results[r][c]];
        int[] wider = [1, 3, 1027, 4, 2052, 5, 1029, 7, 1031, 9, 1033, 2057, 100, 1024, 2048];
        foreach (var id in wider)
        {
            bool sharpS = id is 1 or 1029 or 1031 or 1033 or 2057;
            bool ligature = id is 1 or 1031 or 1033 or 2057;
            yield return [$"InStr(1, \"Straße\", \"ss\", {id})", sharpS ? 5 : 0];
            yield return [$"InStr(1, \"xÆy\", \"ae\", {id})", ligature ? 2 : 0];
            yield return [$"InStr(1, \"ß\", \"s\", {id})", sharpS ? 1 : 0];
            yield return [$"InStr(1, \"xxaBb\", \"b\", {id})", 4];
        }
    }

    [Theory]
    [MemberData(nameof(Searches))]
    public async Task InStr_matches_the_measured_locale_columns(string expression, int expected) =>
        await Check(expression, expected);

    [WindowsOnlyFact]
    public async Task StrComp_matches_every_measured_cell_on_Windows()
    {
        // The default-LCID column was measured with the VM locale set to en-GB.
        int[] ids = [0, 1, 2, 3, 4, 5, 100, 1024, 1033, 1031, 1053, 1029, 2057, 1041, 999999, -2, 65535];
        string[] left = ["a", "a", "ä", "ö", "ß", "Æ", "co-op", "ch", ""];
        string[] right = ["B", "A", "b", "z", "ss", "AE", "coop", "d", "a"];
        int[] binary = [1, 1, 1, 1, 1, 1, -1, -1, -1];
        int[] text = [-1, 0, -1, -1, 0, 0, 1, -1, -1];
        for (var r = 0; r < left.Length; r++)
            foreach (var id in ids)
            {
                int expected = id == 0 ? binary[r] : text[r];
                if (id == 2 || (r != 8 && id is 999999 or -2 or 65535)) expected = -5;
                if (r is 2 or 3 && id == 1053) expected = 1;
                if (r == 7 && id is 5 or 1029) expected = 1;
                await Check($"StrComp(\"{left[r]}\", \"{right[r]}\", {id})", expected);
            }
        foreach (var id in new[] { 1, 3, 1027, 4, 2052, 5, 1029, 7, 1031, 9, 1033, 2057, 100, 1024, 2048 })
            await Check($"StrComp(\"ß\", \"ss\", {id})", 0);
        foreach (var id in new[] { 4096, 2048, 1027, 2052, 7, 9 })
            await Check($"StrComp(\"a\", \"A\", {id})", 0);
        foreach (var id in new[] { 3, 4, 1033 })
            await Check($"StrComp(\"a\", \"a\", {id})", 0);
        foreach (var id in new[] { 3000, 4095, 32767, 65536 })
            await Check($"StrComp(\"a\", \"A\", {id})", -5);
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    [Fact(Skip = "The refusal is specific to non-Windows hosts.", SkipWhen = nameof(IsWindows))]
    public async Task StrComp_locale_order_is_refused_off_Windows()
    {
        foreach (var id in new[] { 3, 4, 1033, 3000 })
        {
            var run = () => Run($"Debug.Print StrComp(\"a\", \"a\", {id})");
            (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#4151*");
        }
    }

    [Fact]
    public async Task Unmeasured_search_ID_is_still_refused()
    {
        var run = () => Run("Debug.Print InStr(1, \"a\", \"A\", 1041)");
        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*#4151*");
    }

    private async Task Check(string expression, int expected)
    {
        await Run($"""
            On Error Resume Next
            Err.Clear
            Dim result As Variant
            result = {expression}
            If Err.Number <> 0 Then
                Debug.Print CStr(-Err.Number)
            Else
                Debug.Print CStr(result)
            End If
            """);
        using var scope = new AwesomeAssertions.Execution.AssertionScope(expression);
        AssertDebugLog([new Vb6Value(expected.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        debug.Clear();
    }
}
