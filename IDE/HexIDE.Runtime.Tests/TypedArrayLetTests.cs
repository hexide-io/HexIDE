using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// A Let into an array variable declared with an element type takes only an array of exactly that type, or for a Byte()
/// a String, which becomes its bytes (#998, #1588). Anything else is 13, and the array is left as it was, or a compile
/// error where the statement shows the type. Measured on vb6.exe, native and p-code alike, with the probe projects in
/// docs/vb6-fidelity-oracle/a-let-into-an-array-variable.md, "A Let into an array variable": each program here is one of them, and each expected line
/// is what vb6.exe logged. HexIDE stored whatever it was given, so an array became Empty, a String or another type.
/// </summary>
public class TypedArrayLetTests : BaseVBTestFixture
{
    private static List<Vb6Value> Lines(string transcript) =>
        [.. transcript.Split('\n').Select(l => l.TrimEnd('\r')).Where(l => l.Length > 0).Select(l => new Vb6Value(l))];

    // Fourteen values, each through a Variant, into a dynamic array of each of six element types, unallocated and holding
    // two elements; then the same statements written directly, and through a ByRef array parameter.
    [Fact]
    public async Task A_dynamic_array_takes_only_an_array_of_its_own_type_and_a_Byte_array_a_String()
    {
        await Run(""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function MakeV(ByVal k As Integer) As Variant
                Dim ln() As Long, it() As Integer, st() As String, bt() As Byte, db() As Double
                Select Case k
                Case 0: MakeV = Empty
                Case 1: MakeV = Null
                Case 2: MakeV = 5
                Case 3: MakeV = 2.5
                Case 4: MakeV = "ab"
                Case 5: MakeV = ""
                Case 6: ReDim ln(1 To 2): ln(1) = 1: ln(2) = 2: MakeV = ln
                Case 7: ReDim it(0 To 1): it(0) = 3: it(1) = 4: MakeV = it
                Case 8: ReDim st(0 To 1): st(0) = "p": st(1) = "q": MakeV = st
                Case 9: ReDim bt(0 To 1): bt(0) = 65: bt(1) = 0: MakeV = bt
                Case 10: MakeV = Array(1, "x")
                Case 11: ReDim db(0 To 1): db(0) = 1.5: db(1) = 2.5: MakeV = db
                Case 12: MakeV = Split("p q")
                Case 13: Set MakeV = Nothing
                End Select
            End Function

            Public Function SourceName(ByVal k As Integer) As String
                Select Case k
                Case 0: SourceName = "Empty"
                Case 1: SourceName = "Null"
                Case 2: SourceName = "Integer 5"
                Case 3: SourceName = "Double 2.5"
                Case 4: SourceName = "String ""ab"""
                Case 5: SourceName = "String """""
                Case 6: SourceName = "Long()"
                Case 7: SourceName = "Integer()"
                Case 8: SourceName = "String()"
                Case 9: SourceName = "Byte()"
                Case 10: SourceName = "Variant() Array(1, ""x"")"
                Case 11: SourceName = "Double()"
                Case 12: SourceName = "Split(""p q"")"
                Case 13: SourceName = "Nothing"
                End Select
            End Function

            Public Function DescString(a() As String) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescString = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescString = s
            End Function

            Public Sub RunString()
                Dim a() As String, v As Variant, k As Integer, st As Integer, e As Long
                For k = 0 To 13
                    For st = 0 To 1
                        Erase a
                        If st = 1 Then ReDim a(1 To 2): a(1) = "s1": a(2) = "s2"
                        If k = 13 Then Set v = MakeV(k) Else v = MakeV(k)
                        On Error Resume Next
                        Err.Clear
                        a = v
                        e = Err.Number
                        On Error GoTo 0
                        L "String() <- " & SourceName(k) & IIf(st = 1, " (allocated)", "") & ": err " & e & ", " & DescString(a)
                    Next
                Next
            End Sub

            Public Function DescLong(a() As Long) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescLong = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescLong = s
            End Function

            Public Sub RunLong()
                Dim a() As Long, v As Variant, k As Integer, st As Integer, e As Long
                For k = 0 To 13
                    For st = 0 To 1
                        Erase a
                        If st = 1 Then ReDim a(1 To 2): a(1) = 7: a(2) = 8
                        If k = 13 Then Set v = MakeV(k) Else v = MakeV(k)
                        On Error Resume Next
                        Err.Clear
                        a = v
                        e = Err.Number
                        On Error GoTo 0
                        L "Long() <- " & SourceName(k) & IIf(st = 1, " (allocated)", "") & ": err " & e & ", " & DescLong(a)
                    Next
                Next
            End Sub

            Public Function DescInteger(a() As Integer) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescInteger = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescInteger = s
            End Function

            Public Sub RunInteger()
                Dim a() As Integer, v As Variant, k As Integer, st As Integer, e As Long
                For k = 0 To 13
                    For st = 0 To 1
                        Erase a
                        If st = 1 Then ReDim a(1 To 2): a(1) = 7: a(2) = 8
                        If k = 13 Then Set v = MakeV(k) Else v = MakeV(k)
                        On Error Resume Next
                        Err.Clear
                        a = v
                        e = Err.Number
                        On Error GoTo 0
                        L "Integer() <- " & SourceName(k) & IIf(st = 1, " (allocated)", "") & ": err " & e & ", " & DescInteger(a)
                    Next
                Next
            End Sub

            Public Function DescDouble(a() As Double) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescDouble = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescDouble = s
            End Function

            Public Sub RunDouble()
                Dim a() As Double, v As Variant, k As Integer, st As Integer, e As Long
                For k = 0 To 13
                    For st = 0 To 1
                        Erase a
                        If st = 1 Then ReDim a(1 To 2): a(1) = 7.5: a(2) = 8.5
                        If k = 13 Then Set v = MakeV(k) Else v = MakeV(k)
                        On Error Resume Next
                        Err.Clear
                        a = v
                        e = Err.Number
                        On Error GoTo 0
                        L "Double() <- " & SourceName(k) & IIf(st = 1, " (allocated)", "") & ": err " & e & ", " & DescDouble(a)
                    Next
                Next
            End Sub

            Public Function DescVariant(a() As Variant) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescVariant = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescVariant = s
            End Function

            Public Sub RunVariant()
                Dim a() As Variant, v As Variant, k As Integer, st As Integer, e As Long
                For k = 0 To 13
                    For st = 0 To 1
                        Erase a
                        If st = 1 Then ReDim a(1 To 2): a(1) = 7: a(2) = "s"
                        If k = 13 Then Set v = MakeV(k) Else v = MakeV(k)
                        On Error Resume Next
                        Err.Clear
                        a = v
                        e = Err.Number
                        On Error GoTo 0
                        L "Variant() <- " & SourceName(k) & IIf(st = 1, " (allocated)", "") & ": err " & e & ", " & DescVariant(a)
                    Next
                Next
            End Sub

            Public Function DescByte(a() As Byte) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescByte = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescByte = s
            End Function

            Public Sub RunByte()
                Dim a() As Byte, v As Variant, k As Integer, st As Integer, e As Long
                For k = 0 To 13
                    For st = 0 To 1
                        Erase a
                        If st = 1 Then ReDim a(1 To 2): a(1) = 7: a(2) = 8
                        If k = 13 Then Set v = MakeV(k) Else v = MakeV(k)
                        On Error Resume Next
                        Err.Clear
                        a = v
                        e = Err.Number
                        On Error GoTo 0
                        L "Byte() <- " & SourceName(k) & IIf(st = 1, " (allocated)", "") & ": err " & e & ", " & DescByte(a)
                    Next
                Next
            End Sub

            Public Sub Direct()
                Dim t() As String, b() As Byte, va() As Variant, ln() As Long, s As String, e As Long
                On Error Resume Next
                Err.Clear: t = Split("p q"): L "direct t = Split: err " & Err.Number & ", " & DescString(t)
                Err.Clear: t = Array("a"): L "direct t = Array(""a""): err " & Err.Number & ", " & DescString(t)
                Err.Clear: b = "ab": L "direct b = ""ab"": err " & Err.Number & ", " & DescByte(b)
                s = "xy"
                Err.Clear: b = s: L "direct b = s: err " & Err.Number & ", " & DescByte(b)
                Err.Clear: b = "": L "direct b = """": err " & Err.Number & ", " & DescByte(b)
                Err.Clear: va = Array(1, 2): L "direct va = Array(1, 2): err " & Err.Number & ", " & DescVariant(va)
                Err.Clear: ln = Array(1, 2): L "direct ln = Array(1, 2): err " & Err.Number & ", " & DescLong(ln)
                Err.Clear: t = Empty: L "direct t = Empty: err " & Err.Number & ", " & DescString(t)
                Err.Clear: t = Null: L "direct t = Null: err " & Err.Number & ", " & DescString(t)
                Err.Clear: b = Chr$(0) & ChrW$(&H100): L "direct b = Chr(0) & ChrW(256): err " & Err.Number & ", " & DescByte(b)
            End Sub

            Public Sub ByRefString(a() As String, v As Variant)
                a = v
            End Sub

            Public Sub ByRefByte(a() As Byte, v As Variant)
                a = v
            End Sub

            Public Sub ByRefs()
                Dim t() As String, b() As Byte, k As Integer, v As Variant
                On Error Resume Next
                For k = 0 To 13
                    Erase t: Erase b
                    If k = 13 Then Set v = MakeV(k) Else v = MakeV(k)
                    Err.Clear: ByRefString t, v: L "byref String() <- " & SourceName(k) & ": err " & Err.Number & ", " & DescString(t)
                    Err.Clear: ByRefByte b, v: L "byref Byte() <- " & SourceName(k) & ": err " & Err.Number & ", " & DescByte(b)
                Next
            End Sub

            Sub Main()
                RunString
                RunLong
                RunInteger
                RunDouble
                RunVariant
                RunByte
                Direct
                ByRefs
            End Sub

            Main
            """""");

        AssertDebugLog(Lines("""
            String() <- Empty: err 13, unallocated
            String() <- Empty (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Null: err 13, unallocated
            String() <- Null (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Integer 5: err 13, unallocated
            String() <- Integer 5 (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Double 2.5: err 13, unallocated
            String() <- Double 2.5 (allocated): err 13, String() 1 To 2: s1 s2
            String() <- String "ab": err 13, unallocated
            String() <- String "ab" (allocated): err 13, String() 1 To 2: s1 s2
            String() <- String "": err 13, unallocated
            String() <- String "" (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Long(): err 13, unallocated
            String() <- Long() (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Integer(): err 13, unallocated
            String() <- Integer() (allocated): err 13, String() 1 To 2: s1 s2
            String() <- String(): err 0, String() 0 To 1: p q
            String() <- String() (allocated): err 0, String() 0 To 1: p q
            String() <- Byte(): err 13, unallocated
            String() <- Byte() (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Variant() Array(1, "x"): err 13, unallocated
            String() <- Variant() Array(1, "x") (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Double(): err 13, unallocated
            String() <- Double() (allocated): err 13, String() 1 To 2: s1 s2
            String() <- Split("p q"): err 0, String() 0 To 1: p q
            String() <- Split("p q") (allocated): err 0, String() 0 To 1: p q
            String() <- Nothing: err 13, unallocated
            String() <- Nothing (allocated): err 13, String() 1 To 2: s1 s2
            Long() <- Empty: err 13, unallocated
            Long() <- Empty (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Null: err 13, unallocated
            Long() <- Null (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Integer 5: err 13, unallocated
            Long() <- Integer 5 (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Double 2.5: err 13, unallocated
            Long() <- Double 2.5 (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- String "ab": err 13, unallocated
            Long() <- String "ab" (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- String "": err 13, unallocated
            Long() <- String "" (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Long(): err 0, Long() 1 To 2: 1 2
            Long() <- Long() (allocated): err 0, Long() 1 To 2: 1 2
            Long() <- Integer(): err 13, unallocated
            Long() <- Integer() (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- String(): err 13, unallocated
            Long() <- String() (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Byte(): err 13, unallocated
            Long() <- Byte() (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Variant() Array(1, "x"): err 13, unallocated
            Long() <- Variant() Array(1, "x") (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Double(): err 13, unallocated
            Long() <- Double() (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Split("p q"): err 13, unallocated
            Long() <- Split("p q") (allocated): err 13, Long() 1 To 2: 7 8
            Long() <- Nothing: err 13, unallocated
            Long() <- Nothing (allocated): err 13, Long() 1 To 2: 7 8
            Integer() <- Empty: err 13, unallocated
            Integer() <- Empty (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Null: err 13, unallocated
            Integer() <- Null (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Integer 5: err 13, unallocated
            Integer() <- Integer 5 (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Double 2.5: err 13, unallocated
            Integer() <- Double 2.5 (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- String "ab": err 13, unallocated
            Integer() <- String "ab" (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- String "": err 13, unallocated
            Integer() <- String "" (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Long(): err 13, unallocated
            Integer() <- Long() (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Integer(): err 0, Integer() 0 To 1: 3 4
            Integer() <- Integer() (allocated): err 0, Integer() 0 To 1: 3 4
            Integer() <- String(): err 13, unallocated
            Integer() <- String() (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Byte(): err 13, unallocated
            Integer() <- Byte() (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Variant() Array(1, "x"): err 13, unallocated
            Integer() <- Variant() Array(1, "x") (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Double(): err 13, unallocated
            Integer() <- Double() (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Split("p q"): err 13, unallocated
            Integer() <- Split("p q") (allocated): err 13, Integer() 1 To 2: 7 8
            Integer() <- Nothing: err 13, unallocated
            Integer() <- Nothing (allocated): err 13, Integer() 1 To 2: 7 8
            Double() <- Empty: err 13, unallocated
            Double() <- Empty (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Null: err 13, unallocated
            Double() <- Null (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Integer 5: err 13, unallocated
            Double() <- Integer 5 (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Double 2.5: err 13, unallocated
            Double() <- Double 2.5 (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- String "ab": err 13, unallocated
            Double() <- String "ab" (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- String "": err 13, unallocated
            Double() <- String "" (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Long(): err 13, unallocated
            Double() <- Long() (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Integer(): err 13, unallocated
            Double() <- Integer() (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- String(): err 13, unallocated
            Double() <- String() (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Byte(): err 13, unallocated
            Double() <- Byte() (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Variant() Array(1, "x"): err 13, unallocated
            Double() <- Variant() Array(1, "x") (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Double(): err 0, Double() 0 To 1: 1.5 2.5
            Double() <- Double() (allocated): err 0, Double() 0 To 1: 1.5 2.5
            Double() <- Split("p q"): err 13, unallocated
            Double() <- Split("p q") (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Double() <- Nothing: err 13, unallocated
            Double() <- Nothing (allocated): err 13, Double() 1 To 2: 7.5 8.5
            Variant() <- Empty: err 13, unallocated
            Variant() <- Empty (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Null: err 13, unallocated
            Variant() <- Null (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Integer 5: err 13, unallocated
            Variant() <- Integer 5 (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Double 2.5: err 13, unallocated
            Variant() <- Double 2.5 (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- String "ab": err 13, unallocated
            Variant() <- String "ab" (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- String "": err 13, unallocated
            Variant() <- String "" (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Long(): err 13, unallocated
            Variant() <- Long() (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Integer(): err 13, unallocated
            Variant() <- Integer() (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- String(): err 13, unallocated
            Variant() <- String() (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Byte(): err 13, unallocated
            Variant() <- Byte() (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Variant() Array(1, "x"): err 0, Variant() 0 To 1: 1 x
            Variant() <- Variant() Array(1, "x") (allocated): err 0, Variant() 0 To 1: 1 x
            Variant() <- Double(): err 13, unallocated
            Variant() <- Double() (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Split("p q"): err 13, unallocated
            Variant() <- Split("p q") (allocated): err 13, Variant() 1 To 2: 7 s
            Variant() <- Nothing: err 13, unallocated
            Variant() <- Nothing (allocated): err 13, Variant() 1 To 2: 7 s
            Byte() <- Empty: err 13, unallocated
            Byte() <- Empty (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Null: err 13, unallocated
            Byte() <- Null (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Integer 5: err 13, unallocated
            Byte() <- Integer 5 (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Double 2.5: err 13, unallocated
            Byte() <- Double 2.5 (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- String "ab": err 0, Byte() 0 To 3: 97 0 98 0
            Byte() <- String "ab" (allocated): err 0, Byte() 0 To 3: 97 0 98 0
            Byte() <- String "": err 0, Byte() 0 To -1:
            Byte() <- String "" (allocated): err 0, Byte() 0 To -1:
            Byte() <- Long(): err 13, unallocated
            Byte() <- Long() (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Integer(): err 13, unallocated
            Byte() <- Integer() (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- String(): err 13, unallocated
            Byte() <- String() (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Byte(): err 0, Byte() 0 To 1: 65 0
            Byte() <- Byte() (allocated): err 0, Byte() 0 To 1: 65 0
            Byte() <- Variant() Array(1, "x"): err 13, unallocated
            Byte() <- Variant() Array(1, "x") (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Double(): err 13, unallocated
            Byte() <- Double() (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Split("p q"): err 13, unallocated
            Byte() <- Split("p q") (allocated): err 13, Byte() 1 To 2: 7 8
            Byte() <- Nothing: err 13, unallocated
            Byte() <- Nothing (allocated): err 13, Byte() 1 To 2: 7 8
            direct t = Split: err 0, String() 0 To 1: p q
            direct t = Array("a"): err 13, String() 0 To 1: p q
            direct b = "ab": err 0, Byte() 0 To 3: 97 0 98 0
            direct b = s: err 0, Byte() 0 To 3: 120 0 121 0
            direct b = "": err 0, Byte() 0 To -1:
            direct va = Array(1, 2): err 0, Variant() 0 To 1: 1 2
            direct ln = Array(1, 2): err 13, unallocated
            direct t = Empty: err 13, String() 0 To 1: p q
            direct t = Null: err 13, String() 0 To 1: p q
            direct b = Chr(0) & ChrW(256): err 0, Byte() 0 To 3: 0 0 0 1
            byref String() <- Empty: err 13, unallocated
            byref Byte() <- Empty: err 13, unallocated
            byref String() <- Null: err 13, unallocated
            byref Byte() <- Null: err 13, unallocated
            byref String() <- Integer 5: err 13, unallocated
            byref Byte() <- Integer 5: err 13, unallocated
            byref String() <- Double 2.5: err 13, unallocated
            byref Byte() <- Double 2.5: err 13, unallocated
            byref String() <- String "ab": err 13, unallocated
            byref Byte() <- String "ab": err 0, Byte() 0 To 3: 97 0 98 0
            byref String() <- String "": err 13, unallocated
            byref Byte() <- String "": err 0, Byte() 0 To -1:
            byref String() <- Long(): err 13, unallocated
            byref Byte() <- Long(): err 13, unallocated
            byref String() <- Integer(): err 13, unallocated
            byref Byte() <- Integer(): err 13, unallocated
            byref String() <- String(): err 0, String() 0 To 1: p q
            byref Byte() <- String(): err 13, unallocated
            byref String() <- Byte(): err 13, unallocated
            byref Byte() <- Byte(): err 0, Byte() 0 To 1: 65 0
            byref String() <- Variant() Array(1, "x"): err 13, unallocated
            byref Byte() <- Variant() Array(1, "x"): err 13, unallocated
            byref String() <- Double(): err 13, unallocated
            byref Byte() <- Double(): err 13, unallocated
            byref String() <- Split("p q"): err 0, String() 0 To 1: p q
            byref Byte() <- Split("p q"): err 13, unallocated
            byref String() <- Nothing: err 13, unallocated
            byref Byte() <- Nothing: err 13, unallocated
            """));
    }

    // The other scalar types into a Byte(), a String() and a Variant(), and four more element types' own arrays. The
    // probe's CVErr row is left out, since HexIDE has no CVErr. The Date rows are written in en-GB, the oracle's locale.
    [Fact]
    public async Task So_does_every_other_element_type_and_no_other_scalar_converts()
    {
        using (VbLocale.Use(CultureInfo.GetCultureInfo("en-GB")))
            await Run(""""""
                Public Sub L(ByVal s As String)
                    Debug.Print s
                End Sub

                Public Function MakeV(ByVal k As Integer) As Variant
                    Select Case k
                    Case 0: MakeV = 5&
                    Case 1: MakeV = True
                    Case 2: MakeV = #1/2/2000#
                    Case 3: MakeV = 1.5@
                    Case 4: MakeV = 1.5!
                    Case 5: MakeV = CDec(1)
                    Case 7: MakeV = CByte(7)
                    Case 8: MakeV = "12"
                    End Select
                End Function

                Public Function SourceName(ByVal k As Integer) As String
                    Select Case k
                    Case 0: SourceName = "Long 5&"
                    Case 1: SourceName = "Boolean True"
                    Case 2: SourceName = "Date #1/2/2000#"
                    Case 3: SourceName = "Currency 1.5@"
                    Case 4: SourceName = "Single 1.5!"
                    Case 5: SourceName = "Decimal CDec(1)"
                    Case 7: SourceName = "Byte CByte(7)"
                    Case 8: SourceName = "String 12"
                    End Select
                End Function

                Public Function DescByte(a() As Byte) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescByte = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        s = s & " " & CStr(a(i))
                    Next
                    DescByte = s
                End Function

                Public Function DescString(a() As String) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescString = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        s = s & " " & CStr(a(i))
                    Next
                    DescString = s
                End Function

                Public Function DescVariant(a() As Variant) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescVariant = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        If IsObject(a(i)) Then s = s & " " & TypeName(a(i)) Else s = s & " " & CStr(a(i))
                    Next
                    DescVariant = s
                End Function

                Public Function DescBoolean(a() As Boolean) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescBoolean = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        s = s & " " & CStr(a(i))
                    Next
                    DescBoolean = s
                End Function

                Public Function DescDate(a() As Date) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescDate = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        s = s & " " & CStr(a(i))
                    Next
                    DescDate = s
                End Function

                Public Function DescCurrency(a() As Currency) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescCurrency = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        s = s & " " & CStr(a(i))
                    Next
                    DescCurrency = s
                End Function

                Public Function DescSingle(a() As Single) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescSingle = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        s = s & " " & CStr(a(i))
                    Next
                    DescSingle = s
                End Function

                Public Function DescObject(a() As Object) As String
                    Dim u As Long, i As Long, s As String
                    On Error Resume Next
                    u = UBound(a)
                    If Err.Number <> 0 Then DescObject = "unallocated": Exit Function
                    On Error GoTo 0
                    s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                    For i = LBound(a) To UBound(a)
                        If IsObject(a(i)) Then s = s & " " & TypeName(a(i)) Else s = s & " " & CStr(a(i))
                    Next
                    DescObject = s
                End Function

                Public Sub Scalars()
                    Dim b() As Byte, t() As String, w() As Variant, v As Variant, k As Integer
                    On Error Resume Next
                    For k = 0 To 8
                        If k <> 6 Then   ' the probe's CVErr(5): HexIDE has no CVErr
                        v = MakeV(k)
                        Erase b: Err.Clear: b = v: L "Byte() <- " & SourceName(k) & ": err " & Err.Number & ", " & DescByte(b)
                        Erase t: Err.Clear: t = v: L "String() <- " & SourceName(k) & ": err " & Err.Number & ", " & DescString(t)
                        Erase w: Err.Clear: w = v: L "Variant() <- " & SourceName(k) & ": err " & Err.Number & ", " & DescVariant(w)
                        End If
                    Next
                End Sub

                Public Sub Arrays()
                    Dim v As Variant, w As Variant, c As New Collection
                    Dim srcBoolean() As Boolean, dstBoolean() As Boolean
                    Dim srcDate() As Date, dstDate() As Date
                    Dim srcCurrency() As Currency, dstCurrency() As Currency
                    Dim srcSingle() As Single, dstSingle() As Single
                    Dim srcObject() As Object, dstObject() As Object
                    On Error Resume Next
                    ReDim srcBoolean(0 To 1): srcBoolean(0) = True: srcBoolean(1) = False
                    v = srcBoolean
                    Err.Clear: dstBoolean = v: L "Boolean() <- Boolean() through a Variant: err " & Err.Number & ", " & DescBoolean(dstBoolean)
                    Erase dstBoolean: Err.Clear: dstBoolean = srcBoolean: L "Boolean() <- Boolean() directly: err " & Err.Number & ", " & DescBoolean(dstBoolean)
                    w = Array(1, 2)
                    Erase dstBoolean: Err.Clear: dstBoolean = w: L "Boolean() <- Variant() through a Variant: err " & Err.Number & ", " & DescBoolean(dstBoolean)
                    w = Empty
                    Erase dstBoolean: Err.Clear: dstBoolean = w: L "Boolean() <- Empty: err " & Err.Number & ", " & DescBoolean(dstBoolean)
                    ReDim srcDate(0 To 1): srcDate(0) = #1/2/2000#: srcDate(1) = #3/4/2001#
                    v = srcDate
                    Err.Clear: dstDate = v: L "Date() <- Date() through a Variant: err " & Err.Number & ", " & DescDate(dstDate)
                    Erase dstDate: Err.Clear: dstDate = srcDate: L "Date() <- Date() directly: err " & Err.Number & ", " & DescDate(dstDate)
                    w = Array(1, 2)
                    Erase dstDate: Err.Clear: dstDate = w: L "Date() <- Variant() through a Variant: err " & Err.Number & ", " & DescDate(dstDate)
                    w = Empty
                    Erase dstDate: Err.Clear: dstDate = w: L "Date() <- Empty: err " & Err.Number & ", " & DescDate(dstDate)
                    ReDim srcCurrency(0 To 1): srcCurrency(0) = 1.5@: srcCurrency(1) = 2.5@
                    v = srcCurrency
                    Err.Clear: dstCurrency = v: L "Currency() <- Currency() through a Variant: err " & Err.Number & ", " & DescCurrency(dstCurrency)
                    Erase dstCurrency: Err.Clear: dstCurrency = srcCurrency: L "Currency() <- Currency() directly: err " & Err.Number & ", " & DescCurrency(dstCurrency)
                    w = Array(1, 2)
                    Erase dstCurrency: Err.Clear: dstCurrency = w: L "Currency() <- Variant() through a Variant: err " & Err.Number & ", " & DescCurrency(dstCurrency)
                    w = Empty
                    Erase dstCurrency: Err.Clear: dstCurrency = w: L "Currency() <- Empty: err " & Err.Number & ", " & DescCurrency(dstCurrency)
                    ReDim srcSingle(0 To 1): srcSingle(0) = 1.5!: srcSingle(1) = 2.5!
                    v = srcSingle
                    Err.Clear: dstSingle = v: L "Single() <- Single() through a Variant: err " & Err.Number & ", " & DescSingle(dstSingle)
                    Erase dstSingle: Err.Clear: dstSingle = srcSingle: L "Single() <- Single() directly: err " & Err.Number & ", " & DescSingle(dstSingle)
                    w = Array(1, 2)
                    Erase dstSingle: Err.Clear: dstSingle = w: L "Single() <- Variant() through a Variant: err " & Err.Number & ", " & DescSingle(dstSingle)
                    w = Empty
                    Erase dstSingle: Err.Clear: dstSingle = w: L "Single() <- Empty: err " & Err.Number & ", " & DescSingle(dstSingle)
                    ReDim srcObject(0 To 1): Set srcObject(0) = c: Set srcObject(1) = Nothing
                    v = srcObject
                    Err.Clear: dstObject = v: L "Object() <- Object() through a Variant: err " & Err.Number & ", " & DescObject(dstObject)
                    Erase dstObject: Err.Clear: dstObject = srcObject: L "Object() <- Object() directly: err " & Err.Number & ", " & DescObject(dstObject)
                    w = Array(1, 2)
                    Erase dstObject: Err.Clear: dstObject = w: L "Object() <- Variant() through a Variant: err " & Err.Number & ", " & DescObject(dstObject)
                    w = Empty
                    Erase dstObject: Err.Clear: dstObject = w: L "Object() <- Empty: err " & Err.Number & ", " & DescObject(dstObject)
                End Sub

                Sub Main()
                    Scalars
                    Arrays
                End Sub

                Main
                """""");

        AssertDebugLog(Lines("""
            Byte() <- Long 5&: err 13, unallocated
            String() <- Long 5&: err 13, unallocated
            Variant() <- Long 5&: err 13, unallocated
            Byte() <- Boolean True: err 13, unallocated
            String() <- Boolean True: err 13, unallocated
            Variant() <- Boolean True: err 13, unallocated
            Byte() <- Date #1/2/2000#: err 13, unallocated
            String() <- Date #1/2/2000#: err 13, unallocated
            Variant() <- Date #1/2/2000#: err 13, unallocated
            Byte() <- Currency 1.5@: err 13, unallocated
            String() <- Currency 1.5@: err 13, unallocated
            Variant() <- Currency 1.5@: err 13, unallocated
            Byte() <- Single 1.5!: err 13, unallocated
            String() <- Single 1.5!: err 13, unallocated
            Variant() <- Single 1.5!: err 13, unallocated
            Byte() <- Decimal CDec(1): err 13, unallocated
            String() <- Decimal CDec(1): err 13, unallocated
            Variant() <- Decimal CDec(1): err 13, unallocated
            Byte() <- Byte CByte(7): err 13, unallocated
            String() <- Byte CByte(7): err 13, unallocated
            Variant() <- Byte CByte(7): err 13, unallocated
            Byte() <- String 12: err 0, Byte() 0 To 3: 49 0 50 0
            String() <- String 12: err 13, unallocated
            Variant() <- String 12: err 13, unallocated
            Boolean() <- Boolean() through a Variant: err 0, Boolean() 0 To 1: True False
            Boolean() <- Boolean() directly: err 0, Boolean() 0 To 1: True False
            Boolean() <- Variant() through a Variant: err 13, unallocated
            Boolean() <- Empty: err 13, unallocated
            Date() <- Date() through a Variant: err 0, Date() 0 To 1: 02/01/2000 04/03/2001
            Date() <- Date() directly: err 0, Date() 0 To 1: 02/01/2000 04/03/2001
            Date() <- Variant() through a Variant: err 13, unallocated
            Date() <- Empty: err 13, unallocated
            Currency() <- Currency() through a Variant: err 0, Currency() 0 To 1: 1.5 2.5
            Currency() <- Currency() directly: err 0, Currency() 0 To 1: 1.5 2.5
            Currency() <- Variant() through a Variant: err 13, unallocated
            Currency() <- Empty: err 13, unallocated
            Single() <- Single() through a Variant: err 0, Single() 0 To 1: 1.5 2.5
            Single() <- Single() directly: err 0, Single() 0 To 1: 1.5 2.5
            Single() <- Variant() through a Variant: err 13, unallocated
            Single() <- Empty: err 13, unallocated
            Object() <- Object() through a Variant: err 0, Object() 0 To 1: Collection Nothing
            Object() <- Object() directly: err 0, Object() 0 To 1: Collection Nothing
            Object() <- Variant() through a Variant: err 13, unallocated
            Object() <- Empty: err 13, unallocated
            """));
    }

    // A fixed array through a ByRef parameter is 13 whatever it is given, an array of its own type included, and keeps
    // its elements.
    [Fact]
    public async Task A_fixed_array_through_a_parameter_is_13_whatever_it_is_given()
    {
        await Run(""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Desc(a() As String) As String
                Dim i As Long, s As String
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " [" & a(i) & "]"
                Next
                Desc = s
            End Function

            Public Sub Store(a() As String, v As Variant)
                a = v
            End Sub

            Sub Main()
                Dim f(1) As String, v As Variant, k As Integer
                On Error Resume Next
                For k = 0 To 3
                    f(0) = "a": f(1) = "b"
                    Select Case k
                    Case 0: v = Split("p q")
                    Case 1: v = Empty
                    Case 2: v = 5
                    Case 3: v = Split("p q r")
                    End Select
                    Err.Clear
                    Store f, v
                    L "byref fixed <- case " & k & ": err " & Err.Number & ", " & Desc(f)
                Next
            End Sub

            Main
            """""");

        AssertDebugLog(Lines("""
            byref fixed <- case 0: err 13, String() 0 To 1: [a] [b]
            byref fixed <- case 1: err 13, String() 0 To 1: [a] [b]
            byref fixed <- case 2: err 13, String() 0 To 1: [a] [b]
            byref fixed <- case 3: err 13, String() 0 To 1: [a] [b]
            """));
    }

    // A Function declared to return a String() given Nothing or Empty through a Variant is 13, as an array variable given
    // either is.
    [Fact]
    public async Task A_Functions_array_return_given_Nothing_through_a_Variant_is_13()
    {
        await Run(""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function DescString(a() As String) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescString = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescString = s
            End Function

            Public Function DescByte(a() As Byte) As String
                Dim u As Long, i As Long, s As String
                On Error Resume Next
                u = UBound(a)
                If Err.Number <> 0 Then DescByte = "unallocated": Exit Function
                On Error GoTo 0
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " " & CStr(a(i))
                Next
                DescByte = s
            End Function

            Public Function Shape(x) As String
                Dim lo, hi
                On Error Resume Next
                lo = "-": hi = "-"
                lo = LBound(x): hi = UBound(x)
                Shape = TypeName(x) & " " & lo & " To " & hi
            End Function

            Public Function NothingThroughVariant() As String()
                Dim v As Variant
                Set v = Nothing
                NothingThroughVariant = v
            End Function

            Public Function EmptyThroughVariant() As String()
                Dim v As Variant
                EmptyThroughVariant = v
            End Function

            Public Function MakeV(ByVal k As Integer) As Variant
                Dim st() As String, ln() As Long
                Select Case k
                Case 0: MakeV = Empty
                Case 1: ReDim st(0 To 1): st(0) = "p": st(1) = "q": MakeV = st
                Case 2: ReDim ln(0 To 1): MakeV = ln
                Case 3: MakeV = "ab"
                Case 4: Set MakeV = Nothing
                End Select
            End Function

            Sub Main()
                Dim x As Variant
                On Error Resume Next
                Err.Clear: x = NothingThroughVariant(): L "return <- Nothing through a Variant: err " & Err.Number & ", " & Shape(x)
                x = Empty
                Err.Clear: x = EmptyThroughVariant(): L "return <- Empty through a Variant: err " & Err.Number & ", " & Shape(x)
            End Sub

            Main
            """""");

        AssertDebugLog(Lines("""
            return <- Nothing through a Variant: err 13, Empty - To -
            return <- Empty through a Variant: err 13, Empty - To -
            """));
    }

    // A String Const shows its type, and still converts, as a String literal does.
    [Fact]
    public async Task A_String_Const_into_a_Byte_array_converts()
    {
        await Run(""""""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Byte
                On Error Resume Next
                a = K
                L "ran: err " & Err.Number
            End Sub

            Main
            """""");

        AssertDebugLog([new Vb6Value("ran: err 0")]);
    }

    // What vb6.exe refuses to build, one project each.
    [Theory]
    [InlineData("ce-long-literal")]
    [InlineData("ce-byte-literal")]
    [InlineData("ce-long-string")]
    [InlineData("ce-variant-literal")]
    [InlineData("ce-variant-string")]
    [InlineData("ce-long-from-integers")]
    [InlineData("ce-variant-from-strings")]
    [InlineData("ce-byte-from-strings")]
    [InlineData("ce-byte-from-integer-var")]
    [InlineData("ce-long-nothing")]
    [InlineData("ce-byte-from-variants")]
    [InlineData("ce-string-from-string-var")]
    [InlineData("fx-named-variant-strings")]
    [InlineData("fx-named-variant-empty")]
    [InlineData("fx-named-variant-number")]
    [InlineData("ce-return-nothing")]
    public async Task A_store_vb6_exe_refuses_is_refused_with_its_message(string name)
    {
        var (message, module1) = Refused[name];

        var run = () => Run(module1);

        (await run.Should().ThrowAsync<VBCompileErrorException>()).WithMessage($"*{message}*");
    }

    private static readonly Dictionary<string, (string Message, string Module1)> Refused = new()
    {
        ["ce-long-literal"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Long
                On Error Resume Next
                a = 5
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-byte-literal"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Byte
                On Error Resume Next
                a = 5
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-long-string"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Long
                On Error Resume Next
                a = "ab"
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-variant-literal"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Variant
                On Error Resume Next
                a = 5
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-variant-string"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Variant
                On Error Resume Next
                a = "x"
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-long-from-integers"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Long, i() As Integer
                On Error Resume Next
                a = i
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-variant-from-strings"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Variant, s() As String
                On Error Resume Next
                a = s
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-byte-from-strings"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Byte, s() As String
                On Error Resume Next
                a = s
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-byte-from-integer-var"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Byte, n As Integer
                On Error Resume Next
                a = n
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-long-nothing"] = ("Invalid use of object", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Long
                On Error Resume Next
                a = Nothing
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-byte-from-variants"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As Byte, w() As Variant
                On Error Resume Next
                a = w
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["ce-string-from-string-var"] = ("Can't assign to array", """"""
            Const K = "ab"

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim a() As String, s As String
                On Error Resume Next
                a = s
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
        ["fx-named-variant-strings"] = ("Can't assign to array", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Desc(a() As String) As String
                Dim i As Long, s As String
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " [" & a(i) & "]"
                Next
                Desc = s
            End Function

            Sub Main()
                Dim f(1) As String, v As Variant
                f(0) = "a": f(1) = "b"
                v = Split("p q")
                On Error Resume Next
                f = v
                L "ran: err " & Err.Number & ", " & Desc(f)
            End Sub

            Main
            """"""),
        ["fx-named-variant-empty"] = ("Can't assign to array", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Desc(a() As String) As String
                Dim i As Long, s As String
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " [" & a(i) & "]"
                Next
                Desc = s
            End Function

            Sub Main()
                Dim f(1) As String, v As Variant
                f(0) = "a": f(1) = "b"
                v = Empty
                On Error Resume Next
                f = v
                L "ran: err " & Err.Number & ", " & Desc(f)
            End Sub

            Main
            """"""),
        ["fx-named-variant-number"] = ("Can't assign to array", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Desc(a() As String) As String
                Dim i As Long, s As String
                s = TypeName(a) & " " & LBound(a) & " To " & UBound(a) & ":"
                For i = LBound(a) To UBound(a)
                    s = s & " [" & a(i) & "]"
                Next
                Desc = s
            End Function

            Sub Main()
                Dim f(1) As String, v As Variant
                f(0) = "a": f(1) = "b"
                v = 5
                On Error Resume Next
                f = v
                L "ran: err " & Err.Number & ", " & Desc(f)
            End Sub

            Main
            """"""),
        ["ce-return-nothing"] = ("Invalid use of object", """"""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function F() As String()
                F = Nothing
            End Function

            Sub Main()
                On Error Resume Next
                Dim x
                x = F()
                L "ran: err " & Err.Number
            End Sub

            Main
            """"""),
    };
}
