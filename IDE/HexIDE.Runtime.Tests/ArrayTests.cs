using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ArrayTests : BaseVBTestFixture
{
    [Fact]
    public async Task StringArray_Get_Set()
    {
        await Run(@"Dim Arr(10) As String
Arr(0) = ""Hello""
Debug.Print Arr(0)");
        AssertDebugLog(["Hello"]);
    }

    [Fact]
    public async Task StringArray_Get_Set_Base10()
    {
        await Run(@"Dim Arr(10 To 20) As String
Arr(20) = ""Hello""
Debug.Print Arr(20)");
        AssertDebugLog(["Hello"]);
    }

    [Fact]
    public async Task StringArray_Get_Set_Two_Dim()
    {
        await Run(@"Dim Arr(10, 20) As String
Arr(0, 1) = ""Hello""
Debug.Print Arr(0, 1)");
        AssertDebugLog(["Hello"]);
    }

    [Fact]
    public async Task StringArray_Set_Two_Dims_OutOfBounds()
    {
        var ex = (await ((Func<Task>)(async () => await Run(@"Dim Arr(10, 20) As String
Arr(0, 21) = ""Hello"""))).Should().ThrowAsync<VBRunTimeException>()).Which;
        ex.Error.ErrNo.Should().Be(VBStandardError.SubscriptOutOfRange.ErrNo);
    }

    [Fact]
    public async Task StringArray_Get_Two_Dims_OutOfBounds()
    {
        var ex = (await ((Func<Task>)(async () => await Run(@"Dim Arr(10, 20) As String
Debug.Print Arr(0, 21)"))).Should().ThrowAsync<VBRunTimeException>()).Which;
        ex.Error.ErrNo.Should().Be(VBStandardError.SubscriptOutOfRange.ErrNo);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public async Task Array_Bound_OptionBase(int @base)
    {
        await Run($"Option Base {@base}" + @"
Dim Arr(20) As String
Debug.Print LBound(Arr)
Debug.Print UBound(Arr)
");
        AssertDebugLog([@base, 20]);
    }

    [Fact]
    public async Task UndefinedSize_Array()
    {
        await Run(@"Dim Arr() As String");
    }

    [Fact]
    public async Task StringArray_ReDim()
    {
        await Run(@"Dim Arr()
ReDim Arr(10) As String
Arr(0) = ""Hello""
Debug.Print Arr(0)");
        AssertDebugLog(["Hello"]);
    }
}