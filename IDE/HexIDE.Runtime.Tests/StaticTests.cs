using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Static n As Long</c> (#1491), which stopped the program. A Static local keeps its value from one call to the next:
/// one slot per declaration, shared by recursive calls, kept per instance in a class, and released with the instance,
/// after its fields. A <c>Static Sub</c> or <c>Function</c> makes every local Static, but not a Function's own return
/// value. Each transcript is what <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing
/// (the oracle's "Static").
/// </summary>
public class StaticTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Says when an object ends.
    private const string Ends = """
        Public Name As String
        Private Sub Class_Terminate()
            Debug.Print "  term " & Name
        End Sub

        """;

    // Counts per instance: a field, three methods' Statics, a Static Sub, and a Static reached through a private call.
    private const string Counter = """
        Private f As Ends
        Public Name As String
        Public Sub Hold()
            Static s As Ends
            Static n As Long
            If f Is Nothing Then Set f = New Ends: f.Name = Name & ".f"
            If s Is Nothing Then Set s = New Ends: s.Name = Name & ".Hold"
            n = n + 1
            Debug.Print "  " & Name & " hold " & n
        End Sub
        Public Sub A()
            Static sa As Ends
            If sa Is Nothing Then Set sa = New Ends: sa.Name = Name & ".A"
            Debug.Print "  " & Name & " A"
        End Sub
        Public Sub B()
            Static sb As Ends
            If sb Is Nothing Then Set sb = New Ends: sb.Name = Name & ".B"
            Debug.Print "  " & Name & " B"
        End Sub
        Public Static Sub Tally()
            Dim c As Long
            c = c + 1
            Debug.Print "  " & Name & " tally " & c
        End Sub
        Public Sub Outer()
            Inner
        End Sub
        Private Sub Inner()
            Static calls As Long
            calls = calls + 1
            Debug.Print "  " & Name & " inner " & calls
        End Sub
        Private Sub Class_Initialize()
            Static times As Long
            times = times + 1
            Debug.Print "  Counter init, times " & times
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "  Counter term " & Name
        End Sub

        """;

    // Says when it is made and when it ends.
    private const string Made = """
        Public Name As String
        Private Sub Class_Initialize()
            Debug.Print "  made"
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "  term " & Name
        End Sub

        """;

    // A Collection As New, kept per instance.
    private const string Holder = """
        Public Name As String
        Public Sub Add()
            Static c As New Collection
            c.Add 1
            Debug.Print "  " & Name & " count " & c.Count
        End Sub

        """;

    [Fact]
    public async Task A_Static_keeps_its_value_from_one_call_to_the_next()
    {
        await Run("""
            Private Sub Counter()
                Static n As Long
                n = n + 1
                Debug.Print "  counter " & n
            End Sub
            Private Sub Twin()
                Static n As Long
                n = n + 10
                Debug.Print "  twin " & n
            End Sub
            Private Sub Recurse(ByVal depth As Long)
                Static n As Long
                n = n + 1
                Debug.Print "  depth " & depth & ", n " & n
                If depth < 2 Then Recurse depth + 1
                Debug.Print "  back at depth " & depth & ", n " & n
            End Sub
            Private Static Sub AllStatic()
                Dim a As Long
                Dim s As String
                a = a + 1
                s = s & "z"
                Debug.Print "  AllStatic a " & a & ", s " & s
            End Sub
            Private Static Function StaticF() As Long
                Dim m As Long
                m = m + 1
                StaticF = StaticF + 1
                Debug.Print "  StaticF m " & m & ", StaticF before return " & StaticF
            End Function
            Private Sub Bump(ByRef x As Long)
                x = x + 100
            End Sub
            Private Sub ByRefStatic()
                Static n As Long
                Bump n
                Debug.Print "  after Bump, n " & n
            End Sub
            Private Sub InALoop()
                Dim i As Long
                For i = 1 To 3
                    Static q As Long
                    q = q + 1
                Next
                Debug.Print "  q " & q
            End Sub
            Private Sub Untaken(ByVal take As Boolean)
                If take Then
                    Static u As Long
                End If
                u = u + 1
                Debug.Print "  u " & u
            End Sub

            Dim r As Long
            Debug.Print "counter:": Counter: Counter: Counter
            Debug.Print "twin:": Twin: Twin
            Debug.Print "recurse:": Recurse 0
            Debug.Print "recurse again:": Recurse 2
            Debug.Print "AllStatic:": AllStatic: AllStatic
            Debug.Print "StaticF:"
            r = StaticF(): Debug.Print "  returned " & r
            r = StaticF(): Debug.Print "  returned " & r
            Debug.Print "ByRef:": ByRefStatic: ByRefStatic
            Debug.Print "in a loop:": InALoop: InALoop
            Debug.Print "untaken:": Untaken False: Untaken False

            """);

        AssertDebugLog(Lines("""
            counter:
              counter 1
              counter 2
              counter 3
            twin:
              twin 10
              twin 20
            recurse:
              depth 0, n 1
              depth 1, n 2
              depth 2, n 3
              back at depth 2, n 3
              back at depth 1, n 3
              back at depth 0, n 3
            recurse again:
              depth 2, n 4
              back at depth 2, n 4
            AllStatic:
              AllStatic a 1, s z
              AllStatic a 2, s zz
            StaticF:
              StaticF m 1, StaticF before return 1
              returned 1
              StaticF m 2, StaticF before return 1
              returned 1
            ByRef:
              after Bump, n 100
              after Bump, n 200
            in a loop:
              q 3
              q 6
            untaken:
              u 1
              u 2
            """));
    }

    [Fact]
    public async Task A_Static_starts_at_its_types_zero_and_keeps_whatever_it_holds()
    {
        await RunClasses("""
            Private Type P
                x As Long
                s As String
            End Type
            Private Sub Firsts()
                Static i As Integer, lg As Long, b As Byte, bo As Boolean, si As Single, d As Double, cu As Currency
                Static dt As Date, s As String, v As Variant, u, o As Object, k As Ends, c As Collection
                Static fa(2) As Long, da() As Long, ud As P
                Debug.Print "  Integer " & TypeName(i) & " " & i & ", Long " & TypeName(lg) & " " & lg & ", Byte " & TypeName(b) & " " & b
                Debug.Print "  Boolean " & bo & ", Single " & TypeName(si) & ", Double " & TypeName(d) & ", Currency " & TypeName(cu)
                Debug.Print "  Date " & TypeName(dt) & " " & CDbl(dt) & ", String [" & s & "] " & TypeName(s)
                Debug.Print "  Variant " & TypeName(v) & ", untyped " & TypeName(u) & ", Object " & TypeName(o) & ", Ends " & TypeName(k) & ", Collection " & TypeName(c)
                Debug.Print "  fixed array " & TypeName(fa) & " " & UBound(fa) & ", dynamic " & TypeName(da) & ", UDT x " & ud.x & " s [" & ud.s & "]"
            End Sub
            Private Sub Keeps(ByVal last As Boolean)
                Static fa(2) As Long, da() As Long, ud As P, s As String, v As Variant, sized As Boolean
                Static t As Ends, c As Collection
                fa(1) = fa(1) + 1
                If sized Then ReDim Preserve da(UBound(da) + 1) Else ReDim da(0): sized = True
                ud.x = ud.x + 1: ud.s = ud.s & "x"
                s = s & "s"
                If IsEmpty(v) Then v = 1 Else v = v * 2
                If t Is Nothing Then Set t = New Ends: t.Name = "kept": Debug.Print "  made an Ends"
                If c Is Nothing Then Set c = New Collection
                c.Add fa(1)
                Debug.Print "  fa(1) " & fa(1) & ", UBound(da) " & UBound(da) & ", ud " & ud.x & " " & ud.s & ", s " & s & ", v " & v & ", c.Count " & c.Count
                If last Then Set t = Nothing: Debug.Print "  released"
            End Sub
            Private Sub Erased()
                Static fa(1) As Long, da() As Long, sized As Boolean
                fa(0) = fa(0) + 1
                If Not sized Then ReDim da(3): sized = True
                Debug.Print "  fa(0) " & fa(0) & ", UBound(da) " & UBound(da)
                Erase fa
                Erase da
                ReDim da(1)
            End Sub

            Debug.Print "firsts:": Firsts
            Debug.Print "keeps:": Keeps False: Keeps False: Keeps True
            Debug.Print "erased:": Erased: Erased
            Debug.Print "end"

            """, ("Ends", Ends));

        AssertDebugLog(Lines("""
            firsts:
              Integer Integer 0, Long Long 0, Byte Byte 0
              Boolean False, Single Single, Double Double, Currency Currency
              Date Date 0, String [] String
              Variant Empty, untyped Empty, Object Nothing, Ends Nothing, Collection Nothing
              fixed array Long() 2, dynamic Long(), UDT x 0 s []
            keeps:
              made an Ends
              fa(1) 1, UBound(da) 0, ud 1 x, s s, v 1, c.Count 1
              fa(1) 2, UBound(da) 1, ud 2 xx, s ss, v 2, c.Count 2
              fa(1) 3, UBound(da) 2, ud 3 xxx, s sss, v 4, c.Count 3
              term kept
              released
            erased:
              fa(0) 1, UBound(da) 3
              fa(0) 1, UBound(da) 1
            end
            """));
    }

    [Fact]
    public async Task A_classs_Statics_are_its_instances_and_end_with_it_after_its_fields()
    {
        await RunClasses("""
            Dim k As Counter, j As Counter
            Debug.Print "class:"
            Set k = New Counter: k.Name = "k"
            Set j = New Counter: j.Name = "j"
            k.Hold: k.Hold: j.Hold
            k.B: k.A
            k.Tally: k.Tally: j.Tally
            k.Outer: k.Outer: j.Outer
            Debug.Print "  Set k = Nothing"
            Set k = Nothing
            Debug.Print "  Set j = Nothing"
            Set j = Nothing
            Set k = New Counter: k.Name = "k again"
            k.Hold: k.Tally
            Set k = Nothing
            Debug.Print "end"

            """, ("Counter", Counter), ("Ends", Ends));

        AssertDebugLog(Lines("""
            class:
              Counter init, times 1
              Counter init, times 1
              k hold 1
              k hold 2
              j hold 1
              k B
              k A
              k tally 1
              k tally 2
              j tally 1
              k inner 1
              k inner 2
              j inner 1
              Set k = Nothing
              Counter term k
              term k.f
              term k.Hold
              term k.A
              term k.B
              Set j = Nothing
              Counter term j
              term j.f
              term j.Hold
              Counter init, times 1
              k again hold 1
              k again tally 1
              Counter term k again
              term k again.f
              term k again.Hold
            end
            """));
    }

    [Fact]
    public async Task A_Static_As_New_is_made_at_its_first_use_and_kept_until_set_to_Nothing()
    {
        await RunClasses("""
            Private Sub Uses(ByVal last As Boolean)
                Static s As New Made
                s.Name = s.Name & "x"
                Debug.Print "  uses " & s.Name
                If last Then Set s = Nothing
            End Sub
            Private Sub Clears(ByVal clear As Boolean)
                Static c As New Made
                Debug.Print "  c.Name [" & c.Name & "]"
                c.Name = "set"
                If clear Then Set c = Nothing: Debug.Print "  cleared"
            End Sub

            Dim h As Holder, h2 As Holder
            Debug.Print "module:"
            Uses False: Uses False: Uses True
            Debug.Print "after Nothing:"
            Clears True: Clears False: Clears True
            Debug.Print "class:"
            Set h = New Holder: h.Name = "h"
            Set h2 = New Holder: h2.Name = "h2"
            h.Add: h.Add: h2.Add
            Set h = Nothing: Set h2 = Nothing
            Debug.Print "end"

            """, ("Made", Made), ("Holder", Holder));

        AssertDebugLog(Lines("""
            module:
              made
              uses x
              uses xx
              uses xxx
              term xxx
            after Nothing:
              made
              c.Name []
              term set
              cleared
              made
              c.Name []
              c.Name [set]
              term set
              cleared
            class:
              h count 1
              h count 2
              h2 count 1
            end
            """));
    }

    [Fact]
    public async Task Static_outside_a_procedure_is_refused()
    {
        var run = () => Run("Static n As Long\nSub S()\nEnd Sub\nS\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Invalid outside procedure*");
    }

    // VB6 refuses both. `Sub S(Static x As Long)` is "Expected: identifier", and "Syntax error" when the Sub is Private;
    // the Const is "Syntax error" (measured). Neither parses here.
    [Theory]
    [InlineData("Sub S(Static x As Long)\nEnd Sub\nS 1")]
    [InlineData("Sub S()\n    Static Const C = 1\nEnd Sub\nS")]
    public async Task A_Static_parameter_or_Const_does_not_parse(string program)
    {
        var run = () => Run(program + "\n");

        await run.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task Static_at_a_class_modules_level_is_refused()
    {
        var run = () => RunClasses("Dim c As C\nSet c = New C\n", ("C", "Static n As Long\nPublic Sub Go()\nEnd Sub\n"));

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Invalid outside procedure*");
    }
}
