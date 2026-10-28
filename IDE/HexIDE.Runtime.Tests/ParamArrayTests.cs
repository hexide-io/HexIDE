using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A ParamArray parameter (#2462), which stopped the program at every call with "ParamArray parameters are not yet supported".
/// Each transcript is what <c>vb6.exe</c> logged for the same program, native and p-code agreeing (the oracle's "A ParamArray
/// parameter"). The part people do not expect is that every element is its argument ByRef: a store through `a(i)` reaches the
/// caller's variable, converted to its type.
/// </summary>
public class ParamArrayTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    private const string Procedures = """
        Private Function Desc(a As Variant) As String
            Dim s As String, i As Long
            s = "LBound " & LBound(a) & " UBound " & UBound(a) & " TypeName " & TypeName(a) & " VarType " & VarType(a)
            For i = LBound(a) To UBound(a)
                If IsMissing(a(i)) Then
                    s = s & " | " & i & ": Missing"
                ElseIf IsNull(a(i)) Then
                    s = s & " | " & i & ": Null"
                ElseIf IsObject(a(i)) Then
                    s = s & " | " & i & ": object " & TypeName(a(i))
                ElseIf IsArray(a(i)) Then
                    s = s & " | " & i & ": array " & TypeName(a(i))
                Else
                    s = s & " | " & i & ": " & TypeName(a(i)) & " [" & CStr(a(i)) & "]"
                End If
            Next
            Desc = s
        End Function
        Private Sub Show(ByVal label As String, ParamArray a() As Variant)
            Dim v As Variant
            v = a
            Debug.Print label & ": IsMissing(a) " & IsMissing(a) & ", " & Desc(v)
        End Sub
        Private Sub Bump(ParamArray a() As Variant)
            Dim i As Long
            For i = LBound(a) To UBound(a)
                a(i) = a(i) + 1
            Next
        End Sub
        Private Sub Put5(ParamArray a() As Variant)
            a(0) = 5.7
        End Sub
        Private Sub PutStr(ParamArray a() As Variant)
            a(0) = "abc"
        End Sub
        Private Function Count(x As Long, ParamArray a() As Variant) As Long
            Count = x * 100 + UBound(a) - LBound(a) + 1
        End Function
        Private Sub PassOn(ParamArray b() As Variant)
            Show "PassOn b", b
        End Sub
        Private Sub PassOnElems(ParamArray b() As Variant)
            Show "PassOnElems b(0), b(1)", b(0), b(1)
        End Sub
        Private Sub ForEachIt(ParamArray a() As Variant)
            Dim v As Variant, s As String
            For Each v In a
                s = s & "[" & CStr(v) & "]"
            Next
            Debug.Print "For Each: " & s
        End Sub
        Private Sub Takes(ByVal v As Variant)
            Debug.Print "Takes(a): IsArray " & IsArray(v) & ", TypeName " & TypeName(v) & ", " & LBound(v) & ".." & UBound(v)
        End Sub
        Private Sub ToVariant(ParamArray a() As Variant)
            Dim copy As Variant
            Takes a
            copy = a
            copy(0) = 99
            Debug.Print "copy = a, copy(0) = 99: a(0) " & a(0)
        End Sub
        Private Sub IsM(ParamArray a() As Variant)
            Debug.Print "IsMissing(a) " & IsMissing(a) & ", UBound " & UBound(a)
        End Sub
        Private Sub Reassign(ParamArray a() As Variant)
            On Error Resume Next
            a = Array(7)
            Debug.Print "a = Array(7): err " & Err.Number & ", UBound " & UBound(a) & ", a(0) " & a(0)
        End Sub

        """;

    [Fact]
    public async Task The_procedure_gets_a_Variant_array_from_0_of_its_arguments_empty_and_missing_when_there_are_none()
    {
        await Run(Procedures + """
            Dim c As New Collection
            Show "none"
            Show "one", 1
            Show "mixed", 1, "two", 3.5, Null, Empty, c
            Show "omitted middle", 1, , 3
            Debug.Print "Count(7): " & Count(7) & ", Count(7, 1, 2): " & Count(7, 1, 2)
            IsM
            IsM 1

            """);

        AssertDebugLog(Lines("""
            none: IsMissing(a) True, LBound 0 UBound -1 TypeName Variant() VarType 8204
            one: IsMissing(a) False, LBound 0 UBound 0 TypeName Variant() VarType 8204 | 0: Integer [1]
            mixed: IsMissing(a) False, LBound 0 UBound 5 TypeName Variant() VarType 8204 | 0: Integer [1] | 1: String [two] | 2: Double [3.5] | 3: Null | 4: Empty [] | 5: object Collection
            omitted middle: IsMissing(a) False, LBound 0 UBound 2 TypeName Variant() VarType 8204 | 0: Integer [1] | 1: Missing | 2: Integer [3]
            Count(7): 700, Count(7, 1, 2): 702
            IsMissing(a) True, UBound -1
            IsMissing(a) False, UBound 0
            """));
    }

    [Fact]
    public async Task Option_Base_1_leaves_it_from_0()
    {
        await Run("""
            Option Base 1
            Private Sub Shw(ParamArray a() As Variant)
                Debug.Print "Option Base 1: LBound " & LBound(a) & " UBound " & UBound(a)
            End Sub
            Shw 1, 2

            """);

        AssertDebugLog(Lines("Option Base 1: LBound 0 UBound 1"));
    }

    [Fact]
    public async Task Each_element_is_its_argument_ByRef_and_a_store_converts_to_the_arguments_type()
    {
        await Run(Procedures + """
            Dim i As Integer, n As Long, arr(1 To 3) As Long, d As Double, v As Variant
            i = 1: n = 10
            Bump i, n
            Debug.Print "Bump i, n (Integer 1, Long 10): i " & i & ", n " & n
            Bump (i), n
            Debug.Print "Bump (i), n: i " & i & ", n " & n
            arr(2) = 20
            Bump arr(2)
            Debug.Print "Bump arr(2) (20): arr(2) " & arr(2)
            On Error Resume Next
            i = 1
            Put5 i
            Debug.Print "Put5 i (a(0) = 5.7 into an Integer): err " & Err.Number & ", i " & i & " " & TypeName(i)
            i = 1
            PutStr i
            Debug.Print "PutStr i: err " & Err.Number & ", i " & i
            Err.Clear
            d = 1
            PutStr d
            Debug.Print "PutStr d: err " & Err.Number & ", d " & d
            Err.Clear
            v = 1
            PutStr v
            Debug.Print "PutStr v: err " & Err.Number & ", v " & v & " " & TypeName(v)

            """);

        AssertDebugLog(Lines("""
            Bump i, n (Integer 1, Long 10): i 2, n 11
            Bump (i), n: i 2, n 12
            Bump arr(2) (20): arr(2) 21
            Put5 i (a(0) = 5.7 into an Integer): err 0, i 6 Integer
            PutStr i: err 13, i 1
            PutStr d: err 13, d 1
            PutStr v: err 0, v abc String
            """));
    }

    [Fact]
    public async Task It_can_be_walked_copied_reassigned_and_passed_on()
    {
        await Run(Procedures + """
            PassOn 1, 2
            PassOnElems 1, 2
            ForEachIt 1, "b", 3
            ToVariant 1, 2
            Reassign 1, 2, 3

            """);

        AssertDebugLog(Lines("""
            PassOn b: IsMissing(a) False, LBound 0 UBound 0 TypeName Variant() VarType 8204 | 0: array Variant()
            PassOnElems b(0), b(1): IsMissing(a) False, LBound 0 UBound 1 TypeName Variant() VarType 8204 | 0: Integer [1] | 1: Integer [2]
            For Each: [1][b][3]
            Takes(a): IsArray True, TypeName Variant(), 0..1
            copy = a, copy(0) = 99: a(0) 1
            a = Array(7): err 0, UBound 0, a(0) 7
            """));
    }

    // VB6 refuses each of these at compile time, and the walk as the statement runs.
    [Theory]
    [InlineData("ReDim a(5)")]
    [InlineData("Erase a")]
    [InlineData("TakesArray a")]
    [InlineData("TakesVariant a")]
    public async Task What_VB6_refuses_as_Invalid_ParamArray_use_is_refused(string statement)
    {
        var run = () => Run($$"""
            Private Sub TakesArray(x() As Variant)
            End Sub
            Private Sub TakesVariant(v As Variant)
            End Sub
            Private Sub R(ParamArray a() As Variant)
                {{statement}}
            End Sub
            R 1

            """);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage("*Invalid ParamArray use*");
    }
}
