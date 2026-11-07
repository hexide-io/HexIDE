using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A store into a <c>Const</c> or an Enum member is refused (#1003). It succeeded for a Const, so every later read gave
/// the new value. VB6 refuses each of these at compile time, and the message depends on the statement; measured on
/// vb6.exe, native and p-code agreeing, with a Const and an Enum member alike. HexIDE raises the same compile error as the
/// statement runs (interpreter-core:40-42). The probes are in docs/vb6-fidelity-oracle/what-a-store-into-a-const-is.md, <i>What a store into a Const
/// is</i>.
/// </summary>
public class ConstStoreTests : BaseVBTestFixture
{
    private const string AssignmentToConstant = "Assignment to constant not permitted";
    private const string VariableRequired = "Variable required - can't assign to this expression";
    private const string ExpectedArray = "Expected array";

    private const string Enum = "Enum E\n    eOne = 1\nEnd Enum\n";

    [Theory]
    [InlineData("Const MK = 1\n", "MK = 2", AssignmentToConstant)]
    [InlineData("Const MK = 1\n", "Let MK = 2", AssignmentToConstant)]
    [InlineData("", "Const LK = 1\n    LK = 2", AssignmentToConstant)]
    [InlineData("Const MK = 1\n", "Set MK = Nothing", AssignmentToConstant)]
    [InlineData("Const MK = 1\n", "MK = MK + 1", AssignmentToConstant)]
    [InlineData("Const MK = 1\n", "For MK = 1 To 2\n    Next", VariableRequired)]
    [InlineData("Const MK = 1\n", "Dim arr(1)\n    For Each MK In arr\n    Next", VariableRequired)]
    [InlineData("Const MK = 1\n", "ReDim MK(2)", ExpectedArray)]
    [InlineData("Const MK = 1\n", "Erase MK", ExpectedArray)]
    [InlineData("Const MS = \"abc\"\n", "Mid(MS, 1, 1) = \"x\"", VariableRequired)]
    [InlineData(Enum, "eOne = 2", AssignmentToConstant)]
    [InlineData(Enum, "For eOne = 1 To 2\n    Next", VariableRequired)]
    [InlineData(Enum, "ReDim eOne(2)", ExpectedArray)]
    [InlineData(Enum, "Erase eOne", ExpectedArray)]
    public async Task A_store_into_a_constant_is_refused_as_VB6_refuses_it(string declarations, string statement, string message)
    {
        // `Module1.MK = 2` is covered by ModuleQualifiedStoreTests (#1006). LSet and Line Input into a Const,
        // measured as "Variable required", are not implemented.
        //
        // Under On Error Resume Next, as each probe was: a compile error is not trappable, so nothing steps over it.
        var act = async () => await Run($"{declarations}Main\nSub Main()\n    On Error Resume Next\n    {statement}\n    Debug.Print \"ran on\"\nEnd Sub\n");

        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain(message);
        debug.Should().BeEmpty();
    }

    [Fact]
    public async Task Another_modules_Private_Const_is_not_the_name_a_store_reaches()
    {
        // Measured (#1007's review): Module1's `Const X = 1` is Private, so Module2's `X = 5` makes a new variable there,
        // and both print what they hold. The refusal must not reach across modules: if it did, a valid program would stop.
        // A Public Const is refused, below.
        await RunModules("Const X = 1\nRun\nDebug.Print \"Module1's X = \" & X\n",
            ("Module2", "Sub Run()\n    X = 5\n    Debug.Print \"Module2's X = \" & X & \", \" & TypeName(X)\nEnd Sub\n"));

        AssertDebugLog([new Vb6Value("Module2's X = 5, Integer"), new Vb6Value("Module1's X = 1")]);
    }

    [Fact]
    public async Task Another_modules_Public_Const_is_refused_as_a_store()
    {
        // Measured: Module2's `Public Const M = 3` makes Module1's `M = 5` "Assignment to constant not permitted". HexIDE
        // made a new variable and printed 5, not seeing another module's Public names bare (#880).
        var act = async () => await RunModules("M = 5\nDebug.Print M\n", ("Module2", "Public Const M = 3\n"));

        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain(AssignmentToConstant);
        debug.Should().BeEmpty();
    }

    [Fact]
    public async Task A_class_refuses_a_store_into_its_own_Const()
    {
        var act = async () => await RunClasses("Dim c As Class1\nSet c = New Class1\nDebug.Print c.Bump\n",
            ("Class1", "Private Const CK = 1\nPublic Function Bump() As Integer\n    CK = 2\n    Bump = CK\nEnd Function\n"));

        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain(AssignmentToConstant);
    }

    [Fact]
    public async Task Each_instance_of_a_class_fills_its_Const_again()
    {
        // The Const statement fills the slot the pre-pass hoisted, and a class's declarations run again for each
        // instance. That fill is not a store, so it is not refused.
        await RunClasses("Dim a As Class1, b As Class1\nSet a = New Class1\nSet b = New Class1\nDebug.Print a.K + b.K\n",
            ("Class1", "Private Const CK = 1\nPublic Function K() As Integer\n    K = CK\nEnd Function\n"));

        AssertDebugLog([new Vb6Value(2)]);
    }
}
