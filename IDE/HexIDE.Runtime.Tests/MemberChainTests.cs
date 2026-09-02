using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// `a.b.c` — member chains of any depth, and module-qualified ones.
///
/// Measured (see docs/vb6-fidelity-oracle.md): a module qualifier is simply the first step of an ordinary
/// chain, with no depth limit and no special case. `Module1.p.In1.Z` is Module1 → p → In1 → Z.
///
/// This was filed under "Walled off (by design)" as needing a bound AST. It does not: each step is a lookup
/// on a value that exists by the time it is needed, which is execution rather than analysis, and the parser
/// already hands over the whole chain as an array. (#173)
///
/// READS only. Chained ASSIGNMENT (`a.b.c = 5`) is a separate leg — the prefix may resolve to a UDT, which
/// is a value type, so whether the write lands back in its owner is an aliasing question that deserves its
/// own tests rather than a rushed one.
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
}
