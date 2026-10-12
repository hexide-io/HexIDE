using System;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// An in-box object on the right of a Let stands for its default member in VB6. Measured (#840's review, native and in
/// the IDE): <c>n = Err</c> stores Err.Number, and App, which has no default member, raises 438 (ObjectAsValue). A
/// control does the same, and is #790's: its half is in <c>HexIDE.Integration.Tests</c> <c>LetOfAReferenceTests</c>,
/// which needs a form.
/// </summary>
public class LetOfAnInBoxObjectTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("x = Err\nDebug.Print x")]
    [InlineData("Dim n As Long\nn = Err\nDebug.Print n")]
    public async Task A_Let_of_Err_stores_its_Number(string code)
    {
        await Run("On Error Resume Next\nErr.Raise 11\n" + code + "\n");

        AssertDebugLog([new Vb6Value(11L)]);
    }

    [Fact]
    public async Task A_Let_of_App_raises_438()
    {
        await Run("Dim s As String\ns = \"pre\"\nOn Error Resume Next\ns = App\nDebug.Print Err.Number\nDebug.Print s\n");

        AssertDebugLog([new Vb6Value(438L), new Vb6Value("pre")]);
    }

    [Fact]
    public async Task Set_still_keeps_the_reference()
    {
        await Run("Dim e\nSet e = Err\nErr.Number = 5\nDebug.Print e.Number\n");

        AssertDebugLog([new Vb6Value(5L)]);   // Err.Number is a Long
    }
}
