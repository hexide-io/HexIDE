using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 4.1 — class <c>Property Get/Let/Set</c>. Access kind selects the accessor: a read
/// (<c>= x.P</c>) dispatches Get, a value-assign (<c>x.P = v</c>) dispatches Let, an object-assign
/// (<c>Set x.P = o</c>) dispatches Set — the same instance-env + <c>Me</c> dispatch as methods. The Let/Set
/// value coerces to the accessor parameter's declared type.
/// </summary>
public class PropertyTests : BaseVBTestFixture
{
    [Fact]
    public async Task PropertyGetLet_RoundTrip()
    {
        await RunClasses(
            "Dim t As Thermostat\nSet t = New Thermostat\nt.Target = 21\nDebug.Print t.Target\n",
            ("Thermostat",
                "Private mTarget As Integer\n" +
                "Public Property Get Target() As Integer\nTarget = mTarget\nEnd Property\n" +
                "Public Property Let Target(ByVal v As Integer)\nmTarget = v\nEnd Property\n"));
        AssertDebugLog([21]);
    }

    [Fact]
    public async Task PropertyGet_ReadOnly_ComputesFromFields()
    {
        // A Get with no matching Let is read-only; it can compute from private fields.
        await RunClasses(
            "Dim r As Rect\nSet r = New Rect\nr.W = 4\nr.H = 5\nDebug.Print r.Area\n",
            ("Rect",
                "Private mW As Integer\nPrivate mH As Integer\n" +
                "Public Property Get Area() As Integer\nArea = mW * mH\nEnd Property\n" +
                "Public Property Let W(ByVal v As Integer)\nmW = v\nEnd Property\n" +
                "Public Property Let H(ByVal v As Integer)\nmH = v\nEnd Property\n"));
        AssertDebugLog([20]);
    }

    [Fact]
    public async Task PropertyLet_CoercesToParameterType()
    {
        // The assigned value coerces to the Let parameter's declared type (ByVal … As Integer): 3.9 -> 4
        // (truncation would give 3), reusing the pinned ByVal-param coercion.
        await RunClasses(
            "Dim c As Counter\nSet c = New Counter\nc.N = 3.9\nDebug.Print c.N\n",
            ("Counter",
                "Private mN As Integer\n" +
                "Public Property Get N() As Integer\nN = mN\nEnd Property\n" +
                "Public Property Let N(ByVal v As Integer)\nmN = v\nEnd Property\n"));
        AssertDebugLog([4]);
    }

    [Fact]
    public async Task PropertySet_StoresObjectReference()
    {
        // `Set obj.P = other` dispatches Property Set; a later Get returns the same reference.
        await RunClasses(
            "Dim a As Holder\nDim b As Node\nSet a = New Holder\nSet b = New Node\n" +
            "Set a.Item = b\nDebug.Print (a.Item Is b)\n",
            ("Holder",
                "Private mItem As Node\n" +
                "Public Property Get Item() As Node\nSet Item = mItem\nEnd Property\n" +
                "Public Property Set Item(o As Node)\nSet mItem = o\nEnd Property\n"),
            ("Node", "Public Val As Integer\n"));
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task PropertyLet_ThroughWithTarget()
    {
        await RunClasses(
            "Dim t As Thermostat\nSet t = New Thermostat\nWith t\n.Target = 19\nDebug.Print .Target\nEnd With\n",
            ("Thermostat",
                "Private mTarget As Integer\n" +
                "Public Property Get Target() As Integer\nTarget = mTarget\nEnd Property\n" +
                "Public Property Let Target(ByVal v As Integer)\nmTarget = v\nEnd Property\n"));
        AssertDebugLog([19]);
    }

    [Fact]
    public async Task Property_TwoInstancesAreIndependent()
    {
        await RunClasses(
            "Dim a As Box\nDim b As Box\nSet a = New Box\nSet b = New Box\n" +
            "a.V = 1\nb.V = 2\nDebug.Print a.V\nDebug.Print b.V\n",
            ("Box",
                "Private mV As Integer\n" +
                "Public Property Get V() As Integer\nV = mV\nEnd Property\n" +
                "Public Property Let V(ByVal x As Integer)\nmV = x\nEnd Property\n"));
        AssertDebugLog([1, 2]);
    }
}
