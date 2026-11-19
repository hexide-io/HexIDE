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

    // CR LF measured in native code, p-code and the IDE: https://github.com/hexide-io/HexIDE/issues/4676.
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
        AssertDebugLog([new Vb6Value(-2147221164), new Vb6Value("Class not registered\r\n"), new Vb6Value(true)]);
    }

    [Fact]
    public async Task New_ErrObject_description_has_measured_length_and_trailing_CR_LF()
    {
        // VB6 measurement: https://github.com/hexide-io/HexIDE/issues/4676 (oracle PR #4731).
        await Run("""
            Dim e As ErrObject
            On Error Resume Next
            Set e = New ErrObject
            Debug.Print Len(Err.Description)
            Debug.Print Right$(Err.Description, 2) = vbCrLf
            """);
        AssertDebugLog([new Vb6Value(22L), new Vb6Value(true)]);
    }
}
