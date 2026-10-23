// Generated from the vb6.exe probes for #1562: each program is the probe as it ran, printing where the probe logged,
// and each expected line is what vb6.exe logged, native and p-code alike, under en-GB.

using System.Globalization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// GetSetting, SaveSetting, DeleteSetting and GetAllSettings (#1562), as vb6.exe measured them, over the fixture's settings
/// in memory. The values vb6.exe read that another program wrote, of registry types other than text, are set first, as
/// the guest's registry was seeded.
/// </summary>
public class SettingsProgramTests : BaseVBTestFixture
{
    private static readonly CultureInfo EnGb = CultureInfo.GetCultureInfo("en-GB");

    // The first batch: missing names and defaults, every type a setting can be saved as, empty and Null names, a
    // backslash in each name, Unicode, GetAllSettings's shape and order, and every kind of delete.
    [Fact]
    public async Task Each_statement_answers_as_vb6_exe_does()
    {
        using (VbLocale.Use(EnGb))
            await Run("""""""

                Public Const A As String = "HexProbe1562"

                Public Sub L(ByVal s As String)
                    Debug.Print s
                End Sub

                Public Function Show(v As Variant) As String
                    Dim i As Long, r As String
                    If IsArray(v) Then
                        On Error Resume Next
                        r = "Array " & TypeName(v) & " (" & LBound(v, 1) & " To " & UBound(v, 1) & ", " & LBound(v, 2) & " To " & UBound(v, 2) & ")"
                        For i = LBound(v, 1) To UBound(v, 1)
                            r = r & " " & v(i, 0) & "=" & v(i, 1) & "[" & TypeName(v(i, 0)) & "," & TypeName(v(i, 1)) & "]"
                        Next
                        Show = r
                    ElseIf IsNull(v) Then
                        Show = "Null"
                    ElseIf IsEmpty(v) Then
                        Show = "Empty"
                    Else
                        Show = TypeName(v) & " [" & CStr(v) & "]"
                    End If
                End Function

                Public Sub Clean()
                    On Error Resume Next
                    Dim s As Variant
                    For Each s In Array("S1", "S2\Sub", "S2", "S3", "S4", "S5", "S6", "S7", "X\S")
                        DeleteSetting A, s
                    Next
                    DeleteSetting "1562", "2"
                End Sub

                Sub R0()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K")
                    If Err.Number <> 0 Then L "get-missing-app: err " & Err.Number & " " & Err.Description Else L "get-missing-app: " & Show(r)
                End Sub

                Sub R1()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K", "d")
                    If Err.Number <> 0 Then L "get-missing-app-default: err " & Err.Number & " " & Err.Description Else L "get-missing-app-default: " & Show(r)
                End Sub

                Sub R2()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K", 5)
                    If Err.Number <> 0 Then L "get-default-num: err " & Err.Number & " " & Err.Description Else L "get-default-num: " & Show(r)
                End Sub

                Sub R3()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K", 1.5)
                    If Err.Number <> 0 Then L "get-default-double: err " & Err.Number & " " & Err.Description Else L "get-default-double: " & Show(r)
                End Sub

                Sub R4()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K", Null)
                    If Err.Number <> 0 Then L "get-default-null: err " & Err.Number & " " & Err.Description Else L "get-default-null: " & Show(r)
                End Sub

                Sub R5()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K", Empty)
                    If Err.Number <> 0 Then L "get-default-empty: err " & Err.Number & " " & Err.Description Else L "get-default-empty: " & Show(r)
                End Sub

                Sub R6()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K", #1/2/2003#)
                    If Err.Number <> 0 Then L "get-default-date: err " & Err.Number & " " & Err.Description Else L "get-default-date: " & Show(r)
                End Sub

                Sub R7()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "NoSuchApp", "S", "K", True)
                    If Err.Number <> 0 Then L "get-default-bool: err " & Err.Number & " " & Err.Description Else L "get-default-bool: " & Show(r)
                End Sub

                Sub R8()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "K1", "v1"
                    If Err.Number <> 0 Then L "save-then-get: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "K1")
                    If Err.Number <> 0 Then L "save-then-get: err " & Err.Number & " " & Err.Description Else L "save-then-get: " & Show(r)
                End Sub

                Sub R9()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "S1", "NoKey", "d")
                    If Err.Number <> 0 Then L "get-missing-key: err " & Err.Number & " " & Err.Description Else L "get-missing-key: " & Show(r)
                End Sub

                Sub R10()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "NoSec", "K1", "d")
                    If Err.Number <> 0 Then L "get-missing-section: err " & Err.Number & " " & Err.Description Else L "get-missing-section: " & Show(r)
                End Sub

                Sub R11()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(UCase(A), "s1", "k1")
                    If Err.Number <> 0 Then L "get-case: err " & Err.Number & " " & Err.Description Else L "get-case: " & Show(r)
                End Sub

                Sub R12()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Int", 5
                    If Err.Number <> 0 Then L "save-int: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Int")
                    If Err.Number <> 0 Then L "save-int: err " & Err.Number & " " & Err.Description Else L "save-int: " & Show(r)
                End Sub

                Sub R13()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Dbl", 1.5
                    If Err.Number <> 0 Then L "save-double: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Dbl")
                    If Err.Number <> 0 Then L "save-double: err " & Err.Number & " " & Err.Description Else L "save-double: " & Show(r)
                End Sub

                Sub R14()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Bool", True
                    If Err.Number <> 0 Then L "save-bool: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Bool")
                    If Err.Number <> 0 Then L "save-bool: err " & Err.Number & " " & Err.Description Else L "save-bool: " & Show(r)
                End Sub

                Sub R15()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Date", #1/2/2003#
                    If Err.Number <> 0 Then L "save-date: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Date")
                    If Err.Number <> 0 Then L "save-date: err " & Err.Number & " " & Err.Description Else L "save-date: " & Show(r)
                End Sub

                Sub R16()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Nul", Null
                    If Err.Number <> 0 Then L "save-null: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Nul", "absent")
                    If Err.Number <> 0 Then L "save-null: err " & Err.Number & " " & Err.Description Else L "save-null: " & Show(r)
                End Sub

                Sub R17()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Emp", Empty
                    If Err.Number <> 0 Then L "save-empty: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Emp", "absent")
                    If Err.Number <> 0 Then L "save-empty: err " & Err.Number & " " & Err.Description Else L "save-empty: " & Show(r)
                End Sub

                Sub R18()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "EStr", ""
                    If Err.Number <> 0 Then L "save-empty-string: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "EStr", "absent")
                    If Err.Number <> 0 Then L "save-empty-string: err " & Err.Number & " " & Err.Description Else L "save-empty-string: " & Show(r)
                End Sub

                Sub R19()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting "", "S1", "K", "v"
                    If Err.Number <> 0 Then L "save-empty-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "save-empty-app: ran"
                End Sub

                Sub R20()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "", "K", "v"
                    If Err.Number <> 0 Then L "save-empty-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "save-empty-section: ran"
                End Sub

                Sub R21()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "", "v"
                    If Err.Number <> 0 Then L "save-empty-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "", "absent")
                    If Err.Number <> 0 Then L "save-empty-key: err " & Err.Number & " " & Err.Description Else L "save-empty-key: " & Show(r)
                End Sub

                Sub R22()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "a\b", "v"
                    If Err.Number <> 0 Then L "save-backslash-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "a\b", "absent")
                    If Err.Number <> 0 Then L "save-backslash-key: err " & Err.Number & " " & Err.Description Else L "save-backslash-key: " & Show(r)
                End Sub

                Sub R23()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S2\Sub", "K", "v"
                    If Err.Number <> 0 Then L "save-backslash-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S2\Sub", "K", "absent")
                    If Err.Number <> 0 Then L "save-backslash-section: err " & Err.Number & " " & Err.Description Else L "save-backslash-section: " & Show(r)
                End Sub

                Sub R24()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A, "S2")
                    If Err.Number <> 0 Then L "backslash-section-parent: err " & Err.Number & " " & Err.Description Else L "backslash-section-parent: " & Show(r)
                End Sub

                Sub R25()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "\X", "S", "K", "v"
                    If Err.Number <> 0 Then L "save-backslash-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "X\S", "K", "absent")
                    If Err.Number <> 0 Then L "save-backslash-app: err " & Err.Number & " " & Err.Description Else L "save-backslash-app: " & Show(r)
                End Sub

                Sub R26()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting 1562, 2, 3, 4
                    If Err.Number <> 0 Then L "save-numeric-names: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting("1562", "2", "3", "absent")
                    If Err.Number <> 0 Then L "save-numeric-names: err " & Err.Number & " " & Err.Description Else L "save-numeric-names: " & Show(r)
                End Sub

                Sub R27()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(1562, 2, 3)
                    If Err.Number <> 0 Then L "get-numeric-names: err " & Err.Number & " " & Err.Description Else L "get-numeric-names: " & Show(r)
                End Sub

                Sub R28()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting Null, "S", "K", "v"
                    If Err.Number <> 0 Then L "save-null-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "save-null-app: ran"
                End Sub

                Sub R29()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", Null, "v"
                    If Err.Number <> 0 Then L "save-null-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "save-null-key: ran"
                End Sub

                Sub R30()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(Null, "S", "K")
                    If Err.Number <> 0 Then L "get-null-app: err " & Err.Number & " " & Err.Description Else L "get-null-app: " & Show(r)
                End Sub

                Sub R31()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "S1", Null)
                    If Err.Number <> 0 Then L "get-null-key: err " & Err.Number & " " & Err.Description Else L "get-null-key: " & Show(r)
                End Sub

                Sub R32()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting("", "S", "K", "d")
                    If Err.Number <> 0 Then L "get-empty-app: err " & Err.Number & " " & Err.Description Else L "get-empty-app: " & Show(r)
                End Sub

                Sub R33()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "", "K", "d")
                    If Err.Number <> 0 Then L "get-empty-section: err " & Err.Number & " " & Err.Description Else L "get-empty-section: " & Show(r)
                End Sub

                Sub R34()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "K1", "v2"
                    If Err.Number <> 0 Then L "overwrite: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "K1")
                    If Err.Number <> 0 Then L "overwrite: err " & Err.Number & " " & Err.Description Else L "overwrite: " & Show(r)
                End Sub

                Sub R35()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Long", String(10000, "x")
                    If Err.Number <> 0 Then L "long-value: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = Len(GetSetting(A, "S1", "Long"))
                    If Err.Number <> 0 Then L "long-value: err " & Err.Number & " " & Err.Description Else L "long-value: " & Show(r)
                End Sub

                Sub R36()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Nul0", "a" & Chr(0) & "b"
                    If Err.Number <> 0 Then L "value-with-nul: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = Len(GetSetting(A, "S1", "Nul0"))
                    If Err.Number <> 0 Then L "value-with-nul: err " & Err.Number & " " & Err.Description Else L "value-with-nul: " & Show(r)
                End Sub

                Sub R37()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Uni", ChrW(&H4E00) & ChrW(&H101) & "x"
                    If Err.Number <> 0 Then L "value-unicode: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = AscW(Mid(GetSetting(A, "S1", "Uni"), 1, 1)) & "." & AscW(Mid(GetSetting(A, "S1", "Uni"), 2, 1)) & "." & Len(GetSetting(A, "S1", "Uni"))
                    If Err.Number <> 0 Then L "value-unicode: err " & Err.Number & " " & Err.Description Else L "value-unicode: " & Show(r)
                End Sub

                Sub R38()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", ChrW(&H4E00), "u"
                    If Err.Number <> 0 Then L "key-unicode: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", ChrW(&H4E00), "absent") & "|" & GetSetting(A, "S1", "?", "absent")
                    If Err.Number <> 0 Then L "key-unicode: err " & Err.Number & " " & Err.Description Else L "key-unicode: " & Show(r)
                End Sub

                Sub R39()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S3", "b", "2"
                    If Err.Number <> 0 Then L "getall-order: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    SaveSetting A, "S3", "a", "1"
                    If Err.Number <> 0 Then L "getall-order: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    SaveSetting A, "S3", "C", "3"
                    If Err.Number <> 0 Then L "getall-order: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetAllSettings(A, "S3")
                    If Err.Number <> 0 Then L "getall-order: err " & Err.Number & " " & Err.Description Else L "getall-order: " & Show(r)
                End Sub

                Sub R40()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S6", "z", "26"
                    If Err.Number <> 0 Then L "getall-order-2: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    SaveSetting A, "S6", "m", "13"
                    If Err.Number <> 0 Then L "getall-order-2: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    SaveSetting A, "S6", "a", "1"
                    If Err.Number <> 0 Then L "getall-order-2: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    SaveSetting A, "S6", "M", "x"
                    If Err.Number <> 0 Then L "getall-order-2: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetAllSettings(A, "S6")
                    If Err.Number <> 0 Then L "getall-order-2: err " & Err.Number & " " & Err.Description Else L "getall-order-2: " & Show(r)
                End Sub

                Sub R41()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S6", "z", "again"
                    If Err.Number <> 0 Then L "getall-after-overwrite: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetAllSettings(A, "S6")
                    If Err.Number <> 0 Then L "getall-after-overwrite: err " & Err.Number & " " & Err.Description Else L "getall-after-overwrite: " & Show(r)
                End Sub

                Sub R42()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A, "NoSec")
                    If Err.Number <> 0 Then L "getall-missing-section: err " & Err.Number & " " & Err.Description Else L "getall-missing-section: " & Show(r)
                End Sub

                Sub R43()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A & "NoSuchApp", "S")
                    If Err.Number <> 0 Then L "getall-missing-app: err " & Err.Number & " " & Err.Description Else L "getall-missing-app: " & Show(r)
                End Sub

                Sub R44()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(LCase(A), "s3")
                    If Err.Number <> 0 Then L "getall-case: err " & Err.Number & " " & Err.Description Else L "getall-case: " & Show(r)
                End Sub

                Sub R45()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S7", "e", ""
                    If Err.Number <> 0 Then L "getall-empty-value: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetAllSettings(A, "S7")
                    If Err.Number <> 0 Then L "getall-empty-value: err " & Err.Number & " " & Err.Description Else L "getall-empty-value: " & Show(r)
                End Sub

                Sub R46()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "S3", "b"
                    If Err.Number <> 0 Then L "delete-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S3", "b", "gone")
                    If Err.Number <> 0 Then L "delete-key: err " & Err.Number & " " & Err.Description Else L "delete-key: " & Show(r)
                End Sub

                Sub R47()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "S3", "nokey"
                    If Err.Number <> 0 Then L "delete-missing-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-missing-key: ran"
                End Sub

                Sub R48()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "NoSec"
                    If Err.Number <> 0 Then L "delete-missing-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-missing-section: ran"
                End Sub

                Sub R49()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "NoSec", "K"
                    If Err.Number <> 0 Then L "delete-missing-key-missing-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-missing-key-missing-section: ran"
                End Sub

                Sub R50()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A & "NoSuchApp", "S"
                    If Err.Number <> 0 Then L "delete-missing-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-missing-app: ran"
                End Sub

                Sub R51()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S4", "only", "1"
                    If Err.Number <> 0 Then L "delete-last-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    DeleteSetting A, "S4", "only"
                    If Err.Number <> 0 Then L "delete-last-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetAllSettings(A, "S4")
                    If Err.Number <> 0 Then L "delete-last-key: err " & Err.Number & " " & Err.Description Else L "delete-last-key: " & Show(r)
                End Sub

                Sub R52()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "S4"
                    If Err.Number <> 0 Then L "delete-section-after-last-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-section-after-last-key: ran"
                End Sub

                Sub R53()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "S3"
                    If Err.Number <> 0 Then L "delete-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S3", "a", "gone")
                    If Err.Number <> 0 Then L "delete-section: err " & Err.Number & " " & Err.Description Else L "delete-section: " & Show(r)
                End Sub

                Sub R54()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A, "S3")
                    If Err.Number <> 0 Then L "getall-deleted-section: err " & Err.Number & " " & Err.Description Else L "getall-deleted-section: " & Show(r)
                End Sub

                Sub R55()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "S2"
                    If Err.Number <> 0 Then L "delete-section-with-subkey: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S2\Sub", "K", "gone")
                    If Err.Number <> 0 Then L "delete-section-with-subkey: err " & Err.Number & " " & Err.Description Else L "delete-section-with-subkey: " & Show(r)
                End Sub

                Sub R56()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S5", "Key", "v"
                    If Err.Number <> 0 Then L "delete-case: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    DeleteSetting UCase(A), "s5", "KEY"
                    If Err.Number <> 0 Then L "delete-case: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S5", "Key", "gone")
                    If Err.Number <> 0 Then L "delete-case: err " & Err.Number & " " & Err.Description Else L "delete-case: " & Show(r)
                End Sub

                Sub R57()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, ""
                    If Err.Number <> 0 Then L "delete-empty-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-empty-section: ran"
                End Sub

                Sub R58()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "S1", ""
                    If Err.Number <> 0 Then L "delete-empty-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "", "gone")
                    If Err.Number <> 0 Then L "delete-empty-key: err " & Err.Number & " " & Err.Description Else L "delete-empty-key: " & Show(r)
                End Sub

                Sub R59()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, "S1", Null
                    If Err.Number <> 0 Then L "delete-null-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-null-key: ran"
                End Sub

                Sub R60()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A, Null
                    If Err.Number <> 0 Then L "delete-null-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-null-section: ran"
                End Sub

                Sub R61()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = TypeName(GetSetting(A, "S1", "K1"))
                    If Err.Number <> 0 Then L "get-type: err " & Err.Number & " " & Err.Description Else L "get-type: " & Show(r)
                End Sub

                Sub Main()
                    Clean
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
                    R53
                    R54
                    R55
                    R56
                    R57
                    R58
                    R59
                    R60
                    R61
                    Clean
                End Sub

                Main

                """"""");

        AssertDebugLog(Lines("""
            get-missing-app: String []
            get-missing-app-default: String [d]
            get-default-num: String [5]
            get-default-double: String [1.5]
            get-default-null: err 94 Invalid use of Null
            get-default-empty: String []
            get-default-date: String [02/01/2003]
            get-default-bool: String [True]
            save-then-get: String [v1]
            get-missing-key: String [d]
            get-missing-section: String [d]
            get-case: String [v1]
            save-int: String [5]
            save-double: String [1.5]
            save-bool: String [True]
            save-date: String [02/01/2003]
            save-null: setup err 94 Invalid use of Null
            save-null: String [absent]
            save-empty: String []
            save-empty-string: String []
            save-empty-app: setup err 5 Invalid procedure call or argument
            save-empty-app: ran
            save-empty-section: setup err 5 Invalid procedure call or argument
            save-empty-section: ran
            save-empty-key: setup err 5 Invalid procedure call or argument
            save-empty-key: err 5 Invalid procedure call or argument
            save-backslash-key: String [v]
            save-backslash-section: String [v]
            backslash-section-parent: Empty
            save-backslash-app: String [v]
            save-numeric-names: String [4]
            get-numeric-names: String [4]
            save-null-app: setup err 94 Invalid use of Null
            save-null-app: ran
            save-null-key: setup err 94 Invalid use of Null
            save-null-key: ran
            get-null-app: err 94 Invalid use of Null
            get-null-key: err 94 Invalid use of Null
            get-empty-app: err 5 Invalid procedure call or argument
            get-empty-section: err 5 Invalid procedure call or argument
            overwrite: String [v2]
            long-value: Long [10000]
            value-with-nul: Long [3]
            value-unicode: String [19968.257.3]
            key-unicode: String [u|absent]
            getall-order: Array String() (0 To 2, 0 To 1) b=2[String,String] a=1[String,String] C=3[String,String]
            getall-order-2: Array String() (0 To 2, 0 To 1) z=26[String,String] m=x[String,String] a=1[String,String]
            getall-after-overwrite: Array String() (0 To 2, 0 To 1) z=again[String,String] m=x[String,String] a=1[String,String]
            getall-missing-section: Empty
            getall-missing-app: Empty
            getall-case: Array String() (0 To 2, 0 To 1) b=2[String,String] a=1[String,String] C=3[String,String]
            getall-empty-value: Array String() (0 To 0, 0 To 1) e=[String,String]
            delete-key: String [gone]
            delete-missing-key: setup err 5 Invalid procedure call or argument
            delete-missing-key: ran
            delete-missing-section: setup err 5 Invalid procedure call or argument
            delete-missing-section: ran
            delete-missing-key-missing-section: setup err 5 Invalid procedure call or argument
            delete-missing-key-missing-section: ran
            delete-missing-app: setup err 5 Invalid procedure call or argument
            delete-missing-app: ran
            delete-last-key: Empty
            delete-section-after-last-key: ran
            delete-section: String [gone]
            getall-deleted-section: Empty
            delete-section-with-subkey: setup err 5 Invalid procedure call or argument
            delete-section-with-subkey: String [v]
            delete-case: String [gone]
            delete-empty-section: setup err 5 Invalid procedure call or argument
            delete-empty-section: ran
            delete-empty-key: setup err 5 Invalid procedure call or argument
            delete-empty-key: err 5 Invalid procedure call or argument
            delete-null-key: setup err 94 Invalid use of Null
            delete-null-key: ran
            delete-null-section: setup err 94 Invalid use of Null
            delete-null-section: ran
            get-type: String [String]
            """));
    }

    // The second batch: DeleteSetting with no section, sections that are paths, names that are not trimmed, and
    // values of registry types other than text, which are 5.
    [Fact]
    public async Task Deleting_a_program_path_like_sections_and_values_another_program_wrote()
    {
        var types = new[] { "HexProbe1562Types", "S" };
        SettingsStore.SetValue(types, "Sz", "abc");
        foreach (var name in new[] { "Dword", "Qword", "Expand", "Multi", "Binary" })
            SettingsStore.SetNonText(types, name);
        SettingsStore.SetValue(types, "", "dv");

        using (VbLocale.Use(EnGb))
            await Run("""""""

                Public Const A As String = "HexProbe1562"
                Public Const BS As String = "\"

                Public Sub L(ByVal s As String)
                    Debug.Print s
                End Sub

                Public Function Show(v As Variant) As String
                    Dim i As Long, r As String
                    If IsArray(v) Then
                        On Error Resume Next
                        r = "Array " & TypeName(v) & " (" & LBound(v, 1) & " To " & UBound(v, 1) & ", " & LBound(v, 2) & " To " & UBound(v, 2) & ")"
                        For i = LBound(v, 1) To UBound(v, 1)
                            r = r & " " & v(i, 0) & "=" & v(i, 1) & "[" & TypeName(v(i, 0)) & "," & TypeName(v(i, 1)) & "]"
                        Next
                        Show = r
                    ElseIf IsNull(v) Then
                        Show = "Null"
                    ElseIf IsEmpty(v) Then
                        Show = "Empty"
                    Else
                        Show = TypeName(v) & " [" & CStr(v) & "]"
                    End If
                End Function

                Public Sub Clean()
                    On Error Resume Next
                    Dim s As Variant
                    For Each s In Array("S1", "S2\Sub", "S2", "S3", "S4", "S5", "S6", "S7", "X\S")
                        DeleteSetting A, s
                    Next
                    DeleteSetting "1562", "2"
                End Sub

                Sub R0()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "T1", "k", "v"
                    If Err.Number <> 0 Then L "delete-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    SaveSetting A, "T2", "k", "v"
                    If Err.Number <> 0 Then L "delete-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    DeleteSetting A
                    If Err.Number <> 0 Then L "delete-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "T1", "k", "gone") & "|" & GetSetting(A, "T2", "k", "gone")
                    If Err.Number <> 0 Then L "delete-app: err " & Err.Number & " " & Err.Description Else L "delete-app: " & Show(r)
                End Sub

                Sub R1()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A
                    If Err.Number <> 0 Then L "delete-app-again: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-app-again: ran"
                End Sub

                Sub R2()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "B", "T", "k", "v"
                    If Err.Number <> 0 Then L "delete-app-with-empty-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    DeleteSetting A & "B", "T", "k"
                    If Err.Number <> 0 Then L "delete-app-with-empty-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    DeleteSetting A & "B"
                    If Err.Number <> 0 Then L "delete-app-with-empty-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetAllSettings(A & "B", "T")
                    If Err.Number <> 0 Then L "delete-app-with-empty-section: err " & Err.Number & " " & Err.Description Else L "delete-app-with-empty-section: " & Show(r)
                End Sub

                Sub R3()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "C", "T\U", "k", "v"
                    If Err.Number <> 0 Then L "delete-app-with-nested-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    DeleteSetting A & "C"
                    If Err.Number <> 0 Then L "delete-app-with-nested-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A & "C", "T\U", "k", "gone")
                    If Err.Number <> 0 Then L "delete-app-with-nested-section: err " & Err.Number & " " & Err.Description Else L "delete-app-with-nested-section: " & Show(r)
                End Sub

                Sub R4()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting Null
                    If Err.Number <> 0 Then L "delete-app-null: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-app-null: ran"
                End Sub

                Sub R5()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting ""
                    If Err.Number <> 0 Then L "delete-app-empty: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "delete-app-empty: ran"
                End Sub

                Sub R6()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "D", BS & "S", "k", "lead"
                    If Err.Number <> 0 Then L "section-leading-backslash: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A & "D", BS & "S", "k", "absent") & "|" & GetSetting(A & "D", "S", "k", "absent")
                    If Err.Number <> 0 Then L "section-leading-backslash: err " & Err.Number & " " & Err.Description Else L "section-leading-backslash: " & Show(r)
                End Sub

                Sub R7()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "D", "S" & BS, "k", "trail"
                    If Err.Number <> 0 Then L "section-trailing-backslash: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A & "D", "S" & BS, "k", "absent") & "|" & GetSetting(A & "D", "S", "k", "absent")
                    If Err.Number <> 0 Then L "section-trailing-backslash: err " & Err.Number & " " & Err.Description Else L "section-trailing-backslash: " & Show(r)
                End Sub

                Sub R8()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "D", "S" & BS & BS & "T", "k", "double"
                    If Err.Number <> 0 Then L "section-double-backslash: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A & "D", "S" & BS & BS & "T", "k", "absent") & "|" & GetSetting(A & "D", "S" & BS & "T", "k", "absent")
                    If Err.Number <> 0 Then L "section-double-backslash: err " & Err.Number & " " & Err.Description Else L "section-double-backslash: " & Show(r)
                End Sub

                Sub R9()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "D", BS, "k", "only"
                    If Err.Number <> 0 Then L "section-only-backslash: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A & "D", BS, "k", "absent")
                    If Err.Number <> 0 Then L "section-only-backslash: err " & Err.Number & " " & Err.Description Else L "section-only-backslash: " & Show(r)
                End Sub

                Sub R10()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & " ", "S", "k", "space"
                    If Err.Number <> 0 Then L "app-trailing-space: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S", "k", "absent") & "|" & GetSetting(A & " ", "S", "k", "absent")
                    If Err.Number <> 0 Then L "app-trailing-space: err " & Err.Number & " " & Err.Description Else L "app-trailing-space: " & Show(r)
                End Sub

                Sub R11()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S", " k ", "spaced"
                    If Err.Number <> 0 Then L "key-spaces: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S", "k", "absent") & "|" & GetSetting(A, "S", " k ", "absent")
                    If Err.Number <> 0 Then L "key-spaces: err " & Err.Number & " " & Err.Description Else L "key-spaces: " & Show(r)
                End Sub

                Sub R12()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S", String(300, "k"), "long"
                    If Err.Number <> 0 Then L "long-key: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S", String(300, "k"), "absent")
                    If Err.Number <> 0 Then L "long-key: err " & Err.Number & " " & Err.Description Else L "long-key: " & Show(r)
                End Sub

                Sub R13()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, String(300, "s"), "k", "long"
                    If Err.Number <> 0 Then L "long-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, String(300, "s"), "k", "absent")
                    If Err.Number <> 0 Then L "long-section: err " & Err.Number & " " & Err.Description Else L "long-section: " & Show(r)
                End Sub

                Sub R14()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = Len(GetSetting(A & "Types", "S", "Sz")) & " " & GetSetting(A & "Types", "S", "Sz")
                    If Err.Number <> 0 Then L "types-sz: err " & Err.Number & " " & Err.Description Else L "types-sz: " & Show(r)
                End Sub

                Sub R15()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "Types", "S", "Dword", "absent")
                    If Err.Number <> 0 Then L "types-dword: err " & Err.Number & " " & Err.Description Else L "types-dword: " & Show(r)
                End Sub

                Sub R16()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "Types", "S", "Qword", "absent")
                    If Err.Number <> 0 Then L "types-qword: err " & Err.Number & " " & Err.Description Else L "types-qword: " & Show(r)
                End Sub

                Sub R17()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "Types", "S", "Expand", "absent")
                    If Err.Number <> 0 Then L "types-expand: err " & Err.Number & " " & Err.Description Else L "types-expand: " & Show(r)
                End Sub

                Sub R18()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = Len(GetSetting(A & "Types", "S", "Multi", "absent"))
                    If Err.Number <> 0 Then L "types-multi: err " & Err.Number & " " & Err.Description Else L "types-multi: " & Show(r)
                End Sub

                Sub R19()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = Len(GetSetting(A & "Types", "S", "Binary", "absent"))
                    If Err.Number <> 0 Then L "types-binary: err " & Err.Number & " " & Err.Description Else L "types-binary: " & Show(r)
                End Sub

                Sub R20()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A & "Types", "S")
                    If Err.Number <> 0 Then L "types-getall: err " & Err.Number & " " & Err.Description Else L "types-getall: " & Show(r)
                End Sub

                Sub R21()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "Types", "S", "(default)", "absent")
                    If Err.Number <> 0 Then L "types-default-value: err " & Err.Number & " " & Err.Description Else L "types-default-value: " & Show(r)
                End Sub

                Sub R22()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A & "Types", "S", "Dword", "now text"
                    If Err.Number <> 0 Then L "save-over-dword: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A & "Types", "S", "Dword", "absent")
                    If Err.Number <> 0 Then L "save-over-dword: err " & Err.Number & " " & Err.Description Else L "save-over-dword: " & Show(r)
                End Sub

                Sub R23()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    DeleteSetting A
                    If Err.Number <> 0 Then L "save-after-delete-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    SaveSetting A, "S", "k", "back"
                    If Err.Number <> 0 Then L "save-after-delete-app: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S", "k", "absent")
                    If Err.Number <> 0 Then L "save-after-delete-app: err " & Err.Number & " " & Err.Description Else L "save-after-delete-app: " & Show(r)
                End Sub

                Sub Main()
                    On Error Resume Next
                    DeleteSetting A, "S"
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
                    DeleteSetting A, "S"
                End Sub

                Main

                """"""");

        AssertDebugLog(Lines("""
            delete-app: String [gone|gone]
            delete-app-again: setup err 5 Invalid procedure call or argument
            delete-app-again: ran
            delete-app-with-empty-section: Empty
            delete-app-with-nested-section: setup err 5 Invalid procedure call or argument
            delete-app-with-nested-section: String [v]
            delete-app-null: setup err 94 Invalid use of Null
            delete-app-null: ran
            delete-app-empty: setup err 5 Invalid procedure call or argument
            delete-app-empty: ran
            section-leading-backslash: String [lead|lead]
            section-trailing-backslash: String [trail|trail]
            section-double-backslash: String [double|double]
            section-only-backslash: String [only]
            app-trailing-space: String [absent|space]
            key-spaces: String [absent|spaced]
            long-key: String [long]
            long-section: setup err 5 Invalid procedure call or argument
            long-section: err 5 Invalid procedure call or argument
            types-sz: String [3 abc]
            types-dword: err 5 Invalid procedure call or argument
            types-qword: err 5 Invalid procedure call or argument
            types-expand: err 5 Invalid procedure call or argument
            types-multi: err 5 Invalid procedure call or argument
            types-binary: err 5 Invalid procedure call or argument
            types-getall: err 5 Invalid procedure call or argument
            types-default-value: String [absent]
            save-over-dword: String [now text]
            save-after-delete-app: String [back]
            """));
    }

    // The third batch: GetAllSettings lists a section's unnamed default value as the key "", and a section of "\"
    // is the program's own key.
    [Fact]
    public async Task A_default_value_and_values_on_the_program_key()
    {
        var withDefault = new[] { "HexProbe1562Types", "Sd" };
        SettingsStore.SetValue(withDefault, "Sz", "abc");
        SettingsStore.SetValue(withDefault, "Empty", "");
        SettingsStore.SetValue(withDefault, "", "dv");
        var withExpand = new[] { "HexProbe1562Types", "Se" };
        SettingsStore.SetValue(withExpand, "Sz", "abc");
        SettingsStore.SetNonText(withExpand, "Expand");
        SettingsStore.SetValue(new[] { "HexProbe1562Types" }, "AppVal", "top");

        using (VbLocale.Use(EnGb))
            await Run("""""""

                Public Const A As String = "HexProbe1562"

                Public Sub L(ByVal s As String)
                    Debug.Print s
                End Sub

                Public Function Show(v As Variant) As String
                    Dim i As Long, r As String
                    If IsArray(v) Then
                        On Error Resume Next
                        r = "Array " & TypeName(v) & " (" & LBound(v, 1) & " To " & UBound(v, 1) & ", " & LBound(v, 2) & " To " & UBound(v, 2) & ")"
                        For i = LBound(v, 1) To UBound(v, 1)
                            r = r & " " & v(i, 0) & "=" & v(i, 1) & "[" & TypeName(v(i, 0)) & "," & TypeName(v(i, 1)) & "]"
                        Next
                        Show = r
                    ElseIf IsNull(v) Then
                        Show = "Null"
                    ElseIf IsEmpty(v) Then
                        Show = "Empty"
                    Else
                        Show = TypeName(v) & " [" & CStr(v) & "]"
                    End If
                End Function

                Public Sub Clean()
                    On Error Resume Next
                    Dim s As Variant
                    For Each s In Array("S1", "S2\Sub", "S2", "S3", "S4", "S5", "S6", "S7", "X\S")
                        DeleteSetting A, s
                    Next
                    DeleteSetting "1562", "2"
                End Sub

                Sub R0()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A & "Types", "Sd")
                    If Err.Number <> 0 Then L "getall-sz-and-default: err " & Err.Number & " " & Err.Description Else L "getall-sz-and-default: " & Show(r)
                End Sub

                Sub R1()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A & "Types", "Se")
                    If Err.Number <> 0 Then L "getall-sz-and-expand: err " & Err.Number & " " & Err.Description Else L "getall-sz-and-expand: " & Show(r)
                End Sub

                Sub R2()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetAllSettings(A & "Types", "\")
                    If Err.Number <> 0 Then L "getall-app-key-values: err " & Err.Number & " " & Err.Description Else L "getall-app-key-values: " & Show(r)
                End Sub

                Sub R3()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "Types", "\", "AppVal", "absent")
                    If Err.Number <> 0 Then L "get-app-key-value: err " & Err.Number & " " & Err.Description Else L "get-app-key-value: " & Show(r)
                End Sub

                Sub R4()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A & "Types", "Sd", "Empty", "absent")
                    If Err.Number <> 0 Then L "get-empty-sz: err " & Err.Number & " " & Err.Description Else L "get-empty-sz: " & Show(r)
                End Sub

                Sub Main()
                    R0
                    R1
                    R2
                    R3
                    R4
                End Sub

                Main

                """"""");

        AssertDebugLog(Lines("""
            getall-sz-and-default: Array String() (0 To 2, 0 To 1) Sz=abc[String,String] Empty=[String,String] =dv[String,String]
            getall-sz-and-expand: err 5 Invalid procedure call or argument
            getall-app-key-values: Array String() (0 To 0, 0 To 1) AppVal=top[String,String]
            get-app-key-value: String [top]
            get-empty-sz: String []
            """));
    }

    // The fourth batch: the default is read only when it is the answer, the names are converted before any is
    // checked, Call and VBA. reach the same statements, and GetAllSettings assigns to a String array. Empty assigned
    // to a String array, which vb6.exe refuses with 13, is left out: that is #1588, array assignment, not these four.
    [Fact]
    public async Task What_is_converted_when_and_the_other_ways_to_call_them()
    {
        using (VbLocale.Use(EnGb))
            await Run("""""""

                Public Const A As String = "HexProbe1562"

                Public Sub L(ByVal s As String)
                    Debug.Print s
                End Sub

                Public Function Show(v As Variant) As String
                    Dim i As Long, r As String
                    If IsArray(v) Then
                        On Error Resume Next
                        r = "Array " & TypeName(v) & " (" & LBound(v, 1) & " To " & UBound(v, 1) & ", " & LBound(v, 2) & " To " & UBound(v, 2) & ")"
                        For i = LBound(v, 1) To UBound(v, 1)
                            r = r & " " & v(i, 0) & "=" & v(i, 1) & "[" & TypeName(v(i, 0)) & "," & TypeName(v(i, 1)) & "]"
                        Next
                        Show = r
                    ElseIf IsNull(v) Then
                        Show = "Null"
                    ElseIf IsEmpty(v) Then
                        Show = "Empty"
                    Else
                        Show = TypeName(v) & " [" & CStr(v) & "]"
                    End If
                End Function

                Public Sub Clean()
                    On Error Resume Next
                    Dim s As Variant
                    For Each s In Array("S1", "S2\Sub", "S2", "S3", "S4", "S5", "S6", "S7", "X\S")
                        DeleteSetting A, s
                    Next
                    DeleteSetting "1562", "2"
                End Sub

                Sub R0()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "K", "v"
                    If Err.Number <> 0 Then L "null-default-key-exists: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "K", Null)
                    If Err.Number <> 0 Then L "null-default-key-exists: err " & Err.Number & " " & Err.Description Else L "null-default-key-exists: " & Show(r)
                End Sub

                Sub R1()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "S1", "NoKey", Array(1))
                    If Err.Number <> 0 Then L "array-default-missing: err " & Err.Number & " " & Err.Description Else L "array-default-missing: " & Show(r)
                End Sub

                Sub R2()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "S1", "K", Array(1))
                    If Err.Number <> 0 Then L "array-default-key-exists: err " & Err.Number & " " & Err.Description Else L "array-default-key-exists: " & Show(r)
                End Sub

                Sub R3()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "S1", "NoKey", New Collection)
                    If Err.Number <> 0 Then L "object-default: err " & Err.Number & " " & Err.Description Else L "object-default: " & Show(r)
                End Sub

                Sub R4()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(Array(1), "S1", "K")
                    If Err.Number <> 0 Then L "array-app: err " & Err.Number & " " & Err.Description Else L "array-app: " & Show(r)
                End Sub

                Sub R5()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting A, "S1", "Arr", Array(1)
                    If Err.Number <> 0 Then L "array-setting: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Arr", "absent")
                    If Err.Number <> 0 Then L "array-setting: err " & Err.Number & " " & Err.Description Else L "array-setting: " & Show(r)
                End Sub

                Sub R6()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting "", "S1", "K", Null
                    If Err.Number <> 0 Then L "empty-app-null-setting: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "empty-app-null-setting: ran"
                End Sub

                Sub R7()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    SaveSetting Null, "", "K", "v"
                    If Err.Number <> 0 Then L "null-app-empty-section: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    L "null-app-empty-section: ran"
                End Sub

                Sub R8()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting("", "S1", Null)
                    If Err.Number <> 0 Then L "empty-app-null-key-get: err " & Err.Number & " " & Err.Description Else L "empty-app-null-key-get: " & Show(r)
                End Sub

                Sub R9()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = GetSetting(A, "NoSec", "K", Null)
                    If Err.Number <> 0 Then L "missing-section-null-default: err " & Err.Number & " " & Err.Description Else L "missing-section-null-default: " & Show(r)
                End Sub

                Sub R10()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    Call SaveSetting(A, "S1", "Called", "c")
                    If Err.Number <> 0 Then L "call-save: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Called", "absent")
                    If Err.Number <> 0 Then L "call-save: err " & Err.Number & " " & Err.Description Else L "call-save: " & Show(r)
                End Sub

                Sub R11()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    Call DeleteSetting(A, "S1", "Called")
                    If Err.Number <> 0 Then L "call-delete: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Called", "gone")
                    If Err.Number <> 0 Then L "call-delete: err " & Err.Number & " " & Err.Description Else L "call-delete: " & Show(r)
                End Sub

                Sub R12()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    VBA.SaveSetting A, "S1", "Qual", "q"
                    If Err.Number <> 0 Then L "vba-save: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = VBA.GetSetting(A, "S1", "Qual", "absent")
                    If Err.Number <> 0 Then L "vba-save: err " & Err.Number & " " & Err.Description Else L "vba-save: " & Show(r)
                End Sub

                Sub R13()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = UBound(VBA.GetAllSettings(A, "S1"), 1)
                    If Err.Number <> 0 Then L "vba-getall: err " & Err.Number & " " & Err.Description Else L "vba-getall: " & Show(r)
                End Sub

                Sub R14()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    VBA.DeleteSetting A, "S1", "Qual"
                    If Err.Number <> 0 Then L "vba-delete: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = GetSetting(A, "S1", "Qual", "gone")
                    If Err.Number <> 0 Then L "vba-delete: err " & Err.Number & " " & Err.Description Else L "vba-delete: " & Show(r)
                End Sub

                Sub R15()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = VBA.Interaction.GetSetting(A, "S1", "K", "absent")
                    If Err.Number <> 0 Then L "interaction-get: err " & Err.Number & " " & Err.Description Else L "interaction-get: " & Show(r)
                End Sub

                Sub R16()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    Dim s() As String
                    s = GetAllSettings(A, "S1")
                    If Err.Number <> 0 Then L "getall-into-string-array: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = UBound(s, 1) & " " & s(0, 0)
                    If Err.Number <> 0 Then L "getall-into-string-array: err " & Err.Number & " " & Err.Description Else L "getall-into-string-array: " & Show(r)
                End Sub

                Sub R17()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    Dim t() As String
                    t = GetAllSettings(A, "NoSec")
                    If Err.Number <> 0 Then L "getall-missing-into-string-array: setup err " & Err.Number & " " & Err.Description: Err.Clear
                    r = UBound(t, 1)
                    If Err.Number <> 0 Then L "getall-missing-into-string-array: err " & Err.Number & " " & Err.Description Else L "getall-missing-into-string-array: " & Show(r)
                End Sub

                Sub R18()
                    On Error Resume Next
                    Dim r As Variant
                    Err.Clear
                    r = UBound(GetAllSettings(A, "S1"), 2)
                    If Err.Number <> 0 Then L "getall-two-dims: err " & Err.Number & " " & Err.Description Else L "getall-two-dims: " & Show(r)
                End Sub

                Sub Main()
                    Clean
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
                    R18
                    Clean
                End Sub

                Main

                """"""");

        AssertDebugLog(Lines("""
            null-default-key-exists: String [v]
            array-default-missing: err 13 Type mismatch
            array-default-key-exists: String [v]
            object-default: err 450 Wrong number of arguments or invalid property assignment
            array-app: err 13 Type mismatch
            array-setting: setup err 13 Type mismatch
            array-setting: String [absent]
            empty-app-null-setting: setup err 94 Invalid use of Null
            empty-app-null-setting: ran
            null-app-empty-section: setup err 94 Invalid use of Null
            null-app-empty-section: ran
            empty-app-null-key-get: err 94 Invalid use of Null
            missing-section-null-default: err 94 Invalid use of Null
            call-save: String [c]
            call-delete: String [gone]
            vba-save: String [q]
            vba-getall: Long [1]
            vba-delete: String [gone]
            interaction-get: String [v]
            getall-into-string-array: String [0 K]
            getall-two-dims: Long [1]
            """));
    }

    private static List<Vb6Value> Lines(string text) =>
        text.ReplaceLineEndings("\n").Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(l => new Vb6Value(l)).ToList();
}
