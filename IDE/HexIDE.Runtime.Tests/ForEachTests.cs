using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Phase 4.2 — For Each over arrays. Honours Exit For. The multi-dimensional order (first subscript fastest,
/// i.e. column-major) is pinned against vb6.exe. Collections are deferred (a non-array is a clean TypeMismatch).
/// </summary>
public class ForEachTests : BaseVBTestFixture
{
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
