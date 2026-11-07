using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

public class ErrObjectTests : BaseVBTestFixture
{
    // VB6 SP6 native and p-code measurements: #2627, with oracle provenance in #2624 and #2628.
    [Fact]
    public async Task Collection_assignment_raises_13_and_preserves_Err_reference()
    {
        await Run("""
            Dim e As ErrObject
            Set e = Err
            On Error Resume Next
            Set e = New Collection
            Debug.Print Err.Number
            Debug.Print e Is Err
            Debug.Print e.Number
            Set e = Nothing
            Debug.Print e Is Nothing
            """);
        AssertDebugLog([new Vb6Value(13L), new Vb6Value(true), new Vb6Value(13L), new Vb6Value(true)]);
    }

    [Fact]
    public async Task New_ErrObject_raises_trappable_class_not_registered_and_leaves_Nothing()
    {
        await Run("""
            Dim e As ErrObject
            On Error Resume Next
            Set e = New ErrObject
            Debug.Print Err.Number
            Debug.Print Err.Description
            Debug.Print e Is Nothing
            """);
        AssertDebugLog([new Vb6Value(-2147221164), new Vb6Value("Class not registered"), new Vb6Value(true)]);
    }
}
