using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Relations measured with vb6.exe SP6, native and p-code: #2622, with measuring PRs #2624 and #2628.
// These numbers are identities, so no test pins an address.
public class ObjPtrTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("Debug.Print ObjPtr(Nothing) = 0")]
    [InlineData("Dim o As Object: Debug.Print ObjPtr(o) = 0")]
    [InlineData("Dim o As Object: Set o = Nothing: Debug.Print ObjPtr(o) = 0")]
    public async Task Nothing_has_zero_identity(string code)
    {
        await Run(code);
        AssertDebugLog([true]);
    }

    [Theory]
    [InlineData("Debug.Print ObjPtr(c) <> 0")]
    [InlineData("Debug.Print ObjPtr(o) = ObjPtr(c)")]
    [InlineData("Debug.Print ObjPtr(c) <> ObjPtr(d)")]
    public async Task Live_objects_have_distinct_identities_shared_by_aliases(string assertion)
    {
        await RunClasses("""
            Dim c As Class1, d As Class1, o As Object
            Set c = New Class1
            Set d = New Class1
            Set o = c
            """ + Environment.NewLine + assertion, ("Class1", "Public Name As String"));
        AssertDebugLog([true]);
    }

    [Theory]
    [InlineData("Nothing")]
    [InlineData("c")]
    public async Task Result_is_a_Long(string operand)
    {
        await Run("""
            Dim c As Collection
            Set c = New Collection
            """ + Environment.NewLine + $"Debug.Print TypeName(ObjPtr({operand}))");
        AssertDebugLog(["Long"]);
    }

    [Theory]
    [InlineData("5")]
    [InlineData("\"a\"")]
    public async Task Measured_non_objects_raise_runtime_type_mismatch(string operand)
    {
        await Run("""
            On Error Resume Next
            Dim p As Long
            """ + Environment.NewLine + $"p = ObjPtr({operand})" + Environment.NewLine + "Debug.Print Err.Number");
        AssertDebugLog([new Vb6Value(13L)]);
    }

    [Theory]
    [InlineData("ObjPtr(Null)")]
    [InlineData("ObjPtr(Empty)")]
    [InlineData("ObjPtr(True)")]
    [InlineData("ObjPtr(5&)")]
    [InlineData("ObjPtr(1#)")]
    [InlineData("ObjPtr(Array(1))")]
    [InlineData("ObjPtr()")]
    [InlineData("ObjPtr(Nothing, Nothing)")]
    public async Task Unmeasured_shapes_are_explicitly_refused(string call)
    {
        Func<Task> run = () => Run("Debug.Print " + call);
        await run.Should().ThrowAsync<NotImplementedException>().WithMessage("*unmeasured*");
    }
}
