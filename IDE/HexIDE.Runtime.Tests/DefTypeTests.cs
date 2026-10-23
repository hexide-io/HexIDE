using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>DefInt A-Z</c> and the rest of DefType (#169), which stopped the program. A DefType types every declaration in its
/// module that has no As clause, by the first letter of its name: an undeclared variable, a <c>Dim</c> or <c>Static</c>,
/// a field, a parameter and a Function's return. A <c>Const</c> keeps its value's type. Each transcript is what
/// <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing (the oracle's "DefType").
/// </summary>
public class DefTypeTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // A class under its own DefInt: a field, a local, and a method's return.
    private const string H = """
        DefInt A-Z
        Private cnt
        Public Function Count()
            cnt = cnt + 1.6
            Count = cnt
        End Function
        Public Function Kinds() As String
            Dim q
            q = 2.6
            Kinds = "field cnt " & TypeName(cnt) & ", local q " & TypeName(q) & " " & q & ", Count " & TypeName(Count())
        End Function

        """;

    // A module with no DefType of its own.
    private const string Module2 = """
        Public Function M2Type()
            i2 = 2.6
            M2Type = "Module2 i2 " & TypeName(i2)
        End Function

        """;

    [Fact]
    public async Task DefType_types_every_declaration_without_an_As_clause()
    {
        await Run("""
            DefBool B
            DefByte Y
            DefInt I-J, K
            DefLng N
            DefCur C
            DefSng S
            DefDbl D
            DefDate T
            DefStr Z
            DefObj O
            DefVar V
            Const I8 = 2.6
            Const Z8 = 5
            Dim im, zm, am
            Private Sub R(ByVal label As String)
                If Err.Number <> 0 Then Debug.Print label & " -> err " & Err.Number Else Debug.Print label & " -> ok"
                Err.Clear
            End Sub
            Private Function IFun()
                IFun = 2.6
            End Function
            Private Function ZFun()
                ZFun = 5
            End Function
            Private Function AFun()
                AFun = 2.6
            End Function
            Private Sub ITake(i7)
                Debug.Print "  ITake: TypeName(i7) " & TypeName(i7) & " " & i7
            End Sub
            Private Sub ZTake(ByVal z7)
                Debug.Print "  ZTake: TypeName(z7) " & TypeName(z7) & " [" & z7 & "]"
            End Sub
            Private Sub Counts()
                Static is1
                is1 = is1 + 1
                Debug.Print "  Static is1: " & TypeName(is1) & " " & is1
            End Sub
            Private Sub Kinds()
                On Error Resume Next
                Debug.Print "kinds, assigned:"
                b1 = 1: Debug.Print "  b1 " & TypeName(b1) & " " & b1
                y1 = 300: R "  y1 = 300": y1 = 200: Debug.Print "  y1 " & TypeName(y1) & " " & y1
                i1 = 2.6: Debug.Print "  i1 " & TypeName(i1) & " " & i1
                j1 = 2.5: Debug.Print "  j1 " & TypeName(j1) & " " & j1
                k1 = 40000: R "  k1 = 40000"
                n1 = 70000: Debug.Print "  n1 " & TypeName(n1) & " " & n1
                c1 = 1.23456: Debug.Print "  c1 " & TypeName(c1) & " " & c1
                s1 = 1.5: Debug.Print "  s1 " & TypeName(s1)
                d1 = 1.5: Debug.Print "  d1 " & TypeName(d1)
                t1 = 2: Debug.Print "  t1 " & TypeName(t1) & " " & CDbl(t1)
                z1 = 5: Debug.Print "  z1 " & TypeName(z1) & " [" & z1 & "]"
                Set o1 = New Collection: R "  Set o1 = New Collection": Debug.Print "  o1 " & TypeName(o1)
                Set a2 = New Collection: R "  Set a2 = New Collection": Debug.Print "  a2, no Deftype " & TypeName(a2)
                v1 = 5: Debug.Print "  v1 " & TypeName(v1)
                a1 = 2.6: Debug.Print "  a1, no Deftype " & TypeName(a1)
                Debug.Print "kinds, never assigned:"
                Debug.Print "  i9 " & TypeName(i9) & " " & i9 & ", z9 " & TypeName(z9) & " [" & z9 & "], o9 " & TypeName(o9) & ", b9 " & TypeName(b9) & ", t9 " & TypeName(t9) & ", v9 " & TypeName(v9)
                Debug.Print "a For counter:"
                For i3 = 1 To 2: Next
                Debug.Print "  i3 " & TypeName(i3) & " " & i3
            End Sub
            Private Sub Untyped()
                Dim i5, z5, a5, i6(2)
                Debug.Print "Dim with no As:"
                Debug.Print "  i5 " & TypeName(i5) & ", z5 " & TypeName(z5) & ", a5 " & TypeName(a5) & ", i6 " & TypeName(i6)
                Debug.Print "  module im " & TypeName(im) & ", zm " & TypeName(zm) & ", am " & TypeName(am)
                Debug.Print "Const:"
                Debug.Print "  I8 " & TypeName(I8) & " " & I8 & ", Z8 " & TypeName(Z8)
                Debug.Print "returns:"
                Debug.Print "  IFun " & TypeName(IFun()) & " " & IFun() & ", ZFun " & TypeName(ZFun()) & ", AFun " & TypeName(AFun())
                Debug.Print "parameters:"
                ITake 2.6
                ZTake 5
                Debug.Print "Static:"
                Counts
                Counts
            End Sub

            Kinds
            Untyped

            """);

        AssertDebugLog(Lines("""
            kinds, assigned:
              b1 Boolean True
              y1 = 300 -> err 6
              y1 Byte 200
              i1 Integer 3
              j1 Integer 2
              k1 = 40000 -> err 6
              n1 Long 70000
              c1 Currency 1.2346
              s1 Single
              d1 Double
              t1 Date 2
              z1 String [5]
              Set o1 = New Collection -> ok
              o1 Collection
              Set a2 = New Collection -> ok
              a2, no Deftype Collection
              v1 Integer
              a1, no Deftype Double
            kinds, never assigned:
              i9 Integer 0, z9 String [], o9 Nothing, b9 Boolean, t9 Date, v9 Empty
            a For counter:
              i3 Integer 3
            Dim with no As:
              i5 Integer, z5 String, a5 Empty, i6 Integer()
              module im Integer, zm String, am Empty
            Const:
              I8 Double 2.6, Z8 Integer
            returns:
              IFun Integer 3, ZFun String, AFun Double
            parameters:
              ITake: TypeName(i7) Integer 3
              ZTake: TypeName(z7) String [5]
            Static:
              Static is1: Integer 1
              Static is1: Integer 2
            """));
    }

    [Fact]
    public async Task DefObj_makes_an_object_variable_as_As_Object_does()
    {
        await Run("""
            DefObj O
            Dim om
            Private Sub R(ByVal label As String)
                If Err.Number <> 0 Then Debug.Print label & " -> err " & Err.Number Else Debug.Print label & " -> ok"
                Err.Clear
            End Sub
            Private Function OFun()
                Set OFun = New Collection
            End Function
            Private Sub OTake(o7)
                Debug.Print "  OTake " & TypeName(o7)
            End Sub
            Private Sub OLet(o7)
                On Error Resume Next
                o7 = 5: R "  OLet: o7 = 5"
            End Sub
            Private Function OBad()
                On Error Resume Next
                OBad = 5: R "  OBad: OBad = 5"
            End Function
            Private Sub Show()
                On Error Resume Next
                Dim o5
                Debug.Print "Dim o5 " & TypeName(o5) & ", module om " & TypeName(om)
                Set o5 = New Collection: R "Set o5"
                Debug.Print "o5 " & TypeName(o5)
                Set om = New Collection: R "Set om"
                Debug.Print "om " & TypeName(om)
                o5 = 5: R "o5 = 5"
                Dim o8
                o8 = 5: R "o8 = 5"
                o6 = 5: R "o6 = 5, undeclared"
                Debug.Print "OFun " & TypeName(OFun())
                OTake New Collection
                OTake Nothing
                Dim on1
                OLet on1
                Debug.Print "OBad " & TypeName(OBad())
            End Sub

            Show

            """);

        AssertDebugLog(Lines("""
            Dim o5 Nothing, module om Nothing
            Set o5 -> ok
            o5 Collection
            Set om -> ok
            om Collection
            o5 = 5 -> err 438
            o8 = 5 -> err 91
            o6 = 5, undeclared -> err 91
            OFun Collection
              OTake Collection
              OTake Nothing
              OLet: o7 = 5 -> err 91
              OBad: OBad = 5 -> err 91
            OBad Nothing
            """));
    }

    // A DefType'd variable is a typed operand, as a Dim'd one is: Integer times Integer overflows where a Variant widens.
    [Fact]
    public async Task A_DefTyped_variable_is_typed_in_arithmetic()
    {
        await Run("""
            DefInt P
            Private Sub R(ByVal label As String)
                If Err.Number <> 0 Then Debug.Print label & " -> err " & Err.Number Else Debug.Print label & " -> ok"
                Err.Clear
            End Sub
            Private Function PFun()
                PFun = 30000
            End Function
            Private Sub Show()
                On Error Resume Next
                p1 = 30000
                a3 = p1 * 3: R "p1 * 3"
                a4 = p9 + 32767 + 1: R "p9 + 32767 + 1, p9 never assigned"
                Dim p5
                p5 = 30000
                a5 = p5 * 3: R "p5 * 3"
                a6 = PFun() * 3: R "PFun() * 3"
                b1 = 30000
                a7 = b1 * 3: R "b1 * 3, no Deftype"
                Debug.Print "  a7 " & TypeName(a7) & " " & a7
            End Sub

            Show

            """);

        AssertDebugLog(Lines("""
            p1 * 3 -> err 6
            p9 + 32767 + 1, p9 never assigned -> err 6
            p5 * 3 -> err 6
            PFun() * 3 -> err 6
            b1 * 3, no Deftype -> ok
              a7 Long 90000
            """));
    }

    [Fact]
    public async Task A_classs_DefType_types_its_fields_locals_and_returns()
    {
        await RunClasses("""
            Private Sub Show()
                Dim h As H
                Set h = New H
                Debug.Print h.Kinds()
                Debug.Print TypeName(h.Count())
            End Sub

            Show

            """, ("H", H));

        AssertDebugLog(Lines("""
            field cnt Integer, local q Integer 3, Count Integer
            Integer
            """));
    }

    [Fact]
    public async Task DefType_is_its_modules_own()
    {
        await RunModules("""
            DefInt A-Z
            Private Sub Show()
                i1 = 2.6
                Debug.Print "Module1 i1 " & TypeName(i1)
                Debug.Print M2Type()
            End Sub

            Show

            """, ("Module2", Module2));

        AssertDebugLog(Lines("""
            Module1 i1 Integer
            Module2 i2 Double
            """));
    }

    [Fact]
    public async Task A_range_may_be_written_backwards_and_in_lower_case()
    {
        await Run("""
            DefStr Z-X
            DefInt a-c
            Private Sub Show()
                x = 5
                b = 2.6
                Debug.Print "x " & TypeName(x) & ", b " & TypeName(b) & " " & b
            End Sub

            Show

            """);

        AssertDebugLog(Lines("""
            x String, b Integer 3
            """));
    }

    // A DefType must come before every declaration, but an Option statement or another DefType may come first.
    [Fact]
    public async Task Option_statements_and_other_DefTypes_may_come_first()
    {
        await Run("""
            Option Base 1
            Option Compare Text
            DefStr S
            DefInt X
            Private Sub Show()
                x = 2.6
                s = 5
                Debug.Print "x " & TypeName(x) & " " & x & ", s " & TypeName(s)
            End Sub

            Show

            """);

        AssertDebugLog(Lines("""
            x Integer 3, s String
            """));
    }

    [Theory]
    [InlineData("DefInt A-C\nDefInt B\nSub S()\nEnd Sub\nS", "*Duplicate Deftype statement*")]
    [InlineData("DefInt A-Z\nDefStr A-Z\nSub S()\nEnd Sub\nS", "*Duplicate Deftype statement*")]
    [InlineData("Dim q\nDefInt A-Z\nSub S()\nEnd Sub\nS", "*Deftype statements must precede declarations*")]
    [InlineData("Public pv As Long\nDefInt X\nSub S()\nEnd Sub\nS", "*Deftype statements must precede declarations*")]
    [InlineData("Const K = 1\nDefInt X\nSub S()\nEnd Sub\nS", "*Deftype statements must precede declarations*")]
    [InlineData("Private Type T\n    a As Long\nEnd Type\nDefInt X\nSub S()\nEnd Sub\nS", "*Deftype statements must precede declarations*")]
    [InlineData("Enum E\n    one\nEnd Enum\nDefInt X\nSub S()\nEnd Sub\nS", "*Deftype statements must precede declarations*")]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long\nDefInt X\nSub S()\nEnd Sub\nS", "*Deftype statements must precede declarations*")]
    [InlineData("Sub S()\n    DefInt A-Z\nEnd Sub\nS", "*Invalid inside procedure*")]
    [InlineData("DefInt AB\nSub S()\nEnd Sub\nS", "*Expected: letter*")]
    [InlineData("DefDec E\nSub S()\nEnd Sub\nS", "*Expected: line number or label or statement or end of statement*")]
    public async Task What_VB6_refuses_to_compile_is_refused(string program, string message)
    {
        var run = () => Run(program + "\n");

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage(message);
    }
}
