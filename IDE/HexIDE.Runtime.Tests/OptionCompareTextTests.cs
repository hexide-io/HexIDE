// Generated from the vb6.exe probes for #1567: each program is the probe as it ran, printing where the probe logged,
// and each expected line is what vb6.exe logged, native and p-code alike.

using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Option Compare Text</c> and a compare of 1, as vb6.exe measured them (#1567). The comparer itself is pinned pair by
/// pair in <see cref="TextCompareTests"/>; these run the probe programs, so they pin where each comparison is made.
/// </summary>
public class OptionCompareTextTests : BaseVBTestFixture
{
    // Each row is evaluated in a Binary module (B) and a Text module (T). Option Compare is the module's where the
    // comparison is written: a Binary module's function called from the Text module compares as Binary. Rows where a
    // built-in given no compare would take the module's are left out, as its own change, and so are the two that
    // compare a String with a number, which raise 13 here in either mode (#211).
    [Fact]
    public async Task Option_Compare_Text_compares_with_the_operators_and_Select_Case_in_its_own_module()
    {
        await RunModules("""""""
            Option Compare Binary
            Dim sv As String, su As String, vv As Variant, vu As Variant

            Public Sub L(ByVal s As String)
                Debug.Print s
            End Sub

            Public Function Show(v As Variant) As String
                If IsNull(v) Then
                    Show = "Null"
                ElseIf IsEmpty(v) Then
                    Show = "Empty"
                ElseIf IsArray(v) Then
                    Show = TypeName(v) & " " & (UBound(v) - LBound(v) + 1)
                Else
                    Show = TypeName(v) & " " & CStr(v)
                End If
            End Function

            Public Function BinaryEquals(ByVal a As String, ByVal b As String) As Boolean
                BinaryEquals = (a = b)
            End Function


            Public Function SelectCaseRowsB() As String
                Dim s As String, r As String
                s = "a"
                Select Case s
                    Case "A": r = r & "case-A "
                    Case Else: r = r & "else "
                End Select
                s = "B"
                Select Case s
                    Case "a" To "c": r = r & "range "
                    Case Else: r = r & "else "
                End Select
                s = "A"
                Select Case s
                    Case Is < "b": r = r & "is-lt "
                    Case Else: r = r & "else "
                End Select
                s = "abc"
                Select Case s
                    Case Is = "ABC": r = r & "is-eq "
                    Case Else: r = r & "else "
                End Select
                s = "_"
                Select Case s
                    Case "A" To "z": r = r & "range-A-z "
                    Case Else: r = r & "else "
                End Select
                SelectCaseRowsB = Trim$(r)
            End Function

            Public Function CollectionKeyB() As Variant
                Dim c As New Collection
                c.Add 1, "abc"
                On Error Resume Next
                c.Add 2, "ABC"
                CollectionKeyB = Err.Number
            End Function

            Public Function R0B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("abc" = "ABC")
                If Err.Number <> 0 Then
                    R0B = "err " & Err.Number & " " & Err.Description
                Else
                    R0B = Show(r)
                End If
            End Function

            Public Function R1B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("abc" <> "ABC")
                If Err.Number <> 0 Then
                    R1B = "err " & Err.Number & " " & Err.Description
                Else
                    R1B = Show(r)
                End If
            End Function

            Public Function R2B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" < "B")
                If Err.Number <> 0 Then
                    R2B = "err " & Err.Number & " " & Err.Description
                Else
                    R2B = Show(r)
                End If
            End Function

            Public Function R3B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("B" < "a")
                If Err.Number <> 0 Then
                    R3B = "err " & Err.Number & " " & Err.Description
                Else
                    R3B = Show(r)
                End If
            End Function

            Public Function R4B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" <= "A")
                If Err.Number <> 0 Then
                    R4B = "err " & Err.Number & " " & Err.Description
                Else
                    R4B = Show(r)
                End If
            End Function

            Public Function R5B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" < "A")
                If Err.Number <> 0 Then
                    R5B = "err " & Err.Number & " " & Err.Description
                Else
                    R5B = Show(r)
                End If
            End Function

            Public Function R6B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" > "A")
                If Err.Number <> 0 Then
                    R6B = "err " & Err.Number & " " & Err.Description
                Else
                    R6B = Show(r)
                End If
            End Function

            Public Function R7B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("_" < "a")
                If Err.Number <> 0 Then
                    R7B = "err " & Err.Number & " " & Err.Description
                Else
                    R7B = Show(r)
                End If
            End Function

            Public Function R8B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("Z" < "_")
                If Err.Number <> 0 Then
                    R8B = "err " & Err.Number & " " & Err.Description
                Else
                    R8B = Show(r)
                End If
            End Function

            Public Function R9B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("[" < "a")
                If Err.Number <> 0 Then
                    R9B = "err " & Err.Number & " " & Err.Description
                Else
                    R9B = Show(r)
                End If
            End Function

            Public Function R10B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("1" < "a")
                If Err.Number <> 0 Then
                    R10B = "err " & Err.Number & " " & Err.Description
                Else
                    R10B = Show(r)
                End If
            End Function

            Public Function R11B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (" " < "a")
                If Err.Number <> 0 Then
                    R11B = "err " & Err.Number & " " & Err.Description
                Else
                    R11B = Show(r)
                End If
            End Function

            Public Function R12B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" = "a ")
                If Err.Number <> 0 Then
                    R12B = "err " & Err.Number & " " & Err.Description
                Else
                    R12B = Show(r)
                End If
            End Function

            Public Function R13B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(233) = "e")
                If Err.Number <> 0 Then
                    R13B = "err " & Err.Number & " " & Err.Description
                Else
                    R13B = Show(r)
                End If
            End Function

            Public Function R14B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("e" < Chr(233))
                If Err.Number <> 0 Then
                    R14B = "err " & Err.Number & " " & Err.Description
                Else
                    R14B = Show(r)
                End If
            End Function

            Public Function R15B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(233) < "f")
                If Err.Number <> 0 Then
                    R15B = "err " & Err.Number & " " & Err.Description
                Else
                    R15B = Show(r)
                End If
            End Function

            Public Function R16B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(201) < "f")
                If Err.Number <> 0 Then
                    R16B = "err " & Err.Number & " " & Err.Description
                Else
                    R16B = Show(r)
                End If
            End Function

            Public Function R17B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(223) = "ss")
                If Err.Number <> 0 Then
                    R17B = "err " & Err.Number & " " & Err.Description
                Else
                    R17B = Show(r)
                End If
            End Function

            Public Function R18B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(230) = "ae")
                If Err.Number <> 0 Then
                    R18B = "err " & Err.Number & " " & Err.Description
                Else
                    R18B = Show(r)
                End If
            End Function

            Public Function R19B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("coop" = "co-op")
                If Err.Number <> 0 Then
                    R19B = "err " & Err.Number & " " & Err.Description
                Else
                    R19B = Show(r)
                End If
            End Function

            Public Function R20B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("co-op" < "coop")
                If Err.Number <> 0 Then
                    R20B = "err " & Err.Number & " " & Err.Description
                Else
                    R20B = Show(r)
                End If
            End Function

            Public Function R21B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("its" < "its")
                If Err.Number <> 0 Then
                    R21B = "err " & Err.Number & " " & Err.Description
                Else
                    R21B = Show(r)
                End If
            End Function

            Public Function R22B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (sv = su)
                If Err.Number <> 0 Then
                    R22B = "err " & Err.Number & " " & Err.Description
                Else
                    R22B = Show(r)
                End If
            End Function

            Public Function R23B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (vv = vu)
                If Err.Number <> 0 Then
                    R23B = "err " & Err.Number & " " & Err.Description
                Else
                    R23B = Show(r)
                End If
            End Function

            Public Function R24B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (vv = su)
                If Err.Number <> 0 Then
                    R24B = "err " & Err.Number & " " & Err.Description
                Else
                    R24B = Show(r)
                End If
            End Function

            Public Function R25B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (su < sv)
                If Err.Number <> 0 Then
                    R25B = "err " & Err.Number & " " & Err.Description
                Else
                    R25B = Show(r)
                End If
            End Function

            Public Function R26B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" = Null)
                If Err.Number <> 0 Then
                    R26B = "err " & Err.Number & " " & Err.Description
                Else
                    R26B = Show(r)
                End If
            End Function

            Public Function R27B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("10" < 9)
                If Err.Number <> 0 Then
                    R27B = "err " & Err.Number & " " & Err.Description
                Else
                    R27B = Show(r)
                End If
            End Function

            Public Function R28B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("5" = 5)
                If Err.Number <> 0 Then
                    R28B = "err " & Err.Number & " " & Err.Description
                Else
                    R28B = Show(r)
                End If
            End Function

            Public Function R29B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (BinaryEquals("abc", "ABC"))
                If Err.Number <> 0 Then
                    R29B = "err " & Err.Number & " " & Err.Description
                Else
                    R29B = Show(r)
                End If
            End Function

            Public Function R30B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStr("ABC", "b"))
                If Err.Number <> 0 Then
                    R30B = "err " & Err.Number & " " & Err.Description
                Else
                    R30B = Show(r)
                End If
            End Function

            Public Function R31B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStr(1, "ABC", "b"))
                If Err.Number <> 0 Then
                    R31B = "err " & Err.Number & " " & Err.Description
                Else
                    R31B = Show(r)
                End If
            End Function

            Public Function R32B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStr(1, "ABC", "b", 0))
                If Err.Number <> 0 Then
                    R32B = "err " & Err.Number & " " & Err.Description
                Else
                    R32B = Show(r)
                End If
            End Function

            Public Function R33B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStrRev("ABCabc", "B"))
                If Err.Number <> 0 Then
                    R33B = "err " & Err.Number & " " & Err.Description
                Else
                    R33B = Show(r)
                End If
            End Function

            Public Function R34B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Replace("ABCabc", "b", "x"))
                If Err.Number <> 0 Then
                    R34B = "err " & Err.Number & " " & Err.Description
                Else
                    R34B = Show(r)
                End If
            End Function

            Public Function R35B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Split("aXbxc", "x"))
                If Err.Number <> 0 Then
                    R35B = "err " & Err.Number & " " & Err.Description
                Else
                    R35B = Show(r)
                End If
            End Function

            Public Function R36B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Filter(Array("A", "b", "a"), "a"))
                If Err.Number <> 0 Then
                    R36B = "err " & Err.Number & " " & Err.Description
                Else
                    R36B = Show(r)
                End If
            End Function

            Public Function R37B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (StrComp("a", "A"))
                If Err.Number <> 0 Then
                    R37B = "err " & Err.Number & " " & Err.Description
                Else
                    R37B = Show(r)
                End If
            End Function

            Public Function R38B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("ABC" Like "a*")
                If Err.Number <> 0 Then
                    R38B = "err " & Err.Number & " " & Err.Description
                Else
                    R38B = Show(r)
                End If
            End Function

            Public Function R39B() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (CollectionKeyB())
                If Err.Number <> 0 Then
                    R39B = "err " & Err.Number & " " & Err.Description
                Else
                    R39B = Show(r)
                End If
            End Function

            Sub Main()
                L "eq-case: B=" & R0B() & " | T=" & R0T()
                L "ne-case: B=" & R1B() & " | T=" & R1T()
                L "lt-a-B: B=" & R2B() & " | T=" & R2T()
                L "lt-B-a: B=" & R3B() & " | T=" & R3T()
                L "le-a-A: B=" & R4B() & " | T=" & R4T()
                L "lt-a-A: B=" & R5B() & " | T=" & R5T()
                L "gt-a-A: B=" & R6B() & " | T=" & R6T()
                L "lt-us-a: B=" & R7B() & " | T=" & R7T()
                L "lt-Z-us: B=" & R8B() & " | T=" & R8T()
                L "lt-br-a: B=" & R9B() & " | T=" & R9T()
                L "lt-1-a: B=" & R10B() & " | T=" & R10T()
                L "lt-sp-a: B=" & R11B() & " | T=" & R11T()
                L "eq-trailing: B=" & R12B() & " | T=" & R12T()
                L "eq-e-acute-e: B=" & R13B() & " | T=" & R13T()
                L "lt-e-e-acute: B=" & R14B() & " | T=" & R14T()
                L "lt-e-acute-f: B=" & R15B() & " | T=" & R15T()
                L "lt-E-acute-f: B=" & R16B() & " | T=" & R16T()
                L "eq-sz-ss: B=" & R17B() & " | T=" & R17T()
                L "eq-ae: B=" & R18B() & " | T=" & R18T()
                L "eq-hyphen: B=" & R19B() & " | T=" & R19T()
                L "lt-hyphen: B=" & R20B() & " | T=" & R20T()
                L "lt-apos: B=" & R21B() & " | T=" & R21T()
                L "eq-vars: B=" & R22B() & " | T=" & R22T()
                L "eq-variants: B=" & R23B() & " | T=" & R23T()
                L "eq-var-str: B=" & R24B() & " | T=" & R24T()
                L "lt-vars: B=" & R25B() & " | T=" & R25T()
                L "eq-null: B=" & R26B() & " | T=" & R26T()
                L "cross-module: B=" & R29B() & " | T=" & R29T()
                L "instr-explicit-0: B=" & R32B() & " | T=" & R32T()
                L "like: B=" & R38B() & " | T=" & R38T()
                L "collection-key: B=" & R39B() & " | T=" & R39T()
                L "select-case: B=" & SelectCaseRowsB() & " | T=" & SelectCaseRowsT()
            End Sub

            Main

            """"""",
            ("Module2", """""""
            Option Compare Text
            Dim sv As String, su As String, vv As Variant, vu As Variant


            Public Function SelectCaseRowsT() As String
                Dim s As String, r As String
                s = "a"
                Select Case s
                    Case "A": r = r & "case-A "
                    Case Else: r = r & "else "
                End Select
                s = "B"
                Select Case s
                    Case "a" To "c": r = r & "range "
                    Case Else: r = r & "else "
                End Select
                s = "A"
                Select Case s
                    Case Is < "b": r = r & "is-lt "
                    Case Else: r = r & "else "
                End Select
                s = "abc"
                Select Case s
                    Case Is = "ABC": r = r & "is-eq "
                    Case Else: r = r & "else "
                End Select
                s = "_"
                Select Case s
                    Case "A" To "z": r = r & "range-A-z "
                    Case Else: r = r & "else "
                End Select
                SelectCaseRowsT = Trim$(r)
            End Function

            Public Function CollectionKeyT() As Variant
                Dim c As New Collection
                c.Add 1, "abc"
                On Error Resume Next
                c.Add 2, "ABC"
                CollectionKeyT = Err.Number
            End Function

            Public Function R0T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("abc" = "ABC")
                If Err.Number <> 0 Then
                    R0T = "err " & Err.Number & " " & Err.Description
                Else
                    R0T = Show(r)
                End If
            End Function

            Public Function R1T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("abc" <> "ABC")
                If Err.Number <> 0 Then
                    R1T = "err " & Err.Number & " " & Err.Description
                Else
                    R1T = Show(r)
                End If
            End Function

            Public Function R2T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" < "B")
                If Err.Number <> 0 Then
                    R2T = "err " & Err.Number & " " & Err.Description
                Else
                    R2T = Show(r)
                End If
            End Function

            Public Function R3T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("B" < "a")
                If Err.Number <> 0 Then
                    R3T = "err " & Err.Number & " " & Err.Description
                Else
                    R3T = Show(r)
                End If
            End Function

            Public Function R4T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" <= "A")
                If Err.Number <> 0 Then
                    R4T = "err " & Err.Number & " " & Err.Description
                Else
                    R4T = Show(r)
                End If
            End Function

            Public Function R5T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" < "A")
                If Err.Number <> 0 Then
                    R5T = "err " & Err.Number & " " & Err.Description
                Else
                    R5T = Show(r)
                End If
            End Function

            Public Function R6T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" > "A")
                If Err.Number <> 0 Then
                    R6T = "err " & Err.Number & " " & Err.Description
                Else
                    R6T = Show(r)
                End If
            End Function

            Public Function R7T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("_" < "a")
                If Err.Number <> 0 Then
                    R7T = "err " & Err.Number & " " & Err.Description
                Else
                    R7T = Show(r)
                End If
            End Function

            Public Function R8T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("Z" < "_")
                If Err.Number <> 0 Then
                    R8T = "err " & Err.Number & " " & Err.Description
                Else
                    R8T = Show(r)
                End If
            End Function

            Public Function R9T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("[" < "a")
                If Err.Number <> 0 Then
                    R9T = "err " & Err.Number & " " & Err.Description
                Else
                    R9T = Show(r)
                End If
            End Function

            Public Function R10T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("1" < "a")
                If Err.Number <> 0 Then
                    R10T = "err " & Err.Number & " " & Err.Description
                Else
                    R10T = Show(r)
                End If
            End Function

            Public Function R11T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (" " < "a")
                If Err.Number <> 0 Then
                    R11T = "err " & Err.Number & " " & Err.Description
                Else
                    R11T = Show(r)
                End If
            End Function

            Public Function R12T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" = "a ")
                If Err.Number <> 0 Then
                    R12T = "err " & Err.Number & " " & Err.Description
                Else
                    R12T = Show(r)
                End If
            End Function

            Public Function R13T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(233) = "e")
                If Err.Number <> 0 Then
                    R13T = "err " & Err.Number & " " & Err.Description
                Else
                    R13T = Show(r)
                End If
            End Function

            Public Function R14T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("e" < Chr(233))
                If Err.Number <> 0 Then
                    R14T = "err " & Err.Number & " " & Err.Description
                Else
                    R14T = Show(r)
                End If
            End Function

            Public Function R15T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(233) < "f")
                If Err.Number <> 0 Then
                    R15T = "err " & Err.Number & " " & Err.Description
                Else
                    R15T = Show(r)
                End If
            End Function

            Public Function R16T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(201) < "f")
                If Err.Number <> 0 Then
                    R16T = "err " & Err.Number & " " & Err.Description
                Else
                    R16T = Show(r)
                End If
            End Function

            Public Function R17T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(223) = "ss")
                If Err.Number <> 0 Then
                    R17T = "err " & Err.Number & " " & Err.Description
                Else
                    R17T = Show(r)
                End If
            End Function

            Public Function R18T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Chr(230) = "ae")
                If Err.Number <> 0 Then
                    R18T = "err " & Err.Number & " " & Err.Description
                Else
                    R18T = Show(r)
                End If
            End Function

            Public Function R19T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("coop" = "co-op")
                If Err.Number <> 0 Then
                    R19T = "err " & Err.Number & " " & Err.Description
                Else
                    R19T = Show(r)
                End If
            End Function

            Public Function R20T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("co-op" < "coop")
                If Err.Number <> 0 Then
                    R20T = "err " & Err.Number & " " & Err.Description
                Else
                    R20T = Show(r)
                End If
            End Function

            Public Function R21T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("its" < "its")
                If Err.Number <> 0 Then
                    R21T = "err " & Err.Number & " " & Err.Description
                Else
                    R21T = Show(r)
                End If
            End Function

            Public Function R22T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (sv = su)
                If Err.Number <> 0 Then
                    R22T = "err " & Err.Number & " " & Err.Description
                Else
                    R22T = Show(r)
                End If
            End Function

            Public Function R23T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (vv = vu)
                If Err.Number <> 0 Then
                    R23T = "err " & Err.Number & " " & Err.Description
                Else
                    R23T = Show(r)
                End If
            End Function

            Public Function R24T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (vv = su)
                If Err.Number <> 0 Then
                    R24T = "err " & Err.Number & " " & Err.Description
                Else
                    R24T = Show(r)
                End If
            End Function

            Public Function R25T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (su < sv)
                If Err.Number <> 0 Then
                    R25T = "err " & Err.Number & " " & Err.Description
                Else
                    R25T = Show(r)
                End If
            End Function

            Public Function R26T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("a" = Null)
                If Err.Number <> 0 Then
                    R26T = "err " & Err.Number & " " & Err.Description
                Else
                    R26T = Show(r)
                End If
            End Function

            Public Function R27T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("10" < 9)
                If Err.Number <> 0 Then
                    R27T = "err " & Err.Number & " " & Err.Description
                Else
                    R27T = Show(r)
                End If
            End Function

            Public Function R28T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("5" = 5)
                If Err.Number <> 0 Then
                    R28T = "err " & Err.Number & " " & Err.Description
                Else
                    R28T = Show(r)
                End If
            End Function

            Public Function R29T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (BinaryEquals("abc", "ABC"))
                If Err.Number <> 0 Then
                    R29T = "err " & Err.Number & " " & Err.Description
                Else
                    R29T = Show(r)
                End If
            End Function

            Public Function R30T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStr("ABC", "b"))
                If Err.Number <> 0 Then
                    R30T = "err " & Err.Number & " " & Err.Description
                Else
                    R30T = Show(r)
                End If
            End Function

            Public Function R31T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStr(1, "ABC", "b"))
                If Err.Number <> 0 Then
                    R31T = "err " & Err.Number & " " & Err.Description
                Else
                    R31T = Show(r)
                End If
            End Function

            Public Function R32T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStr(1, "ABC", "b", 0))
                If Err.Number <> 0 Then
                    R32T = "err " & Err.Number & " " & Err.Description
                Else
                    R32T = Show(r)
                End If
            End Function

            Public Function R33T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (InStrRev("ABCabc", "B"))
                If Err.Number <> 0 Then
                    R33T = "err " & Err.Number & " " & Err.Description
                Else
                    R33T = Show(r)
                End If
            End Function

            Public Function R34T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Replace("ABCabc", "b", "x"))
                If Err.Number <> 0 Then
                    R34T = "err " & Err.Number & " " & Err.Description
                Else
                    R34T = Show(r)
                End If
            End Function

            Public Function R35T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Split("aXbxc", "x"))
                If Err.Number <> 0 Then
                    R35T = "err " & Err.Number & " " & Err.Description
                Else
                    R35T = Show(r)
                End If
            End Function

            Public Function R36T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (Filter(Array("A", "b", "a"), "a"))
                If Err.Number <> 0 Then
                    R36T = "err " & Err.Number & " " & Err.Description
                Else
                    R36T = Show(r)
                End If
            End Function

            Public Function R37T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (StrComp("a", "A"))
                If Err.Number <> 0 Then
                    R37T = "err " & Err.Number & " " & Err.Description
                Else
                    R37T = Show(r)
                End If
            End Function

            Public Function R38T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = ("ABC" Like "a*")
                If Err.Number <> 0 Then
                    R38T = "err " & Err.Number & " " & Err.Description
                Else
                    R38T = Show(r)
                End If
            End Function

            Public Function R39T() As String
                Dim r As Variant
                On Error Resume Next
                Err.Clear
                sv = "abc": su = "ABC": vv = "abc": vu = "ABC"
                r = (CollectionKeyT())
                If Err.Number <> 0 Then
                    R39T = "err " & Err.Number & " " & Err.Description
                Else
                    R39T = Show(r)
                End If
            End Function

            """""""));

        AssertDebugLog(Lines("""
            eq-case: B=Boolean False | T=Boolean True
            ne-case: B=Boolean True | T=Boolean False
            lt-a-B: B=Boolean False | T=Boolean True
            lt-B-a: B=Boolean True | T=Boolean False
            le-a-A: B=Boolean False | T=Boolean True
            lt-a-A: B=Boolean False | T=Boolean False
            gt-a-A: B=Boolean True | T=Boolean False
            lt-us-a: B=Boolean True | T=Boolean True
            lt-Z-us: B=Boolean True | T=Boolean False
            lt-br-a: B=Boolean True | T=Boolean True
            lt-1-a: B=Boolean True | T=Boolean True
            lt-sp-a: B=Boolean True | T=Boolean True
            eq-trailing: B=Boolean False | T=Boolean False
            eq-e-acute-e: B=Boolean False | T=Boolean False
            lt-e-e-acute: B=Boolean True | T=Boolean True
            lt-e-acute-f: B=Boolean False | T=Boolean True
            lt-E-acute-f: B=Boolean False | T=Boolean True
            eq-sz-ss: B=Boolean False | T=Boolean True
            eq-ae: B=Boolean False | T=Boolean True
            eq-hyphen: B=Boolean False | T=Boolean False
            lt-hyphen: B=Boolean True | T=Boolean False
            lt-apos: B=Boolean False | T=Boolean False
            eq-vars: B=Boolean False | T=Boolean True
            eq-variants: B=Boolean False | T=Boolean True
            eq-var-str: B=Boolean False | T=Boolean True
            lt-vars: B=Boolean True | T=Boolean False
            eq-null: B=Null | T=Null
            cross-module: B=Boolean False | T=Boolean False
            instr-explicit-0: B=Long 0 | T=Long 0
            like: B=Boolean False | T=Boolean True
            collection-key: B=err 457 This key is already associated with an element of this collection | T=err 457 This key is already associated with an element of this collection
            select-case: B=else else is-lt else range-A-z | T=case-A range is-lt is-eq else
            """));
    }

    // InStr, InStrRev and Filter take a match that starts or ends inside an expansion; Replace and Split take only
    // whole characters. Chr(0), the soft hyphen and the word-sort characters match only themselves.
    [Fact]
    public async Task A_text_search_matches_case_folded_letters_and_expansions_and_nothing_else()
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

            Public Function Parts(v As Variant) As String
                Dim i As Long, r As String
                For i = LBound(v) To UBound(v)
                    r = r & Codes(v(i))
                Next
                Parts = (UBound(v) - LBound(v) + 1) & " " & r
            End Function

            Sub R0()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, Chr(223), "s", 1)
                If Err.Number <> 0 Then L "instr-half-sz: err " & Err.Number Else L "instr-half-sz: " & r
            End Sub

            Sub R1()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "stra" & Chr(223) & "e", "ss", 1)
                If Err.Number <> 0 Then L "instr-sz-ss: err " & Err.Number Else L "instr-sz-ss: " & r
            End Sub

            Sub R2()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "stra" & Chr(223) & "e", "es", 1)
                If Err.Number <> 0 Then L "instr-sz-es: err " & Err.Number Else L "instr-sz-es: " & r
            End Sub

            Sub R3()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "stra" & Chr(223) & "e", "sse", 1)
                If Err.Number <> 0 Then L "instr-sz-sse: err " & Err.Number Else L "instr-sz-sse: " & r
            End Sub

            Sub R4()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "stra" & Chr(223) & "e", "as", 1)
                If Err.Number <> 0 Then L "instr-sz-as: err " & Err.Number Else L "instr-sz-as: " & r
            End Sub

            Sub R5()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "stra" & Chr(223) & "e", "ass", 1)
                If Err.Number <> 0 Then L "instr-sz-ass: err " & Err.Number Else L "instr-sz-ass: " & r
            End Sub

            Sub R6()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "strasse", Chr(223), 1)
                If Err.Number <> 0 Then L "instr-ss-sz: err " & Err.Number Else L "instr-ss-sz: " & r
            End Sub

            Sub R7()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "strasse", Chr(223) & "e", 1)
                If Err.Number <> 0 Then L "instr-ss-sze: err " & Err.Number Else L "instr-ss-sze: " & r
            End Sub

            Sub R8()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "asssb", Chr(223), 1)
                If Err.Number <> 0 Then L "instr-sss-sz: err " & Err.Number Else L "instr-sss-sz: " & r
            End Sub

            Sub R9()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(2, "stra" & Chr(223) & "e", "ss", 1)
                If Err.Number <> 0 Then L "instr-sz-from-2: err " & Err.Number Else L "instr-sz-from-2: " & r
            End Sub

            Sub R10()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(5, "stra" & Chr(223) & "e", "s", 1)
                If Err.Number <> 0 Then L "instr-sz-from-5: err " & Err.Number Else L "instr-sz-from-5: " & r
            End Sub

            Sub R11()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "x" & Chr(230) & "y", "a", 1)
                If Err.Number <> 0 Then L "instr-ae-half: err " & Err.Number Else L "instr-ae-half: " & r
            End Sub

            Sub R12()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "x" & Chr(230) & "y", "ey", 1)
                If Err.Number <> 0 Then L "instr-ae-e: err " & Err.Number Else L "instr-ae-e: " & r
            End Sub

            Sub R13()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "aeb", Chr(230), 1)
                If Err.Number <> 0 Then L "instr-ae-whole: err " & Err.Number Else L "instr-ae-whole: " & r
            End Sub

            Sub R14()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a" & Chr(0) & "bc", "bc", 1)
                If Err.Number <> 0 Then L "instr-nul-skip: err " & Err.Number Else L "instr-nul-skip: " & r
            End Sub

            Sub R15()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a" & Chr(0) & "bc", Chr(0) & "bc", 1)
                If Err.Number <> 0 Then L "instr-nul-lead-needle: err " & Err.Number Else L "instr-nul-lead-needle: " & r
            End Sub

            Sub R16()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "abc", "a" & Chr(0) & "b", 1)
                If Err.Number <> 0 Then L "instr-nul-in-needle: err " & Err.Number Else L "instr-nul-in-needle: " & r
            End Sub

            Sub R17()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a" & Chr(0) & "bc", "ab", 1)
                If Err.Number <> 0 Then L "instr-nul-spans: err " & Err.Number Else L "instr-nul-spans: " & r
            End Sub

            Sub R18()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a" & Chr(173) & "bc", "ab", 1)
                If Err.Number <> 0 Then L "instr-shy-spans: err " & Err.Number Else L "instr-shy-spans: " & r
            End Sub

            Sub R19()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, Chr(173) & "abc", "ab", 1)
                If Err.Number <> 0 Then L "instr-shy-lead: err " & Err.Number Else L "instr-shy-lead: " & r
            End Sub

            Sub R20()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "abc", Chr(0), 1)
                If Err.Number <> 0 Then L "instr-nul-needle: err " & Err.Number Else L "instr-nul-needle: " & r
            End Sub

            Sub R21()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a" & Chr(0), Chr(0), 1)
                If Err.Number <> 0 Then L "instr-nul-needle-has: err " & Err.Number Else L "instr-nul-needle-has: " & r
            End Sub

            Sub R22()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "xyz", "", 1)
                If Err.Number <> 0 Then L "instr-empty-needle: err " & Err.Number Else L "instr-empty-needle: " & r
            End Sub

            Sub R23()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(3, "xyz", "", 1)
                If Err.Number <> 0 Then L "instr-empty-needle-3: err " & Err.Number Else L "instr-empty-needle-3: " & r
            End Sub

            Sub R24()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "co-op", "coop", 1)
                If Err.Number <> 0 Then L "instr-hy-coop: err " & Err.Number Else L "instr-hy-coop: " & r
            End Sub

            Sub R25()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "coop", "co-op", 1)
                If Err.Number <> 0 Then L "instr-coop-hy: err " & Err.Number Else L "instr-coop-hy: " & r
            End Sub

            Sub R26()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "co-op", "op", 1)
                If Err.Number <> 0 Then L "instr-hy-op: err " & Err.Number Else L "instr-hy-op: " & r
            End Sub

            Sub R27()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "co-op", "-op", 1)
                If Err.Number <> 0 Then L "instr-hy-dash-op: err " & Err.Number Else L "instr-hy-dash-op: " & r
            End Sub

            Sub R28()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "co-op", "o-o", 1)
                If Err.Number <> 0 Then L "instr-hy-o-o: err " & Err.Number Else L "instr-hy-o-o: " & r
            End Sub

            Sub R29()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "co-op", "-", 1)
                If Err.Number <> 0 Then L "instr-hy-alone: err " & Err.Number Else L "instr-hy-alone: " & r
            End Sub

            Sub R30()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "coop", "-", 1)
                If Err.Number <> 0 Then L "instr-hy-in-empty: err " & Err.Number Else L "instr-hy-in-empty: " & r
            End Sub

            Sub R31()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "x" & Chr(1) & "y", "xy", 1)
                If Err.Number <> 0 Then L "instr-ctl-spans: err " & Err.Number Else L "instr-ctl-spans: " & r
            End Sub

            Sub R32()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "xy", "x" & Chr(1) & "y", 1)
                If Err.Number <> 0 Then L "instr-ctl-needle: err " & Err.Number Else L "instr-ctl-needle: " & r
            End Sub

            Sub R33()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a-", "a", 1)
                If Err.Number <> 0 Then L "instr-a-dash: err " & Err.Number Else L "instr-a-dash: " & r
            End Sub

            Sub R34()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "-a", "a", 1)
                If Err.Number <> 0 Then L "instr-dash-a: err " & Err.Number Else L "instr-dash-a: " & r
            End Sub

            Sub R35()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, Chr(201), "e", 1)
                If Err.Number <> 0 Then L "instr-accent: err " & Err.Number Else L "instr-accent: " & r
            End Sub

            Sub R36()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "AbC", "bc", 1)
                If Err.Number <> 0 Then L "instr-case: err " & Err.Number Else L "instr-case: " & r
            End Sub

            Sub R37()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("stra" & Chr(223) & "e", "s", -1, 1)
                If Err.Number <> 0 Then L "instrrev-half-sz: err " & Err.Number Else L "instrrev-half-sz: " & r
            End Sub

            Sub R38()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("stra" & Chr(223) & "e", "ss", -1, 1)
                If Err.Number <> 0 Then L "instrrev-sz-ss: err " & Err.Number Else L "instrrev-sz-ss: " & r
            End Sub

            Sub R39()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("a" & Chr(0) & "b", "b", -1, 1)
                If Err.Number <> 0 Then L "instrrev-nul: err " & Err.Number Else L "instrrev-nul: " & r
            End Sub

            Sub R40()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("co-op", "coop", -1, 1)
                If Err.Number <> 0 Then L "instrrev-hy: err " & Err.Number Else L "instrrev-hy: " & r
            End Sub

            Sub R41()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("stra" & Chr(223) & "e", "s", "X", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-sz-s: err " & Err.Number Else L "replace-sz-s: " & r
            End Sub

            Sub R42()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("stra" & Chr(223) & "e", "ss", "X", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-sz-ss: err " & Err.Number Else L "replace-sz-ss: " & r
            End Sub

            Sub R43()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("strasse", Chr(223), "X", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-ss-sz: err " & Err.Number Else L "replace-ss-sz: " & r
            End Sub

            Sub R44()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("a" & Chr(0) & "bc", "ab", "X", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-nul-ab: err " & Err.Number Else L "replace-nul-ab: " & r
            End Sub

            Sub R45()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("a" & Chr(0) & "bc", "b", "X", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-nul-b: err " & Err.Number Else L "replace-nul-b: " & r
            End Sub

            Sub R46()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("co-op", "coop", "X", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-hy-coop: err " & Err.Number Else L "replace-hy-coop: " & r
            End Sub

            Sub R47()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("co-op", "op", "X", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-hy-op: err " & Err.Number Else L "replace-hy-op: " & r
            End Sub

            Sub R48()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("a" & Chr(223) & "b", "s", -1, 1))
                If Err.Number <> 0 Then L "split-sz-s: err " & Err.Number Else L "split-sz-s: " & r
            End Sub

            Sub R49()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("a" & Chr(0) & "b" & Chr(0) & "c", Chr(0), -1, 1))
                If Err.Number <> 0 Then L "split-nul: err " & Err.Number Else L "split-nul: " & r
            End Sub

            Sub R50()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("co-op", "coop", -1, 1))
                If Err.Number <> 0 Then L "split-hy: err " & Err.Number Else L "split-hy: " & r
            End Sub

            Sub R51()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = UBound(Filter(Array("co-op"), "coop", True, 1))
                If Err.Number <> 0 Then L "filter-hy: err " & Err.Number Else L "filter-hy: " & r
            End Sub

            Sub R52()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = UBound(Filter(Array("a" & Chr(0) & "b"), "ab", True, 1))
                If Err.Number <> 0 Then L "filter-nul: err " & Err.Number Else L "filter-nul: " & r
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
                R28
                R29
                R30
                R31
                R32
                R33
                R34
                R35
                R36
                R37
                R38
                R39
                R40
                R41
                R42
                R43
                R44
                R45
                R46
                R47
                R48
                R49
                R50
                R51
                R52
            End Sub

            Main

            """"""");

        AssertDebugLog(Lines("""
            instr-half-sz: 1
            instr-sz-ss: 5
            instr-sz-es: 0
            instr-sz-sse: 5
            instr-sz-as: 4
            instr-sz-ass: 4
            instr-ss-sz: 5
            instr-ss-sze: 5
            instr-sss-sz: 2
            instr-sz-from-2: 5
            instr-sz-from-5: 5
            instr-ae-half: 2
            instr-ae-e: 2
            instr-ae-whole: 1
            instr-nul-skip: 3
            instr-nul-lead-needle: 2
            instr-nul-in-needle: 0
            instr-nul-spans: 0
            instr-shy-spans: 0
            instr-shy-lead: 2
            instr-nul-needle: 0
            instr-nul-needle-has: 2
            instr-empty-needle: 1
            instr-empty-needle-3: 3
            instr-hy-coop: 0
            instr-coop-hy: 0
            instr-hy-op: 4
            instr-hy-dash-op: 3
            instr-hy-o-o: 2
            instr-hy-alone: 3
            instr-hy-in-empty: 0
            instr-ctl-spans: 0
            instr-ctl-needle: 0
            instr-a-dash: 1
            instr-dash-a: 2
            instr-accent: 0
            instr-case: 2
            instrrev-half-sz: 5
            instrrev-sz-ss: 5
            instrrev-nul: 3
            instrrev-hy: 0
            replace-sz-s: [88.116.114.97.223.101]
            replace-sz-ss: [115.116.114.97.88.101]
            replace-ss-sz: [115.116.114.97.88.101]
            replace-nul-ab: [97.0.98.99]
            replace-nul-b: [97.0.88.99]
            replace-hy-coop: [99.111.45.111.112]
            replace-hy-op: [99.111.45.88]
            split-sz-s: 1 [97.223.98]
            split-nul: 3 [97][98][99]
            split-hy: 1 [99.111.45.111.112]
            filter-hy: -1
            filter-nul: -1
            """));
    }

    // The edges: overlapping expansions, superscript digits, the two dashes, and a Filter on half an expansion.
    [Fact]
    public async Task A_text_search_keeps_to_whole_characters_where_it_replaces_or_splits()
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

            Public Function Parts(v As Variant) As String
                Dim i As Long, r As String
                For i = LBound(v) To UBound(v)
                    r = r & Codes(v(i))
                Next
                Parts = (UBound(v) - LBound(v) + 1) & " " & r
            End Function

            Sub R0()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = UBound(Filter(Array(Chr(223)), "s", True, 1))
                If Err.Number <> 0 Then L "filter-half-sz: err " & Err.Number Else L "filter-half-sz: " & r
            End Sub

            Sub R1()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = UBound(Filter(Array("x" & Chr(230) & "y"), "ey", True, 1))
                If Err.Number <> 0 Then L "filter-ae-ey: err " & Err.Number Else L "filter-ae-ey: " & r
            End Sub

            Sub R2()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a" & Chr(173) & "b", "a" & Chr(0) & "b", 1)
                If Err.Number <> 0 Then L "instr-shy-vs-nul: err " & Err.Number Else L "instr-shy-vs-nul: " & r
            End Sub

            Sub R3()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "x" & Chr(185), "1", 1)
                If Err.Number <> 0 Then L "instr-sup1-1: err " & Err.Number Else L "instr-sup1-1: " & r
            End Sub

            Sub R4()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "x1", Chr(185), 1)
                If Err.Number <> 0 Then L "instr-1-sup1: err " & Err.Number Else L "instr-1-sup1: " & r
            End Sub

            Sub R5()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a" & Chr(150) & "b", "a" & Chr(151) & "b", 1)
                If Err.Number <> 0 Then L "instr-en-em: err " & Err.Number Else L "instr-en-em: " & r
            End Sub

            Sub R6()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "a'b", "a" & Chr(146) & "b", 1)
                If Err.Number <> 0 Then L "instr-apos-rsquo: err " & Err.Number Else L "instr-apos-rsquo: " & r
            End Sub

            Sub R7()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("x" & Chr(230) & "y", "ey", "Z", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-ae-ey: err " & Err.Number Else L "replace-ae-ey: " & r
            End Sub

            Sub R8()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("x" & Chr(230) & "y", "a", "Z", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-ae-a: err " & Err.Number Else L "replace-ae-a: " & r
            End Sub

            Sub R9()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("x" & Chr(230) & "y", "ey", -1, 1))
                If Err.Number <> 0 Then L "split-ae-ey: err " & Err.Number Else L "split-ae-ey: " & r
            End Sub

            Sub R10()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, Chr(223) & "s", "sss", 1)
                If Err.Number <> 0 Then L "instr-szs-sss: err " & Err.Number Else L "instr-szs-sss: " & r
            End Sub

            Sub R11()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "s" & Chr(223), "sss", 1)
                If Err.Number <> 0 Then L "instr-ssz-sss: err " & Err.Number Else L "instr-ssz-sss: " & r
            End Sub

            Sub R12()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("s" & Chr(223), "sss", "Z", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-ssz-sss: err " & Err.Number Else L "replace-ssz-sss: " & r
            End Sub

            Sub R13()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace(Chr(223) & "s", "ss", "Z", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-szs-ss: err " & Err.Number Else L "replace-szs-ss: " & r
            End Sub

            Sub R14()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace("s" & Chr(223), "ss", "Z", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-ssz-ss: err " & Err.Number Else L "replace-ssz-ss: " & r
            End Sub

            Sub R15()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "s" & Chr(223), "ss", 1)
                If Err.Number <> 0 Then L "instr-ssz-ss: err " & Err.Number Else L "instr-ssz-ss: " & r
            End Sub

            Sub R16()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev(Chr(223) & "s", "ss", -1, 1)
                If Err.Number <> 0 Then L "instrrev-szs-ss: err " & Err.Number Else L "instrrev-szs-ss: " & r
            End Sub

            Sub R17()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("s" & Chr(223), "ss", -1, 1)
                If Err.Number <> 0 Then L "instrrev-ssz-ss: err " & Err.Number Else L "instrrev-ssz-ss: " & r
            End Sub

            Sub R18()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, Chr(198), "E", 1)
                If Err.Number <> 0 Then L "instr-AE-E: err " & Err.Number Else L "instr-AE-E: " & r
            End Sub

            Sub R19()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = UBound(Filter(Array("a" & Chr(0)), Chr(0), True, 1))
                If Err.Number <> 0 Then L "filter-nul: err " & Err.Number Else L "filter-nul: " & r
            End Sub

            Sub R20()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "x" & Chr(223), Chr(223), 1)
                If Err.Number <> 0 Then L "instr-sz-sz: err " & Err.Number Else L "instr-sz-sz: " & r
            End Sub

            Sub R21()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, "xs", Chr(223), 1)
                If Err.Number <> 0 Then L "instr-sz-half-needle: err " & Err.Number Else L "instr-sz-half-needle: " & r
            End Sub

            Sub R22()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Codes(Replace(Chr(223) & Chr(223), "s", "Z", 1, -1, 1))
                If Err.Number <> 0 Then L "replace-count-sz: err " & Err.Number Else L "replace-count-sz: " & r
            End Sub

            Sub R23()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(1, Chr(230), "a" & Chr(233), 1)
                If Err.Number <> 0 Then L "instr-accent-in-expansion: err " & Err.Number Else L "instr-accent-in-expansion: " & r
            End Sub

            Sub R24()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(5, "abc", "", 1)
                If Err.Number <> 0 Then L "instr-empty-past: err " & Err.Number Else L "instr-empty-past: " & r
            End Sub

            Sub R25()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStr(4, "abc", "", 1)
                If Err.Number <> 0 Then L "instr-empty-end: err " & Err.Number Else L "instr-empty-end: " & r
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
            End Sub

            Main

            """"""");

        AssertDebugLog(Lines("""
            filter-half-sz: 0
            filter-ae-ey: 0
            instr-shy-vs-nul: 0
            instr-sup1-1: 0
            instr-1-sup1: 0
            instr-en-em: 0
            instr-apos-rsquo: 0
            replace-ae-ey: [120.230.121]
            replace-ae-a: [120.230.121]
            split-ae-ey: 1 [120.230.121]
            instr-szs-sss: 1
            instr-ssz-sss: 1
            replace-ssz-sss: [90]
            replace-szs-ss: [90.115]
            replace-ssz-ss: [115.90]
            instr-ssz-ss: 1
            instrrev-szs-ss: 1
            instrrev-ssz-ss: 2
            instr-AE-E: 1
            filter-nul: 0
            instr-sz-sz: 2
            instr-sz-half-needle: 0
            replace-count-sz: [223.223]
            instr-accent-in-expansion: 0
            instr-empty-past: 5
            instr-empty-end: 4
            """));
    }

    // Select Case with a Variant selector, a Variant Case value, a Variant function on either side and an expression
    // selector, in a Binary module and a Text module; and Split and InStrRev as text where binary differs, where a
    // match is shorter than the delimiter, and with a start that falls inside an expansion.
    [Fact]
    public async Task A_Variant_selector_or_Case_value_and_a_text_Split_or_InStrRev_with_a_start()
    {
        await RunModules("""""""
            Option Compare Binary
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

            Public Function Parts(v As Variant) As String
                Dim i As Long, r As String
                For i = LBound(v) To UBound(v)
                    r = r & Codes(v(i))
                Next
                Parts = (UBound(v) - LBound(v) + 1) & " " & r
            End Function


            Public Function VarAB() As Variant
                VarAB = "A"
            End Function

            Public Function VarLowB() As Variant
                VarLowB = "a"
            End Function

            Public Function CaseRowsB() As String
                Dim v As Variant, w As Variant, s As String, r As String
                v = "a"
                Select Case v
                    Case "A": r = r & "var-lit "
                    Case Else: r = r & "- "
                End Select
                v = "B"
                Select Case v
                    Case "a" To "c": r = r & "var-to "
                    Case Else: r = r & "- "
                End Select
                v = "A"
                Select Case v
                    Case Is < "b": r = r & "var-is-lt "
                    Case Else: r = r & "- "
                End Select
                v = "abc"
                Select Case v
                    Case Is = "ABC": r = r & "var-is-eq "
                    Case Else: r = r & "- "
                End Select
                v = "a": w = "A"
                Select Case v
                    Case w: r = r & "var-var "
                    Case Else: r = r & "- "
                End Select
                v = "a"
                Select Case v
                    Case VarAB(): r = r & "var-fn "
                    Case Else: r = r & "- "
                End Select
                s = "a"
                Select Case s
                    Case VarAB(): r = r & "str-fn "
                    Case Else: r = r & "- "
                End Select
                Select Case VarLowB()
                    Case "A": r = r & "fn-lit "
                    Case Else: r = r & "- "
                End Select
                Select Case VarLowB()
                    Case VarAB(): r = r & "fn-fn "
                    Case Else: r = r & "- "
                End Select
                v = "a"
                Select Case v & ""
                    Case "A": r = r & "expr-lit "
                    Case Else: r = r & "- "
                End Select
                v = "B": w = "a"
                Select Case v
                    Case Is > w: r = r & "var-is-gt-var "
                    Case Else: r = r & "- "
                End Select
                v = "_": w = "A"
                Select Case v
                    Case Is < w: r = r & "var-us-lt-var "
                    Case Else: r = r & "- "
                End Select
                CaseRowsB = Trim$(r)
            End Function

            Sub S0()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("aXbxc", "x", -1, 1))
                If Err.Number <> 0 Then L "split-case: err " & Err.Number Else L "split-case: " & r
            End Sub

            Sub S1()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("aXbxc", "x", -1, 0))
                If Err.Number <> 0 Then L "split-case-bin: err " & Err.Number Else L "split-case-bin: " & r
            End Sub

            Sub S2()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("a" & Chr(223) & "b", "ss", -1, 1))
                If Err.Number <> 0 Then L "split-ss-sz: err " & Err.Number Else L "split-ss-sz: " & r
            End Sub

            Sub S3()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("assb", Chr(223), -1, 1))
                If Err.Number <> 0 Then L "split-sz-ss: err " & Err.Number Else L "split-sz-ss: " & r
            End Sub

            Sub S4()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = Parts(Split("aXbxc", "x", 2, 1))
                If Err.Number <> 0 Then L "split-limit: err " & Err.Number Else L "split-limit: " & r
            End Sub

            Sub S5()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("abcABC", "B", 4, 1)
                If Err.Number <> 0 Then L "instrrev-start: err " & Err.Number Else L "instrrev-start: " & r
            End Sub

            Sub S6()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("abcABC", "B", 4, 0)
                If Err.Number <> 0 Then L "instrrev-start-bin: err " & Err.Number Else L "instrrev-start-bin: " & r
            End Sub

            Sub S7()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("ss" & Chr(223), "ss", 2, 1)
                If Err.Number <> 0 Then L "instrrev-start-sz: err " & Err.Number Else L "instrrev-start-sz: " & r
            End Sub

            Sub S8()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("a" & Chr(223) & "b", "s", 2, 1)
                If Err.Number <> 0 Then L "instrrev-start-mid-sz: err " & Err.Number Else L "instrrev-start-mid-sz: " & r
            End Sub

            Sub S9()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("a" & Chr(223) & "b", "s", 1, 1)
                If Err.Number <> 0 Then L "instrrev-start-before-sz: err " & Err.Number Else L "instrrev-start-before-sz: " & r
            End Sub

            Sub S10()
                On Error Resume Next
                Dim r As Variant
                Err.Clear
                r = InStrRev("s" & Chr(223), "ss", 1, 1)
                If Err.Number <> 0 Then L "instrrev-start-end-sz: err " & Err.Number Else L "instrrev-start-end-sz: " & r
            End Sub

            Sub Main()
                S0
                S1
                S2
                S3
                S4
                S5
                S6
                S7
                S8
                S9
                S10
                L "select-case: B=" & CaseRowsB() & " | T=" & Module2.CaseRowsT()
            End Sub

            Main

            """"""",
            ("Module2", """""""
            Option Compare Text

            Public Function VarAT() As Variant
                VarAT = "A"
            End Function

            Public Function VarLowT() As Variant
                VarLowT = "a"
            End Function

            Public Function CaseRowsT() As String
                Dim v As Variant, w As Variant, s As String, r As String
                v = "a"
                Select Case v
                    Case "A": r = r & "var-lit "
                    Case Else: r = r & "- "
                End Select
                v = "B"
                Select Case v
                    Case "a" To "c": r = r & "var-to "
                    Case Else: r = r & "- "
                End Select
                v = "A"
                Select Case v
                    Case Is < "b": r = r & "var-is-lt "
                    Case Else: r = r & "- "
                End Select
                v = "abc"
                Select Case v
                    Case Is = "ABC": r = r & "var-is-eq "
                    Case Else: r = r & "- "
                End Select
                v = "a": w = "A"
                Select Case v
                    Case w: r = r & "var-var "
                    Case Else: r = r & "- "
                End Select
                v = "a"
                Select Case v
                    Case VarAT(): r = r & "var-fn "
                    Case Else: r = r & "- "
                End Select
                s = "a"
                Select Case s
                    Case VarAT(): r = r & "str-fn "
                    Case Else: r = r & "- "
                End Select
                Select Case VarLowT()
                    Case "A": r = r & "fn-lit "
                    Case Else: r = r & "- "
                End Select
                Select Case VarLowT()
                    Case VarAT(): r = r & "fn-fn "
                    Case Else: r = r & "- "
                End Select
                v = "a"
                Select Case v & ""
                    Case "A": r = r & "expr-lit "
                    Case Else: r = r & "- "
                End Select
                v = "B": w = "a"
                Select Case v
                    Case Is > w: r = r & "var-is-gt-var "
                    Case Else: r = r & "- "
                End Select
                v = "_": w = "A"
                Select Case v
                    Case Is < w: r = r & "var-us-lt-var "
                    Case Else: r = r & "- "
                End Select
                CaseRowsT = Trim$(r)
            End Function

            """""""));

        AssertDebugLog(Lines("""
            split-case: 3 [97][98][99]
            split-case-bin: 2 [97.88.98][99]
            split-ss-sz: 2 [97][98]
            split-sz-ss: 2 [97][98]
            split-limit: 2 [97][98.120.99]
            instrrev-start: 2
            instrrev-start-bin: 0
            instrrev-start-sz: 1
            instrrev-start-mid-sz: 2
            instrrev-start-before-sz: 0
            instrrev-start-end-sz: 0
            select-case: B=- - var-is-lt - - - - - - - - - | T=var-lit var-to var-is-lt var-is-eq var-var var-fn str-fn fn-lit fn-fn expr-lit var-is-gt-var var-us-lt-var
            """));
    }

    private static List<Vb6Value> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new Vb6Value(l)).ToList();
}
