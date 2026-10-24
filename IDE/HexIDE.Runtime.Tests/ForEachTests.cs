using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 4.2 — For Each over arrays. Honours Exit For. The multi-dimensional order (first subscript fastest,
/// i.e. column-major) is pinned against vb6.exe. Collections are deferred (a non-array is a clean TypeMismatch).
/// </summary>
public class ForEachTests : BaseVBTestFixture
{
    // #1486: all six measured rows, through both declarations. Expected results are p-code's.
    [Theory]
    [InlineData("Collection", 0)]
    [InlineData("Collection", 1)]
    [InlineData("Collection", 2)]
    [InlineData("Collection", 3)]
    [InlineData("Collection", 4)]
    [InlineData("Collection", 5)]
    [InlineData("Object", 0)]
    [InlineData("Object", 1)]
    [InlineData("Object", 2)]
    [InlineData("Object", 3)]
    [InlineData("Object", 4)]
    [InlineData("Object", 5)]
    public async Task Nothing_ResumeNext_MeasuredTable(string collectionType, int row)
    {
        string[] loops =
        [
            "For Each v In w: Next",
            "For Each v In w: Next: Debug.Print \"after\"",
            "For Each v In w: Debug.Print \"body\": Next",
            "For Each v In w\nNext",
            "For Each v In w\nDebug.Print \"body\"\nNext",
            "For Each o In w: Debug.Print \"body\": Next"
        ];
        await Run($"Dim w As {collectionType}\nDim v\nDim o As Object\n" +
            "v = \"unchanged\"\nOn Error Resume Next\n" + loops[row] +
            "\nDebug.Print Err.Number\nDebug.Print TypeName(v)\n");
        List<Vb6Value> expected = row switch
        {
            0 => [new Vb6Value(424L), "String"],
            1 => ["after", new Vb6Value(424L), "String"],
            2 or 4 => ["body", new Vb6Value(92L), "Empty"],
            3 => [new Vb6Value(92L), "Empty"],
            _ => ["body", new Vb6Value(92L), "String"]
        };
        AssertDebugLog(expected);
    }

    [Fact]
    public async Task OneDimensional()
    {
        await Run(
            "Dim x\n" +
            "For Each x In Array(10, 20, 30)\n" +
            "Debug.Print x\n" +
            "Next\n");
        AssertDebugLog([10, 20, 30]);
    }

    [Fact]
    public async Task ExitFor_StopsEarly()
    {
        await Run(
            "Dim x\n" +
            "For Each x In Array(1, 2, 3, 4, 5)\n" +
            "If x = 3 Then\n" +
            "Exit For\n" +
            "End If\n" +
            "Debug.Print x\n" +
            "Next\n");
        AssertDebugLog([1, 2]);
    }

    [Fact]
    public async Task TwoDimensional_FirstSubscriptFastest()
    {
        await Run(
            "Dim a(1 To 2, 1 To 3) As Integer\n" +
            "a(1, 1) = 11\n" +
            "a(1, 2) = 12\n" +
            "a(1, 3) = 13\n" +
            "a(2, 1) = 21\n" +
            "a(2, 2) = 22\n" +
            "a(2, 3) = 23\n" +
            "Dim x\n" +
            "For Each x In a\n" +
            "Debug.Print x\n" +
            "Next\n");
        AssertDebugLog([11, 21, 12, 22, 13, 23]);   // column-major, verified vs vb6.exe
    }

    [Fact]
    public async Task NonArrayCollection_IsTypeMismatch()
    {
        Func<Task> act = () => Run(
            "Dim x\n" +
            "For Each x In 42\n" +
            "Debug.Print x\n" +
            "Next\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(13);
    }

    [Fact]
    public async Task ForEach_OverEmptyArray_IteratesZeroTimes_NoCrash()
    {
        // Split("") yields a zero-length array (UBound < LBound). For Each must iterate zero times, not throw
        // IndexOutOfRangeException on the empty backing store.
        await Run(
            "Dim parts() As String\n" +
            "parts = Split(\"\")\n" +
            "Dim x\n" +
            "For Each x In parts\n" +
            "Debug.Print x\n" +
            "Next\n" +
            "Debug.Print \"done\"\n");
        AssertDebugLog(["done"]);   // the loop body never ran
    }
}
