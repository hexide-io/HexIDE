using System.Threading.Tasks;

namespace HexIDE.Runtime.Tests;

// vb6.exe SP6 native and p-code measurements, 2026-11-14, in #173's latest comment.
public class ChainedWriteTests : BaseVBTestFixture
{
    [Theory]
    [InlineData("o.H.Child", "NewLeaf(5)", "CStr(o.H.Child.Value)", "5")]
    [InlineData("Module1.o.H.Child", "NewLeaf(6)", "CStr(o.H.Child.Value)", "6")]
    [InlineData("Module1.o.H.Child", "l", "CStr(o.H.Child Is l)", "True")]
    [InlineData("Module1.o.H.Child", "Nothing", "CStr(o.H.Child Is Nothing)", "True")]
    public async Task ChainedSetStoresTheReference(string target, string source, string read, string expected)
    {
        await RunClasses("Dim o As Outer2\r\nDim l As Leaf\r\n" +
            "Function NewLeaf(n) As Leaf\r\nDim x As Leaf\r\nSet x = New Leaf\r\nx.Value = n\r\nSet NewLeaf = x\r\nEnd Function\r\n" +
            "Set o = New Outer2\r\nSet o.H = New Holder\r\nSet l = NewLeaf(5)\r\nSet " + target + " = " + source + "\r\nDebug.Print " + read,
            ("Outer2", "Public H As Holder\r\n"), ("Holder", "Public Child As Leaf\r\n"), ("Leaf", "Public Value As Long\r\n"));
        AssertDebugLog([expected]);
    }

    // vb6.exe SP6 native and p-code, #3941; see the oracle's chained-writes page.
    [Theory]
    [InlineData("o.H.Value = 4", "o.H.Value", "4")]
    [InlineData("Module1.o.H.Value = 5", "o.H.Value", "5")]
    [InlineData("Module1.OP.Value = 6", "hh.Value", "6")]
    [InlineData("OP.Value = 7", "hh.Value", "7")]
    public async Task ObjectRootWriteStoresThroughTheReference(string store, string read, string expected)
    {
        await RunClasses("Dim o As Outer2\r\nDim hh As Holder\r\n" +
            "Property Get OP() As Holder\r\nSet OP = hh\r\nEnd Property\r\n" +
            "Set o = New Outer2\r\nSet o.H = New Holder\r\nSet hh = New Holder\r\n" + store +
            "\r\nDebug.Print CStr(" + read + ")", ("Outer2", "Public H As Holder\r\n"),
            ("Holder", "Public Value As Long\r\n"));
        AssertDebugLog([expected]);
    }

    [Theory]
    [InlineData("PT2.Z = 5")]
    [InlineData("Module2.PT2.Z = 6")]
    public async Task ForeignModuleUdtPropertyWriteDiscardsTheCopy(string store)
    {
        await RunModules("Module2.SetZ 3\r\nModule2.gets = 0\r\n" + store +
            "\r\nDebug.Print CStr(Module2.MZ())\r\nDebug.Print CStr(Module2.gets)",
            ("Module2", Record + "Dim mt As TU\r\nPublic gets As Long\r\n" +
                "Public Property Get PT2() As TU\r\ngets = gets + 1\r\nPT2 = mt\r\nEnd Property\r\n" +
                "Public Sub SetZ(ByVal n As Long)\r\nmt.Z = n\r\nEnd Sub\r\n" +
                "Public Function MZ() As Long\r\nMZ = mt.Z\r\nEnd Function\r\n"));
        AssertDebugLog(["3", "1"]);
    }

    private const string Record = "Public Type TU\r\nZ As Long\r\nEnd Type\r\n";
    private const string Box = "Private m As TU\r\nPublic Gets As Long\r\nPublic Lets As Long\r\n" +
        "Friend Property Get U() As TU\r\nGets = Gets + 1\r\nU = m\r\nEnd Property\r\n" +
        "Friend Function GetU() As TU\r\nGets = Gets + 1\r\nGetU = m\r\nEnd Function\r\n" +
        "Friend Sub SetZ(n)\r\nm.Z = n\r\nEnd Sub\r\nFriend Function MZ()\r\nMZ = m.Z\r\nEnd Function\r\n";
    private const string Setter = "Friend Property Let U(v As TU)\r\nLets = Lets + 1\r\nm = v\r\nEnd Property\r\n";

    [Theory]
    [InlineData("b.U.Z = 5", true, "3")]
    [InlineData("b.GetU().Z = 5", true, "3")]
    [InlineData("b.U.Z = 5", false, "0")]
    public async Task ObjectUdtReturnWriteDiscardsTheCopy(string store, bool setter, string expected)
    {
        await RunClasses(Record + "Dim b As Box\r\nSet b = New Box\r\n" + (setter ? "b.SetZ 3\r\n" : "") + store +
            "\r\nDebug.Print CStr(b.MZ())\r\nDebug.Print CStr(b.Gets)\r\nDebug.Print CStr(b.Lets)", ("Box", Box + (setter ? Setter : "")));
        AssertDebugLog([expected, "1", "0"]);
    }

    [Theory]
    [InlineData("Debug.Print CStr(b.U.Z)", "3")]
    [InlineData("With b.U\r\n.Z = 5\r\nDebug.Print CStr(.Z)\r\nEnd With", "5")]
    public async Task ObjectUdtReturnReadAndWithKeepTheirCopyBehaviour(string statement, string expected)
    {
        await RunClasses(Record + "Dim b As Box\r\nSet b = New Box\r\nb.SetZ 3\r\n" + statement +
            "\r\nDebug.Print CStr(b.MZ())\r\nDebug.Print CStr(b.Gets)\r\nDebug.Print CStr(b.Lets)", ("Box", Box + Setter));
        AssertDebugLog([expected, "3", "1", "0"]);
    }

    [Theory]
    [InlineData("PT.Z = 5")]
    [InlineData("Module1.PT.Z = 6")]
    [InlineData("GetT().Z = 7")]
    public async Task ModuleUdtReturnWriteDiscardsTheCopy(string store)
    {
        await Run(Record + "Dim mt As TU\r\nDim gets As Long\r\nDim lets As Long\r\n" +
            "Property Get PT() As TU\r\ngets = gets + 1\r\nPT = mt\r\nEnd Property\r\n" +
            "Property Let PT(v As TU)\r\nlets = lets + 1\r\nmt = v\r\nEnd Property\r\n" +
            "Function GetT() As TU\r\ngets = gets + 1\r\nGetT = mt\r\nEnd Function\r\n" +
            "mt.Z = 3\r\n" + store + "\r\nDebug.Print CStr(mt.Z)\r\nDebug.Print CStr(gets)\r\nDebug.Print CStr(lets)");
        AssertDebugLog(["3", "1", "0"]);
    }
}
