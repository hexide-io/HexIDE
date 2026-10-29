namespace HexIDE.Runtime.Tests;

// Native and p-code relations measured in #2622; measuring PRs #2624 and #2628.
public class PointerIdentityTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Debug.Print StrPtr(vbNullString) = 0")]
    [InlineData("Dim s As String: Debug.Print StrPtr(s) = 0")]
    [InlineData("Dim s As String: s = \"\": Debug.Print StrPtr(s) <> 0")]
    [InlineData("Dim s As String, t As String: s = \"abc\": t = s: Debug.Print StrPtr(s) <> StrPtr(t)")]
    [InlineData("Dim s As String: s = \"abc\": Debug.Print StrPtr(s) = StrPtr(s)")]
    [InlineData("Dim x As Long: Debug.Print VarPtr(x) <> 0")]
    [InlineData("Dim x As Long: Debug.Print VarPtr(x) = VarPtr(x)")]
    [InlineData("Dim x As Long, y As Long: Debug.Print VarPtr(x) <> VarPtr(y)")]
    [InlineData("Dim s As String: s = \"abc\": Debug.Print VarPtr(s) <> StrPtr(s)")]
    [InlineData("Debug.Print StrPtr(5) <> 0")]
    [InlineData("Debug.Print VarPtr(5) <> 0")]
    public async Task Measured_identity_relation(string code)
    {
        await Run(code);
        AssertDebugLog([true]);
    }

    [Theory]
    [InlineData("Long", 4)]
    [InlineData("Integer", 2)]
    [InlineData("Currency", 8)]
    [InlineData("Variant", 16)]
    public async Task Array_elements_have_measured_steps(string type, int step)
    {
        await Run($"Dim a(1) As {type}: Debug.Print VarPtr(a(1)) - VarPtr(a(0)) = {step}");
        AssertDebugLog([true]);
    }

    [Theory]
    [InlineData("StrPtr")]
    [InlineData("VarPtr")]
    public async Task Result_is_Long(string function)
    {
        await Run($"Debug.Print TypeName({function}(5))");
        AssertDebugLog(["Long"]);
    }

    [Theory]
    [InlineData("ByRef", "VarPtr", "=")]
    [InlineData("ByVal", "VarPtr", "<>")]
    [InlineData("ByRef", "StrPtr", "=")]
    [InlineData("ByVal", "StrPtr", "<>")]
    public async Task Parameters_preserve_or_copy_identity(string passing, string function, string comparison)
    {
        await Run($"""
            Dim s As String
            s = "abc"
            Check s, {function}(s)
            Sub Check({passing} text As String, ByVal caller As Long)
                Debug.Print {function}(text) {comparison} caller
            End Sub
            """);
        AssertDebugLog([true]);
    }

    [Theory]
    [InlineData("ByRef", "=")]
    [InlineData("ByVal", "<>")]
    public async Task Array_element_parameter_identity(string passing, string comparison)
    {
        await Run($"""
            Dim a(1) As Long
            Check a(1), VarPtr(a(1))
            Sub Check({passing} x As Long, ByVal caller As Long)
                Debug.Print VarPtr(x) {comparison} caller
            End Sub
            """);
        AssertDebugLog([true]);
    }

    [Theory]
    [InlineData("StrPtr()")]
    [InlineData("VarPtr()")]
    [InlineData("StrPtr(5, 5)")]
    [InlineData("VarPtr(5, 5)")]
    [InlineData("StrPtr(Null)")]
    [InlineData("StrPtr(True)")]
    [InlineData("StrPtr(5&)")]
    [InlineData("VarPtr(Null)")]
    [InlineData("VarPtr(True)")]
    [InlineData("VarPtr(5&)")]
    [InlineData("StrPtr(Value:=5)")]
    [InlineData("VarPtr(Value:=5)")]
    public async Task Unmeasured_shapes_are_refused(string call)
    {
        Func<Task> run = () => Run("Debug.Print " + call);
        await run.Should().ThrowAsync<NotImplementedException>();
    }

    [Theory]
    [InlineData("Dim a(1) As Byte: Debug.Print VarPtr(a(0))")]
    [InlineData("Dim a(1, 1) As Long: Debug.Print VarPtr(a(0, 0))")]
    [InlineData("Type T: x As Long: End Type: Dim u As T: Debug.Print VarPtr(u.x)")]
    public async Task Unmeasured_targets_are_refused(string code)
    {
        Func<Task> run = () => Run(code);
        await run.Should().ThrowAsync<NotImplementedException>();
    }
}
