namespace HexIDE.Runtime.Tests;

/// <summary>
/// A ByRef <c>v As Variant</c> parameter shares its caller's variable, as an untyped <c>v</c> does, whatever the variable
/// holds (#1001). It got a copy of a scalar, so a store into it never reached the caller. Measured on vb6.exe, native and
/// p-code agreeing; the oracle's <i>Arrays copy wherever VB6 stores one</i> has the rows.
/// </summary>
public class ByRefVariantParameterTests : BaseVBTestFixture
{
    private const string Subs = "Sub S(v As Variant)\n    v = 5\nEnd Sub\nSub U(v)\n    v = 6\nEnd Sub\n";

    private string[] Log => debug.Select(v => v.Value?.ToString() ?? "").ToArray();

    [Theory]
    [InlineData("Dim w\nw = 1\nS w\nDebug.Print w & \" \" & TypeName(w)", "5 Integer")]                 // a Variant holding 1
    [InlineData("Dim n As Integer\nn = 1\nS n\nDebug.Print n & \" \" & TypeName(n)", "5 Integer")]     // a typed Integer
    [InlineData("Dim z\nz = \"a\"\nU z\nDebug.Print z & \" \" & TypeName(z)", "6 Integer")]            // untyped, as before
    [InlineData("Dim m As Integer\nm = 1\nU m\nDebug.Print m & \" \" & TypeName(m)", "6 Integer")]     // stays an Integer
    public async Task A_ByRef_Variant_parameter_writes_back_a_scalar(string caller, string expected)
    {
        await Run(caller + "\n" + Subs);

        Log.Should().Equal(expected);
    }

    [Theory]
    [InlineData("v As Variant")]
    [InlineData("v")]
    public async Task A_Variant_parameter_sharing_a_typed_variable_is_still_a_Variant(string parameter)
    {
        // Measured: given a `Dim n As Integer` holding 5, `v * 7000` widens to a Long 35000, where n * 7000 would overflow,
        // and `Select Case v` takes `Case Is < 5.4`. The slot is the caller's typed one; the parameter's name says it is a
        // Variant.
        await Run("Dim n As Integer\nn = 5\nP n\n"
            + $"Sub P({parameter})\n    On Error Resume Next\n    Dim x\n    x = v * 7000\n    Debug.Print Err.Number & \" \" & x & \" \" & TypeName(x)\nEnd Sub\n");

        Log.Should().Equal("0 35000 Long");
    }
}
