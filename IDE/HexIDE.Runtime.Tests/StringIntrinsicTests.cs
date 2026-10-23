// Generated from the vb6.exe probes for #1560: each program is the probe as it ran, printing where the probe logged,
// and each expected line is what vb6.exe logged, native and p-code alike.

using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// StrComp, StrConv, Environ, Switch and Error (#1560), as vb6.exe measured them. Rows left out, with the issue each
/// waits on: strcomp-3 (#881), strconv-from-unicode (#1510), strconv-from-unicode-e (#1510), strconv-from-unicode-unmapped (#1510), strconv-from-unicode-empty (#1510), strconv-roundtrip (#1510), strconv-unicode-of-string (#1510), strconv-unicode-odd (#1510), strconv-unicode-high (#1510), strconv-from-then-len (#1510), strconv-type-from (#1510), text-strcomp-default (#1572), text-strcomp-B-a (#1572).
/// </summary>
public class StringIntrinsicTests : BaseVBTestFixture
{
    // The main probe: every argument shape of the five, the Text module's StrComp with a compare, Error() after a raise,
    // and Error(n) for every n from 0 to 65535. Environ's rows compare values rather than print them.
    [Fact]
    public async Task StrComp_StrConv_Environ_Switch_and_Error_answer_as_vb6_exe_does()
    {
        await RunModules("""""""
            Option Compare Binary
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Show(v As Variant) As String
                If IsNull(v) Then
                    Show = "Null"
                ElseIf IsEmpty(v) Then
                    Show = "Empty"
                ElseIf IsArray(v) Then
                    Dim i As Long, s As String
                    s = TypeName(v) & " (" & LBound(v) & " To " & UBound(v) & ")"
                    For i = LBound(v) To UBound(v)
                        s = s & " " & v(i)
                    Next
                    Show = s
                Else
                    Show = TypeName(v) & " " & CStr(v)
                End If
            End Function

            Public Function F(ByVal tag As String, v As Variant) As Variant
                L "  called " & tag
                F = v
            End Function

            Public Function Codes(ByVal s As String) As String
                Dim i As Long, r As String
                For i = 1 To Len(s)
                    r = r & IIf(i > 1, " ", "") & AscW(Mid$(s, i, 1))
                Next
                Codes = "[" & r & "]"
            End Function

            Sub P0()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "b")
                If Err.Number <> 0 Then
                    L "strcomp-lt: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-lt: " & Show(r)
                End If
            End Sub

            Sub P1()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("b", "a")
                If Err.Number <> 0 Then
                    L "strcomp-gt: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-gt: " & Show(r)
                End If
            End Sub

            Sub P2()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "a")
                If Err.Number <> 0 Then
                    L "strcomp-eq: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-eq: " & Show(r)
                End If
            End Sub

            Sub P3()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A")
                If Err.Number <> 0 Then
                    L "strcomp-case-default: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-case-default: " & Show(r)
                End If
            End Sub

            Sub P4()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", 0)
                If Err.Number <> 0 Then
                    L "strcomp-case-0: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-case-0: " & Show(r)
                End If
            End Sub

            Sub P5()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", 1)
                If Err.Number <> 0 Then
                    L "strcomp-case-1: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-case-1: " & Show(r)
                End If
            End Sub

            Sub P6()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", vbTextCompare)
                If Err.Number <> 0 Then
                    L "strcomp-case-vbText: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-case-vbText: " & Show(r)
                End If
            End Sub

            Sub P7()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", 2)
                If Err.Number <> 0 Then
                    L "strcomp-2: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-2: " & Show(r)
                End If
            End Sub

            Sub P8()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", 3)
                If Err.Number <> 0 Then
                    L "strcomp-3: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-3: " & Show(r)
                End If
            End Sub

            Sub P9()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", -1)
                If Err.Number <> 0 Then
                    L "strcomp-minus1: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-minus1: " & Show(r)
                End If
            End Sub

            Sub P10()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", -2)
                If Err.Number <> 0 Then
                    L "strcomp-minus2: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-minus2: " & Show(r)
                End If
            End Sub

            Sub P11()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", 1.5)
                If Err.Number <> 0 Then
                    L "strcomp-1.5: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-1.5: " & Show(r)
                End If
            End Sub

            Sub P12()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", 0.5)
                If Err.Number <> 0 Then
                    L "strcomp-0.5: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-0.5: " & Show(r)
                End If
            End Sub

            Sub P13()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", "1")
                If Err.Number <> 0 Then
                    L "strcomp-str1: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-str1: " & Show(r)
                End If
            End Sub

            Sub P14()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", Null)
                If Err.Number <> 0 Then
                    L "strcomp-cmp-null: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-cmp-null: " & Show(r)
                End If
            End Sub

            Sub P15()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", Empty)
                If Err.Number <> 0 Then
                    L "strcomp-cmp-empty: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-cmp-empty: " & Show(r)
                End If
            End Sub

            Sub P16()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Null, "a")
                If Err.Number <> 0 Then
                    L "strcomp-null-l: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-null-l: " & Show(r)
                End If
            End Sub

            Sub P17()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", Null)
                If Err.Number <> 0 Then
                    L "strcomp-null-r: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-null-r: " & Show(r)
                End If
            End Sub

            Sub P18()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Null, Null)
                If Err.Number <> 0 Then
                    L "strcomp-null-both: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-null-both: " & Show(r)
                End If
            End Sub

            Sub P19()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Empty, "")
                If Err.Number <> 0 Then
                    L "strcomp-empty: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-empty: " & Show(r)
                End If
            End Sub

            Sub P20()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(10, 9)
                If Err.Number <> 0 Then
                    L "strcomp-num: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-num: " & Show(r)
                End If
            End Sub

            Sub P21()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(5, "5")
                If Err.Number <> 0 Then
                    L "strcomp-num-str: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-num-str: " & Show(r)
                End If
            End Sub

            Sub P22()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(True, "True")
                If Err.Number <> 0 Then
                    L "strcomp-bool: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-bool: " & Show(r)
                End If
            End Sub

            Sub P23()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("ab", "abc")
                If Err.Number <> 0 Then
                    L "strcomp-prefix: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-prefix: " & Show(r)
                End If
            End Sub

            Sub P24()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("", "a")
                If Err.Number <> 0 Then
                    L "strcomp-empty-a: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-empty-a: " & Show(r)
                End If
            End Sub

            Sub P25()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("B", "a")
                If Err.Number <> 0 Then
                    L "strcomp-B-a: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-B-a: " & Show(r)
                End If
            End Sub

            Sub P26()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("B", "a", 1)
                If Err.Number <> 0 Then
                    L "strcomp-B-a-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-B-a-text: " & Show(r)
                End If
            End Sub

            Sub P27()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("_", "a", 1)
                If Err.Number <> 0 Then
                    L "strcomp-us-a-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-us-a-text: " & Show(r)
                End If
            End Sub

            Sub P28()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("_", "A", 0)
                If Err.Number <> 0 Then
                    L "strcomp-us-A-bin: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-us-A-bin: " & Show(r)
                End If
            End Sub

            Sub P29()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Chr(223), "ss", 1)
                If Err.Number <> 0 Then
                    L "strcomp-sz-ss-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-sz-ss-text: " & Show(r)
                End If
            End Sub

            Sub P30()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Chr(230), "ae", 1)
                If Err.Number <> 0 Then
                    L "strcomp-ae-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-ae-text: " & Show(r)
                End If
            End Sub

            Sub P31()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Chr(233), "e", 1)
                If Err.Number <> 0 Then
                    L "strcomp-e-acute-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-e-acute-text: " & Show(r)
                End If
            End Sub

            Sub P32()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Chr(233), "f", 1)
                If Err.Number <> 0 Then
                    L "strcomp-e-acute-f-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-e-acute-f-text: " & Show(r)
                End If
            End Sub

            Sub P33()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Chr(233), "f", 0)
                If Err.Number <> 0 Then
                    L "strcomp-e-acute-f-bin: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-e-acute-f-bin: " & Show(r)
                End If
            End Sub

            Sub P34()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("co-op", "coop", 1)
                If Err.Number <> 0 Then
                    L "strcomp-hyphen-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-hyphen-text: " & Show(r)
                End If
            End Sub

            Sub P35()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("it's", "its", 1)
                If Err.Number <> 0 Then
                    L "strcomp-apos-text: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-apos-text: " & Show(r)
                End If
            End Sub

            Sub P36()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a" & Chr(0), "a")
                If Err.Number <> 0 Then
                    L "strcomp-nul: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-nul: " & Show(r)
                End If
            End Sub

            Sub P37()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp(Array(1), "a")
                If Err.Number <> 0 Then
                    L "strcomp-array: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-array: " & Show(r)
                End If
            End Sub

            Sub P38()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "b")
                If Err.Number <> 0 Then
                    L "strcomp-type: err " & Err.Number & " " & Err.Description
                Else
                    L "strcomp-type: " & TypeName(r)
                End If
            End Sub

            Sub P39()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("hello World", vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-upper: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper: " & Show(r)
                End If
            End Sub

            Sub P40()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("hello World", vbLowerCase)
                If Err.Number <> 0 Then
                    L "strconv-lower: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-lower: " & Show(r)
                End If
            End Sub

            Sub P41()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("hello wORLD", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper: " & Show(r)
                End If
            End Sub

            Sub P42()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("1st place", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-digit: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-digit: " & Show(r)
                End If
            End Sub

            Sub P43()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("o'neil's", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-apos: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-apos: " & Show(r)
                End If
            End Sub

            Sub P44()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("jean-luc", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-hyphen: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-hyphen: " & Show(r)
                End If
            End Sub

            Sub P45()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("a.b,c;d", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-dot: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-dot: " & Show(r)
                End If
            End Sub

            Sub P46()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("a" & vbTab & "b" & vbCrLf & "c" & vbLf & "d" & vbCr & "e", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-tab: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-tab: " & Codes(r)
                End If
            End Sub

            Sub P47()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("a" & Chr(0) & "b", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-nul: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-nul: " & Codes(r)
                End If
            End Sub

            Sub P48()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("a" & Chr(12) & "b" & Chr(11) & "c", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-ff: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-ff: " & Codes(r)
                End If
            End Sub

            Sub P49()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("  two  spaces", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-spaces: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-spaces: " & Show(r)
                End If
            End Sub

            Sub P50()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(Chr(233) & "cole " & Chr(201) & "COLE", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-accent: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-accent: " & Codes(r)
                End If
            End Sub

            Sub P51()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("a" & Chr(160) & "b", vbProperCase)
                If Err.Number <> 0 Then
                    L "strconv-proper-nbsp: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-proper-nbsp: " & Codes(r)
                End If
            End Sub

            Sub P52()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(Chr(223), vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-upper-sz: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-sz: " & Codes(r)
                End If
            End Sub

            Sub P53()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(Chr(255), vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-upper-y-uml: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-y-uml: " & Codes(r)
                End If
            End Sub

            Sub P54()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(Chr(181), vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-upper-mu: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-mu: " & Codes(r)
                End If
            End Sub

            Sub P55()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(ChrW(&H131), vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-upper-dotless: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-dotless: " & Codes(r)
                End If
            End Sub

            Sub P56()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(ChrW(&H130), vbLowerCase)
                If Err.Number <> 0 Then
                    L "strconv-lower-dotted: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-lower-dotted: " & Codes(r)
                End If
            End Sub

            Sub P57()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("i", vbUpperCase, 1055)
                If Err.Number <> 0 Then
                    L "strconv-upper-tr: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-tr: " & Codes(r)
                End If
            End Sub

            Sub P58()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("I", vbLowerCase, 1055)
                If Err.Number <> 0 Then
                    L "strconv-lower-tr: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-lower-tr: " & Codes(r)
                End If
            End Sub

            Sub P59()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("i", vbUpperCase, 0)
                If Err.Number <> 0 Then
                    L "strconv-upper-lcid-0: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-lcid-0: " & Codes(r)
                End If
            End Sub

            Sub P60()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("i", vbUpperCase, 99999)
                If Err.Number <> 0 Then
                    L "strconv-upper-lcid-bad: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-lcid-bad: " & Codes(r)
                End If
            End Sub

            Sub P61()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbFromUnicode)
                If Err.Number <> 0 Then
                    L "strconv-from-unicode: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-from-unicode: " & Show(r)
                End If
            End Sub

            Sub P62()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(Chr(233) & ChrW(&H20AC), vbFromUnicode)
                If Err.Number <> 0 Then
                    L "strconv-from-unicode-e: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-from-unicode-e: " & Show(r)
                End If
            End Sub

            Sub P63()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(ChrW(&H2100) & ChrW(&HFF41), vbFromUnicode)
                If Err.Number <> 0 Then
                    L "strconv-from-unicode-unmapped: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-from-unicode-unmapped: " & Show(r)
                End If
            End Sub

            Sub P64()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("", vbFromUnicode)
                If Err.Number <> 0 Then
                    L "strconv-from-unicode-empty: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-from-unicode-empty: " & Show(r)
                End If
            End Sub

            Sub P65()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(StrConv("abc", vbFromUnicode), vbUnicode)
                If Err.Number <> 0 Then
                    L "strconv-roundtrip: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-roundtrip: " & Show(r)
                End If
            End Sub

            Sub P66()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("abc", vbUnicode)
                If Err.Number <> 0 Then
                    L "strconv-unicode-of-string: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-unicode-of-string: " & Codes(r)
                End If
            End Sub

            Sub P67()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("a" & ChrB(98), vbUnicode)
                If Err.Number <> 0 Then
                    L "strconv-unicode-odd: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-unicode-odd: " & Codes(r)
                End If
            End Sub

            Sub P68()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(ChrB(128) & ChrB(233), vbUnicode)
                If Err.Number <> 0 Then
                    L "strconv-unicode-high: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-unicode-high: " & Codes(r)
                End If
            End Sub

            Sub P69()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = LenB(StrConv("abc", vbFromUnicode))
                If Err.Number <> 0 Then
                    L "strconv-from-then-len: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-from-then-len: " & Show(r)
                End If
            End Sub

            Sub P70()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbUpperCase Or vbFromUnicode)
                If Err.Number <> 0 Then
                    L "strconv-upper-from: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-from: " & Show(r)
                End If
            End Sub

            Sub P71()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbUpperCase Or vbLowerCase)
                If Err.Number <> 0 Then
                    L "strconv-upper-lower: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-upper-lower: " & Show(r)
                End If
            End Sub

            Sub P72()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", 0)
                If Err.Number <> 0 Then
                    L "strconv-0: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-0: " & Show(r)
                End If
            End Sub

            Sub P73()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbWide)
                If Err.Number <> 0 Then
                    L "strconv-wide: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-wide: " & Codes(r)
                End If
            End Sub

            Sub P74()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbNarrow)
                If Err.Number <> 0 Then
                    L "strconv-narrow: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-narrow: " & Codes(r)
                End If
            End Sub

            Sub P75()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbKatakana)
                If Err.Number <> 0 Then
                    L "strconv-katakana: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-katakana: " & Codes(r)
                End If
            End Sub

            Sub P76()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbHiragana)
                If Err.Number <> 0 Then
                    L "strconv-hiragana: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-hiragana: " & Codes(r)
                End If
            End Sub

            Sub P77()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", 256)
                If Err.Number <> 0 Then
                    L "strconv-256: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-256: " & Show(r)
                End If
            End Sub

            Sub P78()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", 512)
                If Err.Number <> 0 Then
                    L "strconv-512: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-512: " & Show(r)
                End If
            End Sub

            Sub P79()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(Null, vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-null: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-null: " & Show(r)
                End If
            End Sub

            Sub P80()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", Null)
                If Err.Number <> 0 Then
                    L "strconv-null-conv: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-null-conv: " & Show(r)
                End If
            End Sub

            Sub P81()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(5.5, vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-num: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-num: " & Show(r)
                End If
            End Sub

            Sub P82()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(True, vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-bool: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-bool: " & Show(r)
                End If
            End Sub

            Sub P83()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv(Empty, vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-empty: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-empty: " & Show(r)
                End If
            End Sub

            Sub P84()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbUpperCase)
                If Err.Number <> 0 Then
                    L "strconv-type: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-type: " & TypeName(r)
                End If
            End Sub

            Sub P85()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", vbFromUnicode)
                If Err.Number <> 0 Then
                    L "strconv-type-from: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-type-from: " & TypeName(r)
                End If
            End Sub

            Sub P86()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", 1.5)
                If Err.Number <> 0 Then
                    L "strconv-conv-1.5: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-conv-1.5: " & Show(r)
                End If
            End Sub

            Sub P87()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrConv("ab", "1")
                If Err.Number <> 0 Then
                    L "strconv-conv-str: err " & Err.Number & " " & Err.Description
                Else
                    L "strconv-conv-str: " & Show(r)
                End If
            End Sub

            Sub P88()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Len(Environ("PATH")) > 0
                If Err.Number <> 0 Then
                    L "environ-path-len: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-path-len: " & Show(r)
                End If
            End Sub

            Sub P89()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("path") = Environ("PATH")
                If Err.Number <> 0 Then
                    L "environ-path-case: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-path-case: " & Show(r)
                End If
            End Sub

            Sub P90()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("HEXIDE_NO_SUCH_VARIABLE")
                If Err.Number <> 0 Then
                    L "environ-missing: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-missing: " & Show(r)
                End If
            End Sub

            Sub P91()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("")
                If Err.Number <> 0 Then
                    L "environ-empty: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-empty: " & Show(r)
                End If
            End Sub

            Sub P92()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("PATH")
                If Err.Number <> 0 Then
                    L "environ-type: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-type: " & TypeName(r)
                End If
            End Sub

            Sub P93()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ$("PATH")
                If Err.Number <> 0 Then
                    L "environ-dollar-type: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-dollar-type: " & TypeName(r)
                End If
            End Sub

            Sub P94()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = InStr(Environ(1), "=") > 1
                If Err.Number <> 0 Then
                    L "environ-1-has-eq: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-1-has-eq: " & Show(r)
                End If
            End Sub

            Sub P95()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(1)
                If Err.Number <> 0 Then
                    L "environ-1-type: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-1-type: " & TypeName(r)
                End If
            End Sub

            Sub P96()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(0)
                If Err.Number <> 0 Then
                    L "environ-0: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-0: " & Show(r)
                End If
            End Sub

            Sub P97()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(-1)
                If Err.Number <> 0 Then
                    L "environ-minus1: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-minus1: " & Show(r)
                End If
            End Sub

            Sub P98()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Len(Environ(256))
                If Err.Number <> 0 Then
                    L "environ-256: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-256: " & Show(r)
                End If
            End Sub

            Sub P99()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(100000)
                If Err.Number <> 0 Then
                    L "environ-huge: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-huge: " & Show(r)
                End If
            End Sub

            Sub P100()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("1")
                If Err.Number <> 0 Then
                    L "environ-str-1: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-str-1: " & Show(r)
                End If
            End Sub

            Sub P101()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(1.5) = Environ(2)
                If Err.Number <> 0 Then
                    L "environ-1.5-is-2: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-1.5-is-2: " & Show(r)
                End If
            End Sub

            Sub P102()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(2.5) = Environ(2)
                If Err.Number <> 0 Then
                    L "environ-2.5-is-2: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-2.5-is-2: " & Show(r)
                End If
            End Sub

            Sub P103()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(Null)
                If Err.Number <> 0 Then
                    L "environ-null: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-null: " & Show(r)
                End If
            End Sub

            Sub P104()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("PATH=")
                If Err.Number <> 0 Then
                    L "environ-name-eq: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-name-eq: " & Show(r)
                End If
            End Sub

            Sub P105()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(" PATH") = Environ("PATH")
                If Err.Number <> 0 Then
                    L "environ-name-space-is-name: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-name-space-is-name: " & Show(r)
                End If
            End Sub

            Sub P106()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("PATH ") = Environ("PATH")
                If Err.Number <> 0 Then
                    L "environ-name-trailing-space: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-name-trailing-space: " & Show(r)
                End If
            End Sub

            Sub P107()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(True)
                If Err.Number <> 0 Then
                    L "environ-bool: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-bool: " & Show(r)
                End If
            End Sub

            Sub P108()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(CInt(1)) = Environ(1)
                If Err.Number <> 0 Then
                    L "environ-int-var-is-number: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-int-var-is-number: " & Show(r)
                End If
            End Sub

            Sub P109()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ("1") = Environ(1)
                If Err.Number <> 0 Then
                    L "environ-str-num-is-name: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-str-num-is-name: " & Show(r)
                End If
            End Sub

            Sub P110()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Environ(Left(Environ(1), InStr(Environ(1), "=") - 1)) = Mid(Environ(1), InStr(Environ(1), "=") + 1)
                If Err.Number <> 0 Then
                    L "environ-name-of-first: err " & Err.Number & " " & Err.Description
                Else
                    L "environ-name-of-first: " & Show(r)
                End If
            End Sub

            Sub P111()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(True, "a", True, "b")
                If Err.Number <> 0 Then
                    L "switch-first: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-first: " & Show(r)
                End If
            End Sub

            Sub P112()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(False, 1, True, 2)
                If Err.Number <> 0 Then
                    L "switch-second: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-second: " & Show(r)
                End If
            End Sub

            Sub P113()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(False, 1, False, 2)
                If Err.Number <> 0 Then
                    L "switch-none: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-none: " & Show(r)
                End If
            End Sub

            Sub P114()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(True, 1, False)
                If Err.Number <> 0 Then
                    L "switch-odd: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-odd: " & Show(r)
                End If
            End Sub

            Sub P115()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(True)
                If Err.Number <> 0 Then
                    L "switch-one: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-one: " & Show(r)
                End If
            End Sub

            Sub P116()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(0, "zero", 2, "two")
                If Err.Number <> 0 Then
                    L "switch-num-cond: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-num-cond: " & Show(r)
                End If
            End Sub

            Sub P117()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch("True", "s")
                If Err.Number <> 0 Then
                    L "switch-str-cond: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-str-cond: " & Show(r)
                End If
            End Sub

            Sub P118()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch("x", "s")
                If Err.Number <> 0 Then
                    L "switch-str-cond-bad: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-str-cond-bad: " & Show(r)
                End If
            End Sub

            Sub P119()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(Null, 1, True, 2)
                If Err.Number <> 0 Then
                    L "switch-null-cond: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-null-cond: " & Show(r)
                End If
            End Sub

            Sub P120()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(Empty, 1, True, 2)
                If Err.Number <> 0 Then
                    L "switch-empty-cond: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-empty-cond: " & Show(r)
                End If
            End Sub

            Sub P121()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(True, 5)
                If Err.Number <> 0 Then
                    L "switch-type: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-type: " & TypeName(r)
                End If
            End Sub

            Sub P122()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(True, Null)
                If Err.Number <> 0 Then
                    L "switch-null-value: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-null-value: " & Show(r)
                End If
            End Sub

            Sub P123()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(True, F("v1", 1), False, F("v2", 2))
                If Err.Number <> 0 Then
                    L "switch-evaluates-values: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-evaluates-values: " & Show(r)
                End If
            End Sub

            Sub P124()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(F("c1", True), 1, F("c2", True), 2)
                If Err.Number <> 0 Then
                    L "switch-evaluates-conds: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-evaluates-conds: " & Show(r)
                End If
            End Sub

            Sub P125()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(Array(1), 1)
                If Err.Number <> 0 Then
                    L "switch-array-cond: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-array-cond: " & Show(r)
                End If
            End Sub

            Sub P126()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Switch(False, 1, False, 2, False, 3, False, 4, False, 5, False, 6, False, 7, True, 8)
                If Err.Number <> 0 Then
                    L "switch-14-pairs: err " & Err.Number & " " & Err.Description
                Else
                    L "switch-14-pairs: " & Show(r)
                End If
            End Sub

            Sub P127()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(6)
                If Err.Number <> 0 Then
                    L "error-6: err " & Err.Number & " " & Err.Description
                Else
                    L "error-6: " & Show(r)
                End If
            End Sub

            Sub P128()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error$(6)
                If Err.Number <> 0 Then
                    L "error-dollar-6: err " & Err.Number & " " & Err.Description
                Else
                    L "error-dollar-6: " & Show(r)
                End If
            End Sub

            Sub P129()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(0)
                If Err.Number <> 0 Then
                    L "error-0: err " & Err.Number & " " & Err.Description
                Else
                    L "error-0: " & Show(r)
                End If
            End Sub

            Sub P130()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(1)
                If Err.Number <> 0 Then
                    L "error-1: err " & Err.Number & " " & Err.Description
                Else
                    L "error-1: " & Show(r)
                End If
            End Sub

            Sub P131()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(1000)
                If Err.Number <> 0 Then
                    L "error-1000: err " & Err.Number & " " & Err.Description
                Else
                    L "error-1000: " & Show(r)
                End If
            End Sub

            Sub P132()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(32767)
                If Err.Number <> 0 Then
                    L "error-32767: err " & Err.Number & " " & Err.Description
                Else
                    L "error-32767: " & Show(r)
                End If
            End Sub

            Sub P133()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(65535)
                If Err.Number <> 0 Then
                    L "error-65535: err " & Err.Number & " " & Err.Description
                Else
                    L "error-65535: " & Show(r)
                End If
            End Sub

            Sub P134()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(65536)
                If Err.Number <> 0 Then
                    L "error-65536: err " & Err.Number & " " & Err.Description
                Else
                    L "error-65536: " & Show(r)
                End If
            End Sub

            Sub P135()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(-1)
                If Err.Number <> 0 Then
                    L "error-minus1: err " & Err.Number & " " & Err.Description
                Else
                    L "error-minus1: " & Show(r)
                End If
            End Sub

            Sub P136()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(6.6)
                If Err.Number <> 0 Then
                    L "error-6.6: err " & Err.Number & " " & Err.Description
                Else
                    L "error-6.6: " & Show(r)
                End If
            End Sub

            Sub P137()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(5.5)
                If Err.Number <> 0 Then
                    L "error-5.5: err " & Err.Number & " " & Err.Description
                Else
                    L "error-5.5: " & Show(r)
                End If
            End Sub

            Sub P138()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error("6")
                If Err.Number <> 0 Then
                    L "error-str: err " & Err.Number & " " & Err.Description
                Else
                    L "error-str: " & Show(r)
                End If
            End Sub

            Sub P139()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(Null)
                If Err.Number <> 0 Then
                    L "error-null: err " & Err.Number & " " & Err.Description
                Else
                    L "error-null: " & Show(r)
                End If
            End Sub

            Sub P140()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(Empty)
                If Err.Number <> 0 Then
                    L "error-empty: err " & Err.Number & " " & Err.Description
                Else
                    L "error-empty: " & Show(r)
                End If
            End Sub

            Sub P141()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error
                If Err.Number <> 0 Then
                    L "error-none-clean: err " & Err.Number & " " & Err.Description
                Else
                    L "error-none-clean: " & Show(r)
                End If
            End Sub

            Sub P142()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error()
                If Err.Number <> 0 Then
                    L "error-none-clean-parens: err " & Err.Number & " " & Err.Description
                Else
                    L "error-none-clean-parens: " & Show(r)
                End If
            End Sub

            Sub P143()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(6)
                If Err.Number <> 0 Then
                    L "error-type: err " & Err.Number & " " & Err.Description
                Else
                    L "error-type: " & TypeName(r)
                End If
            End Sub

            Sub P144()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error$(Null)
                If Err.Number <> 0 Then
                    L "error-dollar-null: err " & Err.Number & " " & Err.Description
                Else
                    L "error-dollar-null: " & Show(r)
                End If
            End Sub

            Sub P145()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = Error(vbObjectError + 1)
                If Err.Number <> 0 Then
                    L "error-vbObjectError: err " & Err.Number & " " & Err.Description
                Else
                    L "error-vbObjectError: " & Show(r)
                End If
            End Sub


            Sub ErrorAfterRaise()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                Err.Raise 11
                r = Error
                L "error-after-raise: " & Show(r)
                r = Error()
                L "error-after-raise-parens: " & Show(r)
                Err.Raise 1000, , "custom text"
                r = Error
                L "error-after-raise-custom: " & Show(r) & " | Err.Description " & Err.Description
                Err.Clear
                Err.Number = 6
                r = Error
                L "error-after-number-only: " & Show(r)
            End Sub

            Sub ErrorSweep()
                Dim n As Long, s As String, c As Long
                On Error Resume Next
                For n = 0 To 65535
                    Err.Clear
                    s = Error(n)
                    If Err.Number <> 0 Then
                        L "sweep " & n & ": err " & Err.Number
                    ElseIf s <> "Application-defined or object-defined error" Then
                        L "sweep " & n & ": " & s
                    Else
                        c = c + 1
                    End If
                Next
                L "sweep: " & c & " numbers gave Application-defined or object-defined error"
            End Sub

            Sub Main()
                P0
                P1
                P2
                P3
                P4
                P5
                P6
                P7
                P9
                P10
                P11
                P12
                P13
                P14
                P15
                P16
                P17
                P18
                P19
                P20
                P21
                P22
                P23
                P24
                P25
                P26
                P27
                P28
                P29
                P30
                P31
                P32
                P33
                P34
                P35
                P36
                P37
                P38
                P39
                P40
                P41
                P42
                P43
                P44
                P45
                P46
                P47
                P48
                P49
                P50
                P51
                P52
                P53
                P54
                P55
                P56
                P57
                P58
                P59
                P60
                P70
                P71
                P72
                P73
                P74
                P75
                P76
                P77
                P78
                P79
                P80
                P81
                P82
                P83
                P84
                P86
                P87
                P88
                P89
                P90
                P91
                P92
                P93
                P94
                P95
                P96
                P97
                P98
                P99
                P100
                P101
                P102
                P103
                P104
                P105
                P106
                P107
                P108
                P109
                P110
                P111
                P112
                P113
                P114
                P115
                P116
                P117
                P118
                P119
                P120
                P121
                P122
                P123
                P124
                P125
                P126
                P127
                P128
                P129
                P130
                P131
                P132
                P133
                P134
                P135
                P136
                P137
                P138
                P139
                P140
                P141
                P142
                P143
                P144
                P145
                TextRows
                ErrorAfterRaise
                ErrorSweep
            End Sub

            Main

            """"""",
            ("Module2", """""""
            Option Compare Text

            Public Sub TextRows()
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                r = StrComp("a", "A", 0)
                L "text-strcomp-0: " & Show(r)
                Err.Clear
                r = StrComp("a", "A", Empty)
                L "text-strcomp-cmp-empty: " & Show(r)
            End Sub

            """""""));

        AssertDebugLog(Lines("""
            strcomp-lt: Integer -1
            strcomp-gt: Integer 1
            strcomp-eq: Integer 0
            strcomp-case-default: Integer 1
            strcomp-case-0: Integer 1
            strcomp-case-1: Integer 0
            strcomp-case-vbText: Integer 0
            strcomp-2: err 5 Invalid procedure call or argument
            strcomp-minus1: err 5 Invalid procedure call or argument
            strcomp-minus2: err 5 Invalid procedure call or argument
            strcomp-1.5: err 5 Invalid procedure call or argument
            strcomp-0.5: Integer 1
            strcomp-str1: Integer 0
            strcomp-cmp-null: err 94 Invalid use of Null
            strcomp-cmp-empty: Integer 1
            strcomp-null-l: Null
            strcomp-null-r: Null
            strcomp-null-both: Null
            strcomp-empty: Integer 0
            strcomp-num: Integer -1
            strcomp-num-str: Integer 0
            strcomp-bool: Integer 0
            strcomp-prefix: Integer -1
            strcomp-empty-a: Integer -1
            strcomp-B-a: Integer -1
            strcomp-B-a-text: Integer 1
            strcomp-us-a-text: Integer -1
            strcomp-us-A-bin: Integer 1
            strcomp-sz-ss-text: Integer 0
            strcomp-ae-text: Integer 0
            strcomp-e-acute-text: Integer 1
            strcomp-e-acute-f-text: Integer -1
            strcomp-e-acute-f-bin: Integer 1
            strcomp-hyphen-text: Integer 1
            strcomp-apos-text: Integer 1
            strcomp-nul: Integer 1
            strcomp-array: err 13 Type mismatch
            strcomp-type: Integer
            strconv-upper: String HELLO WORLD
            strconv-lower: String hello world
            strconv-proper: String Hello World
            strconv-proper-digit: String 1st Place
            strconv-proper-apos: String O'neil's
            strconv-proper-hyphen: String Jean-luc
            strconv-proper-dot: String A.b,c;d
            strconv-proper-tab: [65 9 66 13 10 67 10 68 13 69]
            strconv-proper-nul: [65 0 66]
            strconv-proper-ff: [65 12 66 11 67]
            strconv-proper-spaces: String   Two  Spaces
            strconv-proper-accent: [201 99 111 108 101 32 201 99 111 108 101]
            strconv-proper-nbsp: [65 160 98]
            strconv-upper-sz: [223]
            strconv-upper-y-uml: [376]
            strconv-upper-mu: [181]
            strconv-upper-dotless: [73]
            strconv-lower-dotted: [105]
            strconv-upper-tr: [73]
            strconv-lower-tr: [105]
            strconv-upper-lcid-0: [73]
            strconv-upper-lcid-bad: err 5 Invalid procedure call or argument
            strconv-upper-from: err 5 Invalid procedure call or argument
            strconv-upper-lower: String Ab
            strconv-0: String ab
            strconv-wide: err 5 Invalid procedure call or argument
            strconv-narrow: err 5 Invalid procedure call or argument
            strconv-katakana: err 5 Invalid procedure call or argument
            strconv-hiragana: err 5 Invalid procedure call or argument
            strconv-256: err 5 Invalid procedure call or argument
            strconv-512: err 5 Invalid procedure call or argument
            strconv-null: Null
            strconv-null-conv: err 94 Invalid use of Null
            strconv-num: String 5.5
            strconv-bool: String TRUE
            strconv-empty: String 
            strconv-type: String
            strconv-conv-1.5: String ab
            strconv-conv-str: String AB
            environ-path-len: Boolean True
            environ-path-case: Boolean True
            environ-missing: String 
            environ-empty: err 5 Invalid procedure call or argument
            environ-type: String
            environ-dollar-type: String
            environ-1-has-eq: Boolean True
            environ-1-type: String
            environ-0: err 5 Invalid procedure call or argument
            environ-minus1: err 5 Invalid procedure call or argument
            environ-256: err 5 Invalid procedure call or argument
            environ-huge: err 6 Overflow
            environ-str-1: String 
            environ-1.5-is-2: Boolean True
            environ-2.5-is-2: Boolean True
            environ-null: err 94 Invalid use of Null
            environ-name-eq: String 
            environ-name-space-is-name: Boolean True
            environ-name-trailing-space: Boolean True
            environ-bool: err 5 Invalid procedure call or argument
            environ-int-var-is-number: Boolean True
            environ-str-num-is-name: Boolean False
            environ-name-of-first: Boolean True
            switch-first: String a
            switch-second: Integer 2
            switch-none: Null
            switch-odd: err 5 Invalid procedure call or argument
            switch-one: err 5 Invalid procedure call or argument
            switch-num-cond: String two
            switch-str-cond: String s
            switch-str-cond-bad: err 13 Type mismatch
            switch-null-cond: Integer 2
            switch-empty-cond: Integer 2
            switch-type: Integer
            switch-null-value: Null
              called v1
              called v2
            switch-evaluates-values: Integer 1
              called c1
              called c2
            switch-evaluates-conds: Integer 1
            switch-array-cond: err 13 Type mismatch
            switch-14-pairs: Integer 8
            error-6: String Overflow
            error-dollar-6: String Overflow
            error-0: String 
            error-1: String Application-defined or object-defined error
            error-1000: String Application-defined or object-defined error
            error-32767: String Application-defined or object-defined error
            error-65535: String Application-defined or object-defined error
            error-65536: err 6 Overflow
            error-minus1: String Application-defined or object-defined error
            error-6.6: String Out of memory
            error-5.5: String Overflow
            error-str: String Overflow
            error-null: err 94 Invalid use of Null
            error-empty: String 
            error-none-clean: String 
            error-none-clean-parens: String 
            error-type: String
            error-dollar-null: err 94 Invalid use of Null
            error-vbObjectError: String Application-defined or object-defined error
            text-strcomp-0: Integer 1
            text-strcomp-cmp-empty: Integer 1
            error-after-raise: String Division by zero
            error-after-raise-parens: String Division by zero
            error-after-raise-custom: String custom text | Err.Description custom text
            error-after-number-only: String Overflow
            sweep 0: 
            sweep 3: Return without GoSub
            sweep 5: Invalid procedure call or argument
            sweep 6: Overflow
            sweep 7: Out of memory
            sweep 9: Subscript out of range
            sweep 10: This array is fixed or temporarily locked
            sweep 11: Division by zero
            sweep 13: Type mismatch
            sweep 14: Out of string space
            sweep 16: Expression too complex
            sweep 17: Can't perform requested operation
            sweep 18: User interrupt occurred
            sweep 20: Resume without error
            sweep 28: Out of stack space
            sweep 35: Sub or Function not defined
            sweep 47: Too many DLL application clients
            sweep 48: Error in loading DLL
            sweep 49: Bad DLL calling convention
            sweep 51: Internal error
            sweep 52: Bad file name or number
            sweep 53: File not found
            sweep 54: Bad file mode
            sweep 55: File already open
            sweep 57: Device I/O error
            sweep 58: File already exists
            sweep 59: Bad record length
            sweep 61: Disk full
            sweep 62: Input past end of file
            sweep 63: Bad record number
            sweep 67: Too many files
            sweep 68: Device unavailable
            sweep 70: Permission denied
            sweep 71: Disk not ready
            sweep 74: Can't rename with different drive
            sweep 75: Path/File access error
            sweep 76: Path not found
            sweep 91: Object variable or With block variable not set
            sweep 92: For loop not initialized
            sweep 93: Invalid pattern string
            sweep 94: Invalid use of Null
            sweep 96: Unable to sink events of object because the object is already firing events to the maximum number of event receivers that it supports
            sweep 97: Can not call friend function on object which is not an instance of defining class
            sweep 98: A property or method call cannot include a reference to a private object, either as an argument or as a return value
            sweep 321: Invalid file format
            sweep 322: Can't create necessary temporary file
            sweep 325: Invalid format in resource file
            sweep 380: Invalid property value
            sweep 381: Invalid property array index
            sweep 382: Set not supported at runtime
            sweep 383: Set not supported (read-only property)
            sweep 385: Need property array index
            sweep 387: Set not permitted
            sweep 393: Get not supported at runtime
            sweep 394: Get not supported (write-only property)
            sweep 422: Property not found
            sweep 423: Property or method not found
            sweep 424: Object required
            sweep 429: ActiveX component can't create object
            sweep 430: Class does not support Automation or does not support expected interface
            sweep 432: File name or class name not found during Automation operation
            sweep 438: Object doesn't support this property or method
            sweep 440: Automation error
            sweep 442: Connection to type library or object library for remote process has been lost. Press OK for dialog to remove reference.
            sweep 443: Automation object does not have a default value
            sweep 445: Object doesn't support this action
            sweep 446: Object doesn't support named arguments
            sweep 447: Object doesn't support current locale setting
            sweep 448: Named argument not found
            sweep 449: Argument not optional
            sweep 450: Wrong number of arguments or invalid property assignment
            sweep 451: Property let procedure not defined and property get procedure did not return an object
            sweep 452: Invalid ordinal
            sweep 453: Specified DLL function not found
            sweep 454: Code resource not found
            sweep 455: Code resource lock error
            sweep 457: This key is already associated with an element of this collection
            sweep 458: Variable uses an Automation type not supported in Visual Basic
            sweep 459: Object or class does not support the set of events
            sweep 460: Invalid clipboard format
            sweep 461: Method or data member not found
            sweep 462: The remote server machine does not exist or is unavailable
            sweep 463: Class not registered on local machine
            sweep 481: Invalid picture
            sweep 482: Printer error
            sweep 735: Can't save file to TEMP
            sweep 744: Search text not found
            sweep 746: Replacements too long
            sweep: 65448 numbers gave Application-defined or object-defined error
            """));
    }

    // Which argument is checked first, StrConv's case through the ANSI code page for characters it lacks, and Error()
    // at the ends of a Long.
    [Fact]
    public async Task The_edges_the_first_probe_left_open()
    {
        await Run("""""""

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Codes(ByVal s As String) As String
                Dim i As Long, r As String
                For i = 1 To Len(s)
                    If i > 1 Then r = r & "."
                    r = r & AscW(Mid$(s, i, 1))
                Next
                Codes = "[" & r & "]"
            End Function

            Public Function Show(v As Variant) As String
                If IsNull(v) Then
                    Show = "Null"
                ElseIf IsEmpty(v) Then
                    Show = "Empty"
                Else
                    Show = TypeName(v) & " " & CStr(v)
                End If
            End Function

            Sub R0()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(StrConv(ChrW(&H4E00) & ChrW(&H3B1) & ChrW(&H101) & ChrW(&H20AC), vbUpperCase))
                If Err.Number <> 0 Then L "strconv-upper-nonansi: err " & Err.Number & " " & Err.Description Else L "strconv-upper-nonansi: " & Show(r)
            End Sub

            Sub R1()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(StrConv(ChrW(&H391) & ChrW(&H100), vbLowerCase))
                If Err.Number <> 0 Then L "strconv-lower-nonansi: err " & Err.Number & " " & Err.Description Else L "strconv-lower-nonansi: " & Show(r)
            End Sub

            Sub R2()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(StrConv(ChrW(&H101) & "b c" & ChrW(&H3B1), vbProperCase))
                If Err.Number <> 0 Then L "strconv-proper-nonansi: err " & Err.Number & " " & Err.Description Else L "strconv-proper-nonansi: " & Show(r)
            End Sub

            Sub R3()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(StrConv(ChrW(&H101) & ChrW(&H4E00), 0))
                If Err.Number <> 0 Then L "strconv-0-nonansi: err " & Err.Number & " " & Err.Description Else L "strconv-0-nonansi: " & Show(r)
            End Sub

            Sub R4()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv("ab", vbUpperCase Or vbUnicode)
                If Err.Number <> 0 Then L "strconv-upper-unicode: err " & Err.Number & " " & Err.Description Else L "strconv-upper-unicode: " & Show(r)
            End Sub

            Sub R5()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv("ab", vbUnicode Or vbFromUnicode)
                If Err.Number <> 0 Then L "strconv-unicode-from: err " & Err.Number & " " & Err.Description Else L "strconv-unicode-from: " & Show(r)
            End Sub

            Sub R6()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv(Null, Null)
                If Err.Number <> 0 Then L "strconv-null-null: err " & Err.Number & " " & Err.Description Else L "strconv-null-null: " & Show(r)
            End Sub

            Sub R7()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv(Null, 256)
                If Err.Number <> 0 Then L "strconv-null-bad: err " & Err.Number & " " & Err.Description Else L "strconv-null-bad: " & Show(r)
            End Sub

            Sub R8()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv("a", vbUpperCase, Null)
                If Err.Number <> 0 Then L "strconv-lcid-null: err " & Err.Number & " " & Err.Description Else L "strconv-lcid-null: " & Show(r)
            End Sub

            Sub R9()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv("a", vbUpperCase, 2057)
                If Err.Number <> 0 Then L "strconv-lcid-2057: err " & Err.Number & " " & Err.Description Else L "strconv-lcid-2057: " & Show(r)
            End Sub

            Sub R10()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv("a", vbUpperCase, 1)
                If Err.Number <> 0 Then L "strconv-lcid-1: err " & Err.Number & " " & Err.Description Else L "strconv-lcid-1: " & Show(r)
            End Sub

            Sub R11()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv("a", vbUpperCase, -1)
                If Err.Number <> 0 Then L "strconv-lcid-minus1: err " & Err.Number & " " & Err.Description Else L "strconv-lcid-minus1: " & Show(r)
            End Sub

            Sub R12()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(StrConv(Chr(0) & "ab", vbProperCase))
                If Err.Number <> 0 Then L "strconv-proper-after-nul-lead: err " & Err.Number & " " & Err.Description Else L "strconv-proper-after-nul-lead: " & Show(r)
            End Sub

            Sub R13()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv("mcDONALD o'BRIEN", vbProperCase)
                If Err.Number <> 0 Then L "strconv-proper-mixed: err " & Err.Number & " " & Err.Description Else L "strconv-proper-mixed: " & Show(r)
            End Sub

            Sub R14()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrConv(Array("a"), vbUpperCase)
                If Err.Number <> 0 Then L "strconv-array: err " & Err.Number & " " & Err.Description Else L "strconv-array: " & Show(r)
            End Sub

            Sub R15()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrComp(Null, "a", Null)
                If Err.Number <> 0 Then L "strcomp-null-and-null-compare: err " & Err.Number & " " & Err.Description Else L "strcomp-null-and-null-compare: " & Show(r)
            End Sub

            Sub R16()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrComp(Array(1), Null)
                If Err.Number <> 0 Then L "strcomp-array-and-null: err " & Err.Number & " " & Err.Description Else L "strcomp-array-and-null: " & Show(r)
            End Sub

            Sub R17()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = StrComp(Null, Array(1))
                If Err.Number <> 0 Then L "strcomp-null-and-array: err " & Err.Number & " " & Err.Description Else L "strcomp-null-and-array: " & Show(r)
            End Sub

            Sub R18()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Error(-70000)
                If Err.Number <> 0 Then L "error-minus-70000: err " & Err.Number & " " & Err.Description Else L "error-minus-70000: " & Show(r)
            End Sub

            Sub R19()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Error(2147483647)
                If Err.Number <> 0 Then L "error-long-max: err " & Err.Number & " " & Err.Description Else L "error-long-max: " & Show(r)
            End Sub

            Sub R20()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Error(-2147483648#)
                If Err.Number <> 0 Then L "error-long-min: err " & Err.Number & " " & Err.Description Else L "error-long-min: " & Show(r)
            End Sub

            Sub R21()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Error(2147483648#)
                If Err.Number <> 0 Then L "error-past-long: err " & Err.Number & " " & Err.Description Else L "error-past-long: " & Show(r)
            End Sub

            Sub R22()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Error(True)
                If Err.Number <> 0 Then L "error-true: err " & Err.Number & " " & Err.Description Else L "error-true: " & Show(r)
            End Sub

            Sub R23()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Error(65535.5)
                If Err.Number <> 0 Then L "error-65535-5: err " & Err.Number & " " & Err.Description Else L "error-65535-5: " & Show(r)
            End Sub

            Sub R24()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Error(Array(6))
                If Err.Number <> 0 Then L "error-array: err " & Err.Number & " " & Err.Description Else L "error-array: " & Show(r)
            End Sub

            Sub R25()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Switch(New Collection, 1)
                If Err.Number <> 0 Then L "switch-object-cond: err " & Err.Number & " " & Err.Description Else L "switch-object-cond: " & Show(r)
            End Sub

            Sub R26()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Switch(#1/1/2000#, "d")
                If Err.Number <> 0 Then L "switch-date-cond: err " & Err.Number & " " & Err.Description Else L "switch-date-cond: " & Show(r)
            End Sub

            Sub R27()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Switch(True, 1, "x", 2)
                If Err.Number <> 0 Then L "switch-cond-error-after-true: err " & Err.Number & " " & Err.Description Else L "switch-cond-error-after-true: " & Show(r)
            End Sub

            Sub Main()
                R0
                R1
                R2
                R3
                R4
                R5
                R6
                R7
                R8
                R9
                R10
                R11
                R12
                R13
                R14
                R15
                R16
                R17
                R18
                R19
                R20
                R21
                R22
                R23
                R24
                R25
                R26
                R27
            End Sub

            Main

            """"""");

        AssertDebugLog(Lines("""
            strconv-upper-nonansi: String [63.65.65.8364]
            strconv-lower-nonansi: String [63.97]
            strconv-proper-nonansi: String [65.98.32.67.97]
            strconv-0-nonansi: String [257.19968]
            strconv-upper-unicode: err 5 Invalid procedure call or argument
            strconv-unicode-from: err 5 Invalid procedure call or argument
            strconv-null-null: err 94 Invalid use of Null
            strconv-null-bad: err 5 Invalid procedure call or argument
            strconv-lcid-null: err 94 Invalid use of Null
            strconv-lcid-2057: String A
            strconv-lcid-1: String A
            strconv-lcid-minus1: err 5 Invalid procedure call or argument
            strconv-proper-after-nul-lead: String [0.65.98]
            strconv-proper-mixed: String Mcdonald O'brien
            strconv-array: err 13 Type mismatch
            strcomp-null-and-null-compare: err 94 Invalid use of Null
            strcomp-array-and-null: err 13 Type mismatch
            strcomp-null-and-array: err 13 Type mismatch
            error-minus-70000: String Application-defined or object-defined error
            error-long-max: err 6 Overflow
            error-long-min: String Application-defined or object-defined error
            error-past-long: err 6 Overflow
            error-true: String Application-defined or object-defined error
            error-65535-5: err 6 Overflow
            error-array: err 13 Type mismatch
            switch-object-cond: err 450 Wrong number of arguments or invalid property assignment
            switch-date-cond: String d
            switch-cond-error-after-true: Integer 1
            """));
    }

    // UCase and LCase take a case only where it comes back; StrConv takes one only where the ANSI code page has it, so
    // f-with-hook stays as it is there. Proper case starts a word after Chr(0), a tab, LF, VT, FF, CR and a space.
    [Fact]
    public async Task UCase_LCase_and_StrConvs_case_over_every_character_Chr_gives()
    {
        await Run("""""""
            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Sub Main()
                Dim i As Long, c As String, u As Long, lo As Long, su As Long, sl As Long, p As String, breaks As String
                For i = 0 To 255
                    c = Chr$(i)
                    u = AscW(UCase$(c)): lo = AscW(LCase$(c))
                    su = AscW(StrConv(c, vbUpperCase)): sl = AscW(StrConv(c, vbLowerCase))
                    If u <> AscW(c) Or lo <> AscW(c) Or su <> u Or sl <> lo Then
                        L "case " & i & " " & AscW(c) & ": U " & u & " L " & lo & " SU " & su & " SL " & sl
                    End If
                    p = StrConv("a" & c & "b", vbProperCase)
                    If Right$(p, 1) = "B" Then breaks = breaks & " " & i
                Next
                L "proper breaks after:" & breaks
                L "proper first char after break: " & StrConv("x" & Chr$(32) & Chr$(233), vbProperCase) = "X " & Chr$(201)
                L "ucase wide: " & AscW(UCase$(ChrW(&HFF41))) & " lcase wide: " & AscW(LCase$(ChrW(&HFF21)))
                L "ucase greek: " & AscW(UCase$(ChrW(&H3B1))) & " lcase greek: " & AscW(LCase$(ChrW(&H391)))
                L "ucase final sigma: " & AscW(UCase$(ChrW(&H3C2))) & " ucase micro: " & AscW(UCase$(ChrW(&HB5)))
                L "ucase dotless: " & AscW(UCase$(ChrW(&H131))) & " lcase dotted: " & AscW(LCase$(ChrW(&H130)))
                L "ucase long s: " & AscW(UCase$(ChrW(&H17F))) & " lcase kelvin: " & AscW(LCase$(ChrW(&H212A)))
            End Sub

            Main

            """"""");

        AssertDebugLog(Lines("""
            case 65 65: U 65 L 97 SU 65 SL 97
            case 66 66: U 66 L 98 SU 66 SL 98
            case 67 67: U 67 L 99 SU 67 SL 99
            case 68 68: U 68 L 100 SU 68 SL 100
            case 69 69: U 69 L 101 SU 69 SL 101
            case 70 70: U 70 L 102 SU 70 SL 102
            case 71 71: U 71 L 103 SU 71 SL 103
            case 72 72: U 72 L 104 SU 72 SL 104
            case 73 73: U 73 L 105 SU 73 SL 105
            case 74 74: U 74 L 106 SU 74 SL 106
            case 75 75: U 75 L 107 SU 75 SL 107
            case 76 76: U 76 L 108 SU 76 SL 108
            case 77 77: U 77 L 109 SU 77 SL 109
            case 78 78: U 78 L 110 SU 78 SL 110
            case 79 79: U 79 L 111 SU 79 SL 111
            case 80 80: U 80 L 112 SU 80 SL 112
            case 81 81: U 81 L 113 SU 81 SL 113
            case 82 82: U 82 L 114 SU 82 SL 114
            case 83 83: U 83 L 115 SU 83 SL 115
            case 84 84: U 84 L 116 SU 84 SL 116
            case 85 85: U 85 L 117 SU 85 SL 117
            case 86 86: U 86 L 118 SU 86 SL 118
            case 87 87: U 87 L 119 SU 87 SL 119
            case 88 88: U 88 L 120 SU 88 SL 120
            case 89 89: U 89 L 121 SU 89 SL 121
            case 90 90: U 90 L 122 SU 90 SL 122
            case 97 97: U 65 L 97 SU 65 SL 97
            case 98 98: U 66 L 98 SU 66 SL 98
            case 99 99: U 67 L 99 SU 67 SL 99
            case 100 100: U 68 L 100 SU 68 SL 100
            case 101 101: U 69 L 101 SU 69 SL 101
            case 102 102: U 70 L 102 SU 70 SL 102
            case 103 103: U 71 L 103 SU 71 SL 103
            case 104 104: U 72 L 104 SU 72 SL 104
            case 105 105: U 73 L 105 SU 73 SL 105
            case 106 106: U 74 L 106 SU 74 SL 106
            case 107 107: U 75 L 107 SU 75 SL 107
            case 108 108: U 76 L 108 SU 76 SL 108
            case 109 109: U 77 L 109 SU 77 SL 109
            case 110 110: U 78 L 110 SU 78 SL 110
            case 111 111: U 79 L 111 SU 79 SL 111
            case 112 112: U 80 L 112 SU 80 SL 112
            case 113 113: U 81 L 113 SU 81 SL 113
            case 114 114: U 82 L 114 SU 82 SL 114
            case 115 115: U 83 L 115 SU 83 SL 115
            case 116 116: U 84 L 116 SU 84 SL 116
            case 117 117: U 85 L 117 SU 85 SL 117
            case 118 118: U 86 L 118 SU 86 SL 118
            case 119 119: U 87 L 119 SU 87 SL 119
            case 120 120: U 88 L 120 SU 88 SL 120
            case 121 121: U 89 L 121 SU 89 SL 121
            case 122 122: U 90 L 122 SU 90 SL 122
            case 131 402: U 401 L 402 SU 402 SL 402
            case 138 352: U 352 L 353 SU 352 SL 353
            case 140 338: U 338 L 339 SU 338 SL 339
            case 142 381: U 381 L 382 SU 381 SL 382
            case 154 353: U 352 L 353 SU 352 SL 353
            case 156 339: U 338 L 339 SU 338 SL 339
            case 158 382: U 381 L 382 SU 381 SL 382
            case 159 376: U 376 L 255 SU 376 SL 255
            case 192 192: U 192 L 224 SU 192 SL 224
            case 193 193: U 193 L 225 SU 193 SL 225
            case 194 194: U 194 L 226 SU 194 SL 226
            case 195 195: U 195 L 227 SU 195 SL 227
            case 196 196: U 196 L 228 SU 196 SL 228
            case 197 197: U 197 L 229 SU 197 SL 229
            case 198 198: U 198 L 230 SU 198 SL 230
            case 199 199: U 199 L 231 SU 199 SL 231
            case 200 200: U 200 L 232 SU 200 SL 232
            case 201 201: U 201 L 233 SU 201 SL 233
            case 202 202: U 202 L 234 SU 202 SL 234
            case 203 203: U 203 L 235 SU 203 SL 235
            case 204 204: U 204 L 236 SU 204 SL 236
            case 205 205: U 205 L 237 SU 205 SL 237
            case 206 206: U 206 L 238 SU 206 SL 238
            case 207 207: U 207 L 239 SU 207 SL 239
            case 208 208: U 208 L 240 SU 208 SL 240
            case 209 209: U 209 L 241 SU 209 SL 241
            case 210 210: U 210 L 242 SU 210 SL 242
            case 211 211: U 211 L 243 SU 211 SL 243
            case 212 212: U 212 L 244 SU 212 SL 244
            case 213 213: U 213 L 245 SU 213 SL 245
            case 214 214: U 214 L 246 SU 214 SL 246
            case 216 216: U 216 L 248 SU 216 SL 248
            case 217 217: U 217 L 249 SU 217 SL 249
            case 218 218: U 218 L 250 SU 218 SL 250
            case 219 219: U 219 L 251 SU 219 SL 251
            case 220 220: U 220 L 252 SU 220 SL 252
            case 221 221: U 221 L 253 SU 221 SL 253
            case 222 222: U 222 L 254 SU 222 SL 254
            case 224 224: U 192 L 224 SU 192 SL 224
            case 225 225: U 193 L 225 SU 193 SL 225
            case 226 226: U 194 L 226 SU 194 SL 226
            case 227 227: U 195 L 227 SU 195 SL 227
            case 228 228: U 196 L 228 SU 196 SL 228
            case 229 229: U 197 L 229 SU 197 SL 229
            case 230 230: U 198 L 230 SU 198 SL 230
            case 231 231: U 199 L 231 SU 199 SL 231
            case 232 232: U 200 L 232 SU 200 SL 232
            case 233 233: U 201 L 233 SU 201 SL 233
            case 234 234: U 202 L 234 SU 202 SL 234
            case 235 235: U 203 L 235 SU 203 SL 235
            case 236 236: U 204 L 236 SU 204 SL 236
            case 237 237: U 205 L 237 SU 205 SL 237
            case 238 238: U 206 L 238 SU 206 SL 238
            case 239 239: U 207 L 239 SU 207 SL 239
            case 240 240: U 208 L 240 SU 208 SL 240
            case 241 241: U 209 L 241 SU 209 SL 241
            case 242 242: U 210 L 242 SU 210 SL 242
            case 243 243: U 211 L 243 SU 211 SL 243
            case 244 244: U 212 L 244 SU 212 SL 244
            case 245 245: U 213 L 245 SU 213 SL 245
            case 246 246: U 214 L 246 SU 214 SL 246
            case 248 248: U 216 L 248 SU 216 SL 248
            case 249 249: U 217 L 249 SU 217 SL 249
            case 250 250: U 218 L 250 SU 218 SL 250
            case 251 251: U 219 L 251 SU 219 SL 251
            case 252 252: U 220 L 252 SU 220 SL 252
            case 253 253: U 221 L 253 SU 221 SL 253
            case 254 254: U 222 L 254 SU 222 SL 254
            case 255 255: U 376 L 255 SU 376 SL 255
            proper breaks after: 0 9 10 11 12 13 32
            False
            ucase wide: -223 lcase wide: -191
            ucase greek: 913 lcase greek: 945
            ucase final sigma: 962 ucase micro: 181
            ucase dotless: 305 lcase dotted: 304
            ucase long s: 383 lcase kelvin: 8490
            """));
    }

    private static List<Vb6Value> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new Vb6Value(l)).ToList();
}
