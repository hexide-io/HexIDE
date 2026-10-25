namespace HexIDE.Runtime.Tests;

// Class1.V is a Public Variant holding Array(10, 20, 30), measured native and p-code (#1435).
public class VariantFieldIndexTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("v = k.V(1)", "20 err 0")]
    [InlineData("v = k.V(5)", " err 9")]
    [InlineData("v = o.V(2)", " err 451")]
    [InlineData("k.V(1) = 99: v = k.V(1)", "20 err 0")]
    public async Task A_Variant_field_index_matches_the_measured_binding(string statement, string expected)
    {
        await RunClasses($"""
            Dim k As Class1
            Dim o As Object
            Dim v As Variant
            Set k = New Class1
            Set o = k
            k.V = Array(10, 20, 30)
            On Error Resume Next
            {statement}
            Debug.Print v & " err " & Err.Number
            """, ("Class1", "Public V As Variant\r\n"));
        AssertDebugLog([expected]);
    }
}
