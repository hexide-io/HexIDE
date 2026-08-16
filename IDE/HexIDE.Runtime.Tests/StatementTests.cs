using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class StatementTests : BaseVBTestFixture
{
    // ChDir/ChDrive gap-audit fix: the old code used a string-unpack that never succeeds, so they ALWAYS threw
    // (never applied anything). Now oracle-pinned (vb6.exe): ChDir failures → Path Not Found (76); ChDrive with a
    // non-drive-letter first char → Invalid Procedure Call (5); empty ChDrive → no-op. These error/no-op paths do
    // not mutate the process-global working directory, so they're CI-safe (a failed ChDir leaves CWD unchanged).
    [Fact]
    public async Task ChDir_NonexistentPath_RaisesPathNotFound()
    {
        await Run("On Error Resume Next\nChDir \"/no_such_dir_hexide_zzz_9999\"\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(76L)]);
    }

    [Fact]
    public async Task ChDrive_NonDriveLetter_RaisesInvalidProcedureCall()
    {
        // `ChDrive 5` coerces to "5"; '5' is not a valid drive letter → Error 5 (oracle-verified). Deterministic.
        await Run("On Error Resume Next\nChDrive 5\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(5L)]);
    }

    [Fact]
    public async Task ChDrive_EmptyString_IsNoOp()
    {
        await Run("On Error Resume Next\nChDrive \"\"\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(0L)]);
    }

    public static TheoryData<int, int, int[], int> ForLoopData => new()
    {
        { 1, 5, new[] { 1, 2, 3, 4, 5, 6 }, 1 },          // Simple increment loop
        { 5, 1, new[] { 5, 4, 3, 2, 1, 0 }, -1 },         // Simple decrement loop
        { 5, 1, new[] { 5 }, 1 },                          // No-step decrement (step defaults 1)
        { 1, 5, new[] { 1, 3, 5, 7 }, 2 },                // Increment by step 2
        { 10, 2, new[] { 10, 8, 6, 4, 2, 0 }, -2 },       // Decrement by step -2
        { 1, 1, new[] { 1, 2 }, 1 },                       // Single iteration (start = end)
    };

    [Theory]
    [MemberData(nameof(ForLoopData))]
    public async Task ForLoop_ShouldIterateCorrectly(int start, int end, int[] expectedValues, int step)
    {
        string code = $@"
            For i = {start} To {end}" + (step == 1 ? "" : $" Step {step}") + $@"
                Debug.Print i
            Next
            Debug.Print i
        ";

        await Run(code);

        var expectedLog = expectedValues.Select(value => new Vb6Value(value)).ToList();

        AssertDebugLog(expectedLog);
    }

    [Fact]
    public async Task ExitFor_ShouldTerminateLoopEarly()
    {
        string code = @"
            Dim result
            For i = 1 To 10
                If i = 2 Then
                    Exit For
                End If
                Debug.Print i
            Next
            Debug.Print i
        ";

        await Run(code);

        var expectedLog = new[]
        {
            new Vb6Value(1),
            new Vb6Value(2)
        };

        AssertDebugLog(expectedLog.ToList());
    }

    [Fact]
    public async Task ExitSub_ShouldTerminateLoopEarly()
    {
        string code = @"
            Dim result
            Public Sub Test()
                For i = 1 To 10
                    If i = 2 Then
                        Exit Sub
                    End If
                    Debug.Print i
                Next
            End Sub
            Call Test
        ";

        await Run(code);

        var expectedLog = new[]
        {
            new Vb6Value(1)
        };

        AssertDebugLog(expectedLog.ToList());
    }

    [Theory]
    [InlineData(1, "One")]                          // Simple case / condition and operator
    [InlineData(2, "Two")]                          // Simple case
    [InlineData(3, "Three")]                        // Simple case
    [InlineData(4, "Number greater than 3")]        // Case with Else
    [InlineData(5, "Number greater than 3")]        // Case with Else
    [InlineData(10, "Between 10 and 20")]           // Case with a range
    [InlineData(15, "14, 15, 16")]
    [InlineData(0, "Out of range")]                 // Case with a range
    [InlineData(100, "Number greater than 3")]      // Case with a range
    [InlineData(20, "Between 10 and 20")]           // Case with To range
    public async Task SelectCaseTests(int input, string expectedOutput)
    {
        string code = $@"
        Select Case {input}
            Case 1
                Debug.Print ""One""
            Case 2
                Debug.Print ""Two""
            Case 3
                Debug.Print ""Three""
            Case 14,15,16
                Debug.Print ""14, 15, 16""
            Case 10 To 20
                Debug.Print ""Between 10 and 20""
            Case Is > 3
                Debug.Print ""Number greater than 3""
            Case Else
                Debug.Print ""Out of range""
        End Select
    ";

        await Run(code);
        AssertDebugLog([new Vb6Value(expectedOutput)]);
    }
}
