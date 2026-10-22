using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Dim o As New C1</c> (#435), which stopped the program before it ran. The variable holds Nothing until it is used, and
/// every use makes the object: <c>o Is Nothing</c> and <c>TypeName(o)</c> as much as <c>o.Go</c>. After
/// <c>Set o = Nothing</c> the next use makes another. Each transcript is what <c>vb6.exe</c> logged for the same program
/// under en-GB, native and p-code agreeing (the oracle's "As New").
/// </summary>
public class AsNewTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Each statement's outcome: "ok", or the error it raised.
    private const string Helper = """
        Public Made As Long
        Private Sub R(ByVal label As String)
            If Err.Number <> 0 Then Debug.Print label & " -> err " & Err.Number Else Debug.Print label & " -> ok"
            Err.Clear
        End Sub

        """;

    // Numbers each object it makes, and says when it starts and ends.
    private const string C1 = """
        Public Name As String
        Public Id As Long
        Private Sub Class_Initialize()
            Made = Made + 1
            Id = Made
            Debug.Print "  init " & Id
        End Sub
        Private Sub Class_Terminate()
            If Name = "" Then Debug.Print "  term " & Id Else Debug.Print "  term " & Id & " " & Name
        End Sub
        Public Sub Go()
            Debug.Print "  go " & Id
        End Sub

        """;

    private const string C2 = """
        Private Sub Class_Initialize()
            Debug.Print "  C2 init raises"
            Err.Raise 5
        End Sub
        Public Sub Go()
            Debug.Print "  C2 go"
        End Sub

        """;

    private const string H = """
        Private m As New C1
        Public o As New C1
        Public c As New Collection
        Public Sub Touch()
            m.Name = "field"
            m.Go
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "  H term"
        End Sub

        """;

    // The issue's own class.
    private const string Went = """
        Public Sub Go()
            Debug.Print "went"
        End Sub

        """;

    [Fact]
    public async Task The_issues_own_program_runs()
    {
        await RunClasses("""
            Dim o As New Went
            o.Go

            """, ("Went", Went));

        AssertDebugLog(Lines("""
            went
            """));
    }

    [Fact]
    public async Task Every_use_makes_the_object_and_nothing_else_does()
    {
        await RunClasses(Helper + """
            Private Sub TakesRef(x As C1)
                Debug.Print "  in TakesRef, x Is Nothing " & (x Is Nothing)
            End Sub
            Private Sub TakesVal(ByVal x As C1)
                Debug.Print "  in TakesVal, x Is Nothing " & (x Is Nothing)
            End Sub
            Private Sub TakesVariant(ByVal x As Variant)
                Debug.Print "  in TakesVariant, TypeName(x) " & TypeName(x)
            End Sub
            Private Sub Untouched()
                Dim o As New C1
                Debug.Print "untouched:"
            End Sub
            Private Sub Uses()
                On Error Resume Next
                Dim o As New C1, p As C1, v As Variant
                Debug.Print "uses:"
                Debug.Print "  Is Nothing " & (o Is Nothing)
                Set o = Nothing
                Debug.Print "  TypeName " & TypeName(o)
                Set o = Nothing
                Debug.Print "  VarType " & VarType(o) & ", IsObject " & IsObject(o) & ", TypeOf Is C1 " & (TypeOf o Is C1)
                Set o = Nothing
                Set p = o: Debug.Print "  Set p = o, p Is Nothing " & (p Is Nothing)
                Set p = Nothing: Set o = Nothing
                Set v = o: Debug.Print "  Set v = o, TypeName(v) " & TypeName(v)
                Set v = Nothing: Set o = Nothing
                v = o: R "  v = o"
                Set o = Nothing
                TakesRef o: Debug.Print "  after TakesRef, o.Id " & o.Id
                Set o = Nothing
                TakesVal o
                Set o = Nothing
                TakesVariant o
                Set o = Nothing
                With o: .Name = "w": End With
                Set o = Nothing
                Debug.Print "  o Is p " & (o Is p)
                Set o = Nothing
                Debug.Print "  p Is o " & (p Is o)
                Debug.Print "  leaving"
            End Sub
            Private Sub NotUses()
                Dim o As New C1, q As New C1
                Debug.Print "not uses:"
                Set o = Nothing
                Debug.Print "  after Set o = Nothing, untouched"
                Set q = New C1
                Debug.Print "  after Set q = New C1"
                Debug.Print "  leaving"
            End Sub

            Untouched
            Uses
            NotUses

            """, ("C1", C1));

        AssertDebugLog(Lines("""
            untouched:
            uses:
              init 1
              Is Nothing False
              term 1
              init 2
              TypeName C1
              term 2
              init 3
              VarType 9, IsObject True, TypeOf Is C1 True
              term 3
              init 4
              Set p = o, p Is Nothing False
              term 4
              init 5
              Set v = o, TypeName(v) C1
              term 5
              init 6
              v = o -> err 438
              term 6
              init 7
              in TakesRef, x Is Nothing False
              after TakesRef, o.Id 7
              term 7
              init 8
              in TakesVal, x Is Nothing False
              term 8
              init 9
              in TakesVariant, TypeName(x) C1
              term 9
              init 10
              term 10 w
              init 11
              o Is p False
              term 11
              init 12
              p Is o False
              leaving
              term 12
            not uses:
              after Set o = Nothing, untouched
              init 13
              after Set q = New C1
              leaving
              term 13
            """));
    }

    [Fact]
    public async Task Set_to_Nothing_the_next_use_makes_another()
    {
        await RunClasses(Helper + """
            Private Sub Clear(x As C1)
                Set x = Nothing
                Debug.Print "  in Clear, x Is Nothing " & (x Is Nothing)
            End Sub
            Private Sub ClearSameName(o As C1)
                Set o = Nothing
                Debug.Print "  in ClearSameName, o Is Nothing " & (o Is Nothing)
            End Sub
            Private Sub Again()
                Dim o As New C1, p As C1
                Debug.Print "again:"
                o.Name = "first": o.Go
                Set o = Nothing
                Debug.Print "  after Set o = Nothing"
                o.Go
                Debug.Print "  o.Name now [" & o.Name & "]"
                Set o = p
                Debug.Print "  after Set o = p, p Nothing"
                o.Go
                Set p = o
                Set o = Nothing
                Debug.Print "  p still holds " & p.Id
                o.Go
                Debug.Print "  p Is o " & (p Is o)
                Debug.Print "  leaving"
            End Sub
            Private Sub ByRefClear()
                Dim o As New C1
                Debug.Print "a ByRef callee:"
                o.Go
                Clear o
                Debug.Print "  back"
                o.Go
                ClearSameName o
                Debug.Print "  back again"
                o.Go
                Debug.Print "  leaving"
            End Sub
            Private Sub TwoInOne()
                Dim a As New C1, b As New C1
                Debug.Print "two in one Dim:"
                b.Go
                a.Go
                Debug.Print "  leaving"
            End Sub
            Private Sub InLoop()
                Dim i As Long
                Debug.Print "Dim in a loop:"
                For i = 1 To 3
                    Dim q As New C1
                    q.Go
                Next
                Debug.Print "  leaving"
            End Sub

            Again
            ByRefClear
            TwoInOne
            InLoop

            """, ("C1", C1));

        AssertDebugLog(Lines("""
            again:
              init 1
              go 1
              term 1 first
              after Set o = Nothing
              init 2
              go 2
              o.Name now []
              term 2
              after Set o = p, p Nothing
              init 3
              go 3
              p still holds 3
              init 4
              go 4
              p Is o False
              leaving
              term 4
              term 3
            a ByRef callee:
              init 5
              go 5
              term 5
              in Clear, x Is Nothing True
              back
              init 6
              go 6
              term 6
              in ClearSameName, o Is Nothing True
              back again
              init 7
              go 7
              leaving
              term 7
            two in one Dim:
              init 8
              go 8
              init 9
              go 9
              leaving
              term 9
              term 8
            Dim in a loop:
              init 10
              go 10
              go 10
              go 10
              leaving
              term 10
            """));
    }

    [Fact]
    public async Task A_Collection_a_module_variable_and_a_class_field_make_theirs_the_same_way()
    {
        await RunClasses("""
            Public Made As Long
            Dim mo As New C1
            Private Sub Collections()
                Dim c As New Collection, d As New Collection, e As New Collection, f As New Collection, v As Variant
                Debug.Print "collections:"
                c.Add 1: c.Add 2
                Debug.Print "  c.Count " & c.Count
                Set c = Nothing
                Debug.Print "  c.Count after Set c = Nothing " & c.Count
                For Each v In d
                    Debug.Print "  never"
                Next
                Debug.Print "  after For Each over an untouched one"
                Debug.Print "  e Is Nothing " & (e Is Nothing)
                Debug.Print "  TypeName(f) " & TypeName(f) & ", f.Count " & f.Count
            End Sub
            Private Sub Fields()
                Dim h As H
                Debug.Print "fields:"
                Set h = New H
                Debug.Print "  made H"
                h.Touch
                h.c.Add 5
                Debug.Print "  h.c.Count " & h.c.Count
                h.o.Go
                Set h.o = Nothing
                Debug.Print "  after Set h.o = Nothing"
                h.o.Go
                Set h.c = Nothing
                Debug.Print "  h.c.Count after Set h.c = Nothing " & h.c.Count
                Set h = Nothing
                Debug.Print "  after Set h = Nothing"
            End Sub

            Debug.Print "module:"
            mo.Go
            mo.Go
            Set mo = Nothing
            Debug.Print "  after Set mo = Nothing"
            mo.Go
            Set mo = Nothing
            Collections
            Fields
            Debug.Print "end"

            """, ("C1", C1), ("H", H));

        AssertDebugLog(Lines("""
            module:
              init 1
              go 1
              go 1
              term 1
              after Set mo = Nothing
              init 2
              go 2
              term 2
            collections:
              c.Count 2
              c.Count after Set c = Nothing 0
              after For Each over an untouched one
              e Is Nothing False
              TypeName(f) Collection, f.Count 0
            fields:
              made H
              init 3
              go 3
              h.c.Count 1
              init 4
              go 4
              term 4
              after Set h.o = Nothing
              init 5
              go 5
              h.c.Count after Set h.c = Nothing 0
              H term
              term 3 field
              term 5
              after Set h = Nothing
            end
            """));
    }

    [Fact]
    public async Task An_Initialize_that_raises_raises_at_every_use()
    {
        await RunClasses(Helper + """
            Private Sub Raises()
                On Error Resume Next
                Dim e As New C2
                Debug.Print "raises:"
                e.Go: R "  e.Go"
                e.Go: R "  e.Go again"
                Debug.Print "  Is Nothing " & (e Is Nothing): R "  Is Nothing"
            End Sub
            Private Sub Handled()
                On Error GoTo Handler
                Dim e As New C2
                Debug.Print "with a handler:"
                e.Go
                Debug.Print "  after"
                Exit Sub
            Handler:
                Debug.Print "  handler " & Err.Number
            End Sub

            Raises
            Handled

            """, ("C2", C2));

        AssertDebugLog(Lines("""
            raises:
              C2 init raises
              e.Go -> err 5
              C2 init raises
              e.Go again -> err 5
              C2 init raises
              Is Nothing -> err 5
            with a handler:
              C2 init raises
              handler 5
            """));
    }

    [Theory]
    [InlineData("Dim x As New Integer", "*Syntax error*")]
    [InlineData("Dim x As New String", "*Syntax error*")]
    [InlineData("Dim x As New Variant", "*Syntax error*")]
    [InlineData("Dim x As New Object", "*Syntax error*")]
    [InlineData("Dim x As New E", "*Invalid use of New keyword*")]
    [InlineData("Dim x As New U", "*Invalid use of New keyword*")]
    [InlineData("Dim d As New Collection: d = 5", "*Argument not optional*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string statement, string message)
    {
        var run = () => Run("""
            Private Enum E
                One
            End Enum
            Private Type U
                n As Long
            End Type

            """ + statement + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }

    [Theory]
    [InlineData("Dim a(1) As New Collection\na(0).Add 1", "*#1482*")]
    [InlineData("Sub S()\n    Dim a(1) As New Collection\nEnd Sub\nS", "*#1482*")]
    [InlineData("Sub S()\n    ReDim b(1) As New Collection\nEnd Sub\nS", "*#1482*")]
    public async Task An_array_declared_As_New_stops_the_run_naming_its_issue(string statements, string message)
    {
        var run = () => Run(statements + "\n");

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage(message);
    }
}
