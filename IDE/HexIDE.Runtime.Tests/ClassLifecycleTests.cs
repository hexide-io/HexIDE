using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 4.1 — <c>Class_Initialize</c> fires on <c>New</c>, after the field Dims (so it can
/// set fields), on each instance independently. (<c>Class_Terminate</c> is Phase 4.2.)
/// </summary>
public class ClassLifecycleTests : BaseVBTestFixture
{
    [Fact]
    public async Task ClassInitialize_RunsOnNew_SetsPublicField()
    {
        await RunClasses(
            "Dim c As Widget\nSet c = New Widget\nDebug.Print c.Ready\n",
            ("Widget",
                "Public Ready As Integer\n" +
                "Private Sub Class_Initialize()\nReady = 7\nEnd Sub\n"));
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task ClassInitialize_SetsPrivateFieldReadViaGet()
    {
        await RunClasses(
            "Dim c As Account\nSet c = New Account\nDebug.Print c.Balance\n",
            ("Account",
                "Private mBalance As Integer\n" +
                "Public Property Get Balance() As Integer\nBalance = mBalance\nEnd Property\n" +
                "Private Sub Class_Initialize()\nmBalance = 100\nEnd Sub\n"));
        AssertDebugLog([100]);
    }

    [Fact]
    public async Task ClassInitialize_RunsPerInstance()
    {
        // Each New fires Class_Initialize on its own instance; mutating one doesn't touch the other.
        await RunClasses(
            "Dim a As Seq\nDim b As Seq\nSet a = New Seq\nSet b = New Seq\n" +
            "a.Bump\nDebug.Print a.N\nDebug.Print b.N\n",
            ("Seq",
                "Private mN As Integer\n" +
                "Public Sub Bump()\nmN = mN + 1\nEnd Sub\n" +
                "Public Property Get N() As Integer\nN = mN\nEnd Property\n" +
                "Private Sub Class_Initialize()\nmN = 10\nEnd Sub\n"));
        AssertDebugLog([11, 10]);
    }
}
