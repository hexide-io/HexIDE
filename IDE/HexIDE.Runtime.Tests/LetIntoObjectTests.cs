using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Let into an object variable, <c>k = 5</c> with <c>k As C1</c> holding an instance (#1493), assigns the default member
/// of the object it holds. A class with none raises 438, and the variable keeps the object; Nothing raises 91. HexIDE
/// released the object there and stored the value. A Variant takes the value instead, and drops the object. Each
/// transcript is what <c>vb6.exe</c> logged for the same program under en-GB, native and p-code agreeing (the oracle's
/// "A Let into an object variable").
/// </summary>
public class LetIntoObjectTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Each statement's outcome: "ok", or the error it raised.
    private const string Helper = """
        Private Sub R(ByVal label As String)
            If Err.Number <> 0 Then Debug.Print label & " -> err " & Err.Number Else Debug.Print label & " -> ok"
            Err.Clear
        End Sub

        """;

    // Says when it is made and when it ends.
    private const string C1 = """
        Public Name As String
        Private Sub Class_Initialize()
            Debug.Print "  init"
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "  term " & Name
        End Sub

        """;

    // An object field, a Variant field, and a Let inside the class.
    private const string H = """
        Public o As C1
        Public v As Variant
        Private mo As C1
        Public Sub LetInside()
            Set mo = New C1: mo.Name = "mo"
            On Error Resume Next
            mo = 5
            Debug.Print "  mo = 5 inside the class -> err " & Err.Number
            Debug.Print "  mo Is Nothing " & (mo Is Nothing)
        End Sub
        Private Sub Class_Terminate()
            Debug.Print "  H term"
        End Sub

        """;

    // A default member, which HexIDE does not implement for a class yet (#174).
    private const string D = """
        Private m As Variant
        Public Property Get Value() As Variant
        Attribute Value.VB_UserMemId = 0
            Value = m
        End Property
        Public Property Let Value(ByVal x As Variant)
            m = x
        End Property

        """;

    [Fact]
    public async Task A_Let_into_an_object_variable_is_438_and_keeps_the_object()
    {
        await RunClasses(Helper + """"
            Private Sub LetIt(x As C1)
                On Error Resume Next
                x = 6
                R "  x = 6, a ByRef x As C1"
            End Sub
            Private Function F() As C1
                On Error Resume Next
                Set F = New C1: F.Name = "F"
                F = 7
                R "  F = 7 in Function F() As C1"
            End Function

            On Error Resume Next
            Dim k As C1, n As C1, o As Object, o2 As Object, h As H, got As C1, an As New C1
            Debug.Print "class variable:"
            Set k = New C1: k.Name = "k"
            k = 5
            R "  k = 5"
            Debug.Print "  k Is Nothing " & (k Is Nothing) & ", k.Name " & k.Name
            k = "x"
            R "  k = ""x"""
            Debug.Print "Nothing:"
            n = 5
            R "  n = 5, n Nothing"
            Debug.Print "  n Is Nothing " & (n Is Nothing)
            Debug.Print "As Object:"
            Set o = New C1: o.Name = "o"
            o = 5
            R "  o = 5"
            Debug.Print "  o Is Nothing " & (o Is Nothing)
            o2 = 5
            R "  o2 = 5, o2 Nothing"
            Debug.Print "As New, untouched:"
            an = 5
            R "  an = 5"
            an.Name = "an"
            Debug.Print "ByRef parameter:"
            LetIt k
            Debug.Print "  k.Name after " & k.Name
            Debug.Print "Function return:"
            Set got = F()
            Debug.Print "  got.Name " & got.Name
            Debug.Print "a field:"
            Set h = New H
            Set h.o = New C1: h.o.Name = "h.o"
            h.o = 8
            R "  h.o = 8"
            Debug.Print "  h.o Is Nothing " & (h.o Is Nothing)
            With h
                .o = 9
                R "  .o = 9 in With h"
            End With
            h.LetInside
            Debug.Print "releasing:"
            Set k = Nothing
            Set o = Nothing
            Set got = Nothing
            Set h = Nothing
            Set an = Nothing
            Debug.Print "end"

            """", ("C1", C1), ("H", H));

        AssertDebugLog(Lines("""
            class variable:
              init
              k = 5 -> err 438
              k Is Nothing False, k.Name k
              k = "x" -> err 438
            Nothing:
              n = 5, n Nothing -> err 91
              n Is Nothing True
            As Object:
              init
              o = 5 -> err 438
              o Is Nothing False
              o2 = 5, o2 Nothing -> err 91
            As New, untouched:
              init
              an = 5 -> err 438
            ByRef parameter:
              x = 6, a ByRef x As C1 -> err 438
              k.Name after k
            Function return:
              init
              F = 7 in Function F() As C1 -> err 438
              got.Name F
            a field:
              init
              h.o = 8 -> err 438
              h.o Is Nothing False
              .o = 9 in With h -> err 438
              init
              mo = 5 inside the class -> err 438
              mo Is Nothing False
            releasing:
              term k
              term o
              term F
              H term
              term h.o
              term mo
              term an
            end
            """));
    }

    [Fact]
    public async Task A_Variant_takes_the_value_and_drops_the_object()
    {
        await RunClasses(Helper + """
            Private Sub ValUntyped(ByVal x)
                On Error Resume Next
                x = 4
                R "  x = 4, ByVal x untyped"
                Debug.Print "  TypeName(x) " & TypeName(x)
            End Sub
            Private Sub RefVariant(x As Variant)
                On Error Resume Next
                x = 5
                R "  x = 5, ByRef x As Variant"
            End Sub
            Private Function FVariant() As Variant
                On Error Resume Next
                Dim c As C1
                Set c = New C1: c.Name = "FVariant"
                Set FVariant = c
                Set c = Nothing
                FVariant = 7
                R "  FVariant = 7, As Variant"
                Debug.Print "  TypeName(FVariant) " & TypeName(FVariant)
            End Function
            Private Function FUntyped()
                On Error Resume Next
                Dim c As C1
                Set c = New C1: c.Name = "FUntyped"
                Set FUntyped = c
                Set c = Nothing
                FUntyped = 8
                R "  FUntyped = 8, untyped"
                Debug.Print "  TypeName(FUntyped) " & TypeName(FUntyped)
            End Function

            On Error Resume Next
            Dim v As Variant, u, a As C1, h As H, got As Variant
            Debug.Print "variables:"
            Set v = New C1: v.Name = "v"
            v = 5
            R "  v = 5"
            Debug.Print "  TypeName(v) " & TypeName(v) & ", v " & v
            Set u = New C1: u.Name = "u"
            u = 5
            R "  u = 5, untyped"
            Debug.Print "  TypeName(u) " & TypeName(u)
            Debug.Print "parameters:"
            Set a = New C1: a.Name = "a"
            ValUntyped a
            Debug.Print "  a Is Nothing " & (a Is Nothing)
            Set v = New C1: v.Name = "v2"
            RefVariant v
            Debug.Print "  TypeName(v) " & TypeName(v)
            Debug.Print "return names:"
            got = FVariant()
            Debug.Print "  TypeName(got) " & TypeName(got)
            got = FUntyped()
            Debug.Print "  TypeName(got) " & TypeName(got)
            Debug.Print "a Variant field:"
            Set h = New H
            Set h.v = New C1: h.v.Name = "h.v"
            h.v = 10
            R "  h.v = 10"
            Debug.Print "  TypeName(h.v) " & TypeName(h.v)
            Set h = Nothing
            Set a = Nothing
            Debug.Print "end"

            """, ("C1", C1), ("H", H));

        AssertDebugLog(Lines("""
            variables:
              init
              term v
              v = 5 -> ok
              TypeName(v) Integer, v 5
              init
              term u
              u = 5, untyped -> ok
              TypeName(u) Integer
            parameters:
              init
              x = 4, ByVal x untyped -> ok
              TypeName(x) Integer
              a Is Nothing False
              init
              term v2
              x = 5, ByRef x As Variant -> ok
              TypeName(v) Integer
            return names:
              init
              term FVariant
              FVariant = 7, As Variant -> ok
              TypeName(FVariant) Integer
              TypeName(got) Integer
              init
              term FUntyped
              FUntyped = 8, untyped -> ok
              TypeName(FUntyped) Integer
              TypeName(got) Integer
            a Variant field:
              init
              term h.v
              h.v = 10 -> ok
              TypeName(h.v) Integer
              H term
              term a
            end
            """));
    }

    [Fact]
    public async Task As_Object_parameters_and_returns_are_object_variables()
    {
        await RunClasses(Helper + """
            Private Sub RefObject(x As Object)
                On Error Resume Next
                x = 1
                R "  x = 1, ByRef x As Object"
            End Sub
            Private Sub ValObject(ByVal x As Object)
                On Error Resume Next
                x = 2
                R "  x = 2, ByVal x As Object"
                Debug.Print "  x Is Nothing " & (x Is Nothing)
            End Sub
            Private Sub ValClass(ByVal x As C1)
                On Error Resume Next
                x = 3
                R "  x = 3, ByVal x As C1"
            End Sub
            Private Function FObject() As Object
                On Error Resume Next
                Dim c As C1
                Set c = New C1: c.Name = "FObject"
                Set FObject = c
                Set c = Nothing
                FObject = 6
                R "  FObject = 6, As Object"
                Debug.Print "  TypeName(FObject) " & TypeName(FObject)
            End Function

            On Error Resume Next
            Dim a As C1, b As Object, got As Object
            Set a = New C1: a.Name = "a"
            Set b = New C1: b.Name = "b"
            Debug.Print "parameters:"
            RefObject b
            Debug.Print "  b Is Nothing " & (b Is Nothing)
            ValObject a
            ValClass a
            Debug.Print "  a Is Nothing " & (a Is Nothing)
            Debug.Print "return name:"
            Set got = FObject()
            Debug.Print "  TypeName(got) " & TypeName(got)
            Set got = Nothing
            Set a = Nothing
            Set b = Nothing
            Debug.Print "end"

            """, ("C1", C1));

        AssertDebugLog(Lines("""
              init
              init
            parameters:
              x = 1, ByRef x As Object -> err 438
              b Is Nothing False
              x = 2, ByVal x As Object -> err 438
              x Is Nothing False
              x = 3, ByVal x As C1 -> err 438
              a Is Nothing False
            return name:
              init
              FObject = 6, As Object -> err 438
              TypeName(FObject) C1
              TypeName(got) C1
              term FObject
              term a
              term b
            end
            """));
    }

    [Fact]
    public async Task A_class_with_a_default_member_is_refused_by_name()
    {
        // vb6.exe assigns the default member: `d = 11` runs D's Property Let Value with 11 (measured).
        var run = () => RunClasses("Dim d As D\nSet d = New D\nd = 11\n", ("D", D));

        (await run.Should().ThrowAsync<NotImplementedException>()).WithMessage("*default member of the D it holds*#174*");
    }
}
