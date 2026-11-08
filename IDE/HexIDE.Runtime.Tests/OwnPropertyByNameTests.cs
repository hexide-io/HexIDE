using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A property named bare where the module table on #977 doesn't reach: a class's own, which is implicit Me, against a
/// standard module's Public one of the same name; and a property with a Let and no Get. Each program is a vb6.exe probe's,
/// with L for Debug.Print, and every expected line is what it logged, native and p-code alike (#977).
/// </summary>
public class OwnPropertyByNameTests : BaseVBTestFixture
{
    private const string ModulePg = """
        Option Explicit
        Public Property Get PG() As Long
            PG = 5
        End Property
        Public Property Let PG(ByVal v As Long)
            Debug.Print "module let " & v
        End Property
        Public Property Get First() As String
            First = "Module"
        End Property
        """;

    private const string OwnPg = """
        Option Explicit
        Private mPG As Long
        Public Property Get PG() As Long
            PG = 1 + mPG
        End Property
        Public Property Let PG(ByVal v As Long)
            Debug.Print "own let " & v
            mPG = v
        End Property
        Public Property Get First() As String
            First = "Ada"
        End Property
        Public Property Get Full() As String
            Full = First & "!"
        End Property
        Public Sub Go()
            Debug.Print "read " & PG
            PG = 9
            Debug.Print "after " & PG
            Debug.Print "full " & Full
        End Sub
        """;

    private const string NonePg = """
        Option Explicit
        Public Sub Go()
            Debug.Print "class2 read " & PG
            PG = 4
            Debug.Print "class2 first " & First
        End Sub
        """;

    private static List<Vb6Value> Lines(params string[] lines) => lines.Select(l => new Vb6Value(l)).ToList();

    private async Task RunProject(string main, (string Name, string Code)[] modules, (string Name, string Code)[] classes)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug, written, dialogs, SettingsStore), context, rootEnv, main, "Module1",
            modules, classes);
        await vb.Execute();
    }

    [Fact]
    public async Task A_class_reads_and_writes_its_own_property_by_name()
    {
        // Main's read showed Empty and a bare Let made a variable, so `Full = First & "!"` was "!" (c-own).
        await RunProject("""
            Option Explicit
            Dim c As New Class1
            c.Go
            Debug.Print "c-own: outside " & c.PG & " " & c.Full
            """, [], [("Class1", OwnPg)]);
        AssertDebugLog(Lines("read 1", "own let 9", "after 10", "full Ada!", "c-own: outside 10 Ada!"));
    }

    [Fact]
    public async Task A_class_s_own_property_wins_over_a_standard_module_s_of_the_name()
    {
        // Class2 declares neither, so it reaches Module2's, as Main does (c-collide).
        await RunProject("""
            Option Explicit
            Dim c As New Class1, d As New Class2
            c.Go
            d.Go
            Debug.Print "c-collide: main " & PG & " " & First
            """, [("Module2", ModulePg)], [("Class1", OwnPg), ("Class2", NonePg)]);
        AssertDebugLog(Lines("read 1", "own let 9", "after 10", "full Ada!", "class2 read 5", "module let 4",
            "class2 first Module", "c-collide: main 5 Module"));
    }

    [Fact]
    public async Task A_property_with_a_Let_and_no_Get_is_assigned_to()
    {
        // Bare and qualified, from another module (w-writeonly).
        await RunModules("""
            Option Explicit
            PW = 5
            Module2.PW = 6
            Debug.Print "w-writeonly: done"
            """, ("Module2", """
            Option Explicit
            Public Property Let PW(ByVal v As Long)
                Debug.Print "pw let " & v
            End Property
            """));
        AssertDebugLog(Lines("pw let 5", "pw let 6", "w-writeonly: done"));
    }

    [Fact]
    public async Task A_class_s_own_Property_Get_named_as_a_statement_is_invalid_use()
    {
        // vb6.exe refuses to compile it: "Invalid use of property" (c-statement). Here, as the statement runs.
        var run = async () => await RunClasses("Dim c As New Class1\nc.Go\n", ("Class1", """
            Public Property Get PG() As Long
                PG = 1
            End Property
            Public Sub Go()
                PG
            End Sub
            """));
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Invalid use of property");
    }
}
