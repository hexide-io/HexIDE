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
    public async Task A_Let_of_App_leaves_a_typed_target_as_it_was()
    {
        // Measured, native and in the IDE: a class-typed variable keeps its instance, a ByVal Long parameter its 5, and a
        // Function As Long's own name its 7. A parameter's and a return name's slots carry no declared type, which is
        // why #878 cannot simply empty every slot without one. The parameter is read as text because that missing type
        // also leaves `x = 5` an Integer there, where VB6 has a Long (#874).
        await RunClasses(
            "Dim k As Plain\nSet k = New Plain\nOn Error Resume Next\nk = App\nDebug.Print Err.Number\nDebug.Print TypeName(k)\n"
            + "Err.Clear\nTakesLong 1\nErr.Clear\nDebug.Print RetLong()\n"
            + "Sub TakesLong(ByVal x As Long)\nx = 5\nOn Error Resume Next\nx = App\nDebug.Print Err.Number\nDebug.Print CStr(x)\nEnd Sub\n"
            + "Function RetLong() As Long\nRetLong = 7\nOn Error Resume Next\nRetLong = App\nEnd Function\n",
            ("Plain", "Public Tag As Long\n"));

        AssertDebugLog([new Vb6Value(438L), "Plain", new Vb6Value(438L), "5", new Vb6Value(7L)]);
    }

    [Fact]
    public async Task A_Let_of_a_class_with_no_default_member_leaves_a_Variant_as_it_was()
    {
        await RunClasses("Dim v, k As Plain\nSet k = New Plain\nv = 5\nOn Error Resume Next\nv = k\nDebug.Print Err.Number\nDebug.Print VarType(v)\n",
            ("Plain", "Public Tag As Long\n"));

        AssertDebugLog([new Vb6Value(438L), new Vb6Value(2L)]);
    }

    [Fact]
    public async Task Set_still_keeps_the_reference()
    {
        await Run("Dim e\nSet e = Err\nErr.Number = 5\nDebug.Print e.Number\n");

        AssertDebugLog([new Vb6Value(5L)]);   // Err.Number is a Long
    }
}
