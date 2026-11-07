namespace HexIDE.Runtime.Tests;

/// <summary>Array locking measured in #1460 (en-GB, native and p-code); release is the loop's lifetime requirement.</summary>
public class ForEachArrayLockTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("ReDim a(6)", "Long", 2)]
    [InlineData("ReDim Preserve a(5)", "Long", 2)]
    [InlineData("Erase a", "Variant", 1)]
    public async Task Mutation_raises_10_and_keeps_bounds(string mutation, string type, int upper)
    {
        await Run($"""
            Dim a() As {type}, x As Variant
            ReDim a({upper})
            On Error Resume Next
            For Each x In a
                {mutation}
                Exit For
            Next
            Debug.Print CStr(Err.Number)
            Debug.Print CStr(LBound(a)) & ":" & CStr(UBound(a))
            """);
        AssertDebugLog(["10", $"0:{upper}"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Exit For")]
    [InlineData("GoTo done")]
    public async Task Leaving_loop_releases_array(string leave)
    {
        await Run($"""
            Dim a() As Long, x As Variant
            ReDim a(2)
            For Each x In a
                {leave}
            Next
            done:
            On Error Resume Next
            ReDim a(6)
            Debug.Print CStr(Err.Number) & ":" & CStr(UBound(a))
            """);
        AssertDebugLog(["0:6"]);
    }

    [Theory]
    [InlineData("Sub", "Exit Sub")]
    [InlineData("Function", "Exit Function")]
    [InlineData("Sub", "Err.Raise 5")]
    public async Task Leaving_procedure_releases_array(string kind, string leave)
    {
        await Run($"""
            Dim a() As Long
            Private {kind} Walk()
                Dim x As Variant
                For Each x In a
                    {leave}
                Next
            End {kind}
            ReDim a(2)
            On Error Resume Next
            Walk
            Err.Clear
            ReDim a(6)
            Debug.Print CStr(Err.Number) & ":" & CStr(UBound(a))
            """);
        AssertDebugLog(["0:6"]);
    }
}
