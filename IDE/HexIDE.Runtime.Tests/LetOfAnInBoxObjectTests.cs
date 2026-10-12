using System;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// An in-box object on the right of a Let, like a control, stands for its default member in VB6 (<c>n = Err</c> reads
/// Err.Number, per the documentation; not measured). HexIDE has no default members yet (#790), so a Let of one is
/// refused rather than storing the reference. The controls' half is in
/// <c>HexIDE.Integration.Tests</c> <c>LetOfAReferenceTests</c>, which needs a form.
/// </summary>
public class LetOfAnInBoxObjectTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("x = Err")]
    [InlineData("Dim n As Long\nn = Err")]
    [InlineData("Dim s As String\ns = App")]
    public async Task A_Let_of_an_in_box_object_is_refused_until_default_members_are_implemented(string code)
    {
        Func<Task> act = () => Run("On Error Resume Next\n" + code + "\n");

        (await act.Should().ThrowAsync<NotImplementedException>()).Which.Message.Should().Contain("#790");
    }

    [Fact]
    public async Task Set_still_keeps_the_reference()
    {
        await Run("Dim e\nSet e = Err\nErr.Number = 5\nDebug.Print e.Number\n");

        AssertDebugLog([new Vb6Value(5L)]);   // Err.Number is a Long
    }
}
