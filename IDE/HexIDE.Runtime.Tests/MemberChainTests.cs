using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// `a.b.c` — member chains of any depth, and module-qualified ones.
///
/// Measured (see docs/vb6-fidelity-oracle/member-chains-including-module-qualified-ones.md): a module qualifier is simply the first step of an ordinary
/// chain, with no depth limit and no special case. `Module1.p.In1.Z` is Module1 → p → In1 → Z.
///
/// This was filed under "Walled off (by design)" as needing a bound AST. It does not: each step is a lookup
/// on a value that exists by the time it is needed, which is execution rather than analysis, and the parser
/// already hands over the whole chain as an array. (#173)
///
/// Qualified writes also reach the module-owned record; measured in #173 on 2026-11-14.
/// </summary>
public class MemberChainTests : BaseVBTestFixture
{
    private const string Types =
        "Private Type Inner\r\n  Z As Long\r\nEnd Type\r\n" +
        "Private Type Outer\r\n  X As Long\r\n  In1 As Inner\r\nEnd Type\r\n";

    [Fact]
    public async Task ANestedUdtFieldChainReads()
    {
        await Run(Types + "Dim p As Outer\r\np.In1.Z = 99\r\nDebug.Print CStr(p.In1.Z)");
        AssertDebugLog(["99"]);
    }

    [Fact]
    public async Task AModuleQualifiedScalarReads()
    {
        await Run("Dim n As Long\r\nn = 5\r\nDebug.Print CStr(Module1.n)");
        AssertDebugLog(["5"]);
    }

    [Fact]
    public async Task AModuleQualifiedFieldReads()
    {
        // Two levels past the qualifier — previously "Multi-level qualified member access is not supported".
        await Run(Types + "Dim p As Outer\r\np.X = 7\r\nDebug.Print CStr(Module1.p.X)");
        AssertDebugLog(["7"]);
    }

    [Fact]
    public async Task AModuleQualifiedNestedFieldChainReads()
    {
        // Four levels: Module1 → p → In1 → Z. Measured against vb6.exe at exactly this depth.
        await Run(Types + "Dim p As Outer\r\np.In1.Z = 42\r\nDebug.Print CStr(Module1.p.In1.Z)");
        AssertDebugLog(["42"]);
    }

    [Fact]
    public async Task AChainOnAClassInstanceReads()
    {
        // The object leg: a field holding an object, then a member on that. Set up without a chained write,
        // which is the separate leg noted above.
        await RunClasses(
            "Dim a As Holder\r\nDim l As Leaf\r\nSet a = New Holder\r\nSet l = New Leaf\r\n" +
            "l.Value = 3\r\nSet a.Child = l\r\nDebug.Print CStr(a.Child.Value)",
            ("Holder", "Public Child As Leaf\r\n"),
            ("Leaf", "Public Value As Long\r\n"));
        AssertDebugLog(["3"]);
    }

    [Fact]
    public async Task AChainThroughAMethodResultReads()
    {
        // A call mid-chain: the step resolves on whatever the previous step produced, slot or not — which is
        // why the fold has to carry a value rather than look one up.
        await RunClasses(
            "Dim a As Holder\r\nDim l As Leaf\r\nSet a = New Holder\r\nSet l = New Leaf\r\n" +
            "l.Value = 8\r\nSet a.Child = l\r\nDebug.Print CStr(a.GetChild().Value)",
            ("Holder", "Public Child As Leaf\r\nPublic Function GetChild() As Leaf\r\n  Set GetChild = Child\r\nEnd Function\r\n"),
            ("Leaf", "Public Value As Long\r\n"));
        AssertDebugLog(["8"]);
    }
    // vb6.exe SP6, native and p-code, 2026-11-14: #173's latest measurement comment.
    [Theory]
    [InlineData("Module1.p.X = 7", "p.X", "7")]
    [InlineData("Module1.p.In1.Z = 42", "p.In1.Z", "42")]
    [InlineData("Module1.a(1).In1.Z = 9", "a(1).In1.Z", "9")]
    [InlineData("Module1.p.In1.Z = 42\r\nModule1.p.In1.Z = Module1.p.In1.Z + 1", "p.In1.Z", "43")]
    public async Task AModuleQualifiedUdtWriteReachesItsOwner(string store, string read, string expected)
    {
        await Run(Types + "Dim p As Outer\r\nDim a(1) As Outer\r\n" + store + "\r\nDebug.Print CStr(" + read + ")");
        AssertDebugLog([expected]);
    }

    [Fact]
    public async Task AModuleQualifiedUdtWriteInsideASubReachesItsOwner()
    {
        await Run(Types + "Dim p As Outer\r\nSub WriteP()\r\nModule1.p.In1.Z = 42\r\nEnd Sub\r\nWriteP\r\nDebug.Print CStr(p.In1.Z)");
        AssertDebugLog(["42"]);
    }

    [Fact]
    public async Task AForeignModuleQualifiedUdtWriteReachesItsOwner()
    {
        await RunModules("Module2.q.In1.Z = 5\r\nDebug.Print CStr(Module2.ReadQ())\r\nModule2.q.In1.Z = 6\r\nDebug.Print CStr(q.In1.Z)",
            ("Module2", Types.Replace("Private Type", "Public Type") +
                "Public q As Outer\r\nPublic Function ReadQ() As Long\r\nReadQ = q.In1.Z\r\nEnd Function\r\n"));
        AssertDebugLog(["5", "6"]);
    }
}
