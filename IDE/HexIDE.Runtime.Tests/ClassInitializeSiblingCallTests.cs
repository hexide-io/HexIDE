using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A call from <c>Class_Initialize</c> to one of the object's own methods does not terminate the object it is
/// building. Measured on vb6.exe; the probe is in docs/vb6-fidelity-oracle.md, "Subscripts, bounds, and a method
/// called from Class_Initialize".
/// </summary>
public class ClassInitializeSiblingCallTests : BaseVBTestFixture
{
    private const string Counter = """
        Public Count As Long
        Private Sub Class_Initialize()
            Debug.Print "init start"
            ZeroIt
            Bump
            Debug.Print "init end " & Count
        End Sub
        Private Sub ZeroIt()
            Debug.Print "zero"
            Count = 0
        End Sub
        Public Sub Bump()
            Count = Count + 1
            Debug.Print "bump " & Count
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "terminate " & Count
        End Sub

        """;

    [Fact]
    public async Task Siblings_called_from_Class_Initialize_leave_the_object_alive_until_its_last_reference_goes()
    {
        // The object stood at a count of 0 while Initialize ran, so the first sibling call took it to 1 and back to
        // 0 on return and fired Terminate mid-construction; the Terminated flag then stopped counting it at all.
        await RunClasses("""
            Dim k As Counter
            Set k = New Counter
            Debug.Print "after New " & k.Count
            Set k = Nothing
            Debug.Print "after Set Nothing"

            """.Replace("\r\n", "\n"), ("Counter", Counter.Replace("\r\n", "\n")));
        AssertDebugLog([
            "init start", "zero", "bump 1", "init end 1", "after New 1", "terminate 1", "after Set Nothing",
        ]);
    }

    [Fact]
    public async Task An_object_built_into_a_local_terminates_when_the_procedure_ends()
    {
        await RunClasses("""
            NewAndDrop
            Debug.Print "after"
            Sub NewAndDrop()
                Dim t As Counter
                Set t = New Counter
                Debug.Print "inside " & t.Count
            End Sub

            """.Replace("\r\n", "\n"), ("Counter", Counter.Replace("\r\n", "\n")));
        AssertDebugLog([
            "init start", "zero", "bump 1", "init end 1", "inside 1", "terminate 1", "after",
        ]);
    }
}
