// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors
// Test inputs ported from the Rubberduck project
// https://github.com/rubberduck-vba/Rubberduck (archived, GPLv3)
// Original file: RubberduckTests/Grammar/VBAParserTests.cs
//
// Adaptation: tests are translated from RD's XPath-based tree assertions into
// simple "valid VBA parses without diagnostics" assertions using our
// VbDiagnosticsProvider.GetDiagnostics(). The code inputs are the valuable part.

using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// Grammar torture tests ported from the Rubberduck VBA ANTLR4 grammar test suite.
/// Exercises esoteric but valid VB6/VBA syntax that the grammar must handle cleanly.
/// </summary>
public class RubberduckGrammarTests
{
    private static void AssertNoDiagnostics(string code)
    {
        var diagnostics = VbDiagnosticsProvider.GetDiagnostics(code);
        diagnostics.Should().BeEmpty(
            $"expected valid VBA to parse without errors, but got: {string.Join("; ", diagnostics.Select(d => d.Message))}");
    }

    // ── Line Continuations ────────────────────────────────────────────────────

    [Fact] public void EndSubLineContinuation() => AssertNoDiagnostics(@"
Sub Test()

End _
Sub");

    [Fact] public void ExitSubLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    Exit _
    Sub
End Sub");

    [Fact] public void EndFunctionLineContinuation() => AssertNoDiagnostics(@"
Function Test()

End _
Function");

    [Fact] public void ExitFunctionLineContinuation() => AssertNoDiagnostics(@"
Public Function Test()
    Exit _
    Function
End Function");

    [Fact] public void PropertyGetLineContinuation() => AssertNoDiagnostics(@"
Property _
Get Test()
End Property");

    [Fact] public void PropertyLetLineContinuation() => AssertNoDiagnostics(@"
Property _
Let Test(anything As Integer)
End Property");

    [Fact] public void PropertySetLineContinuation() => AssertNoDiagnostics(@"
Property _
Set Test(anything As Application)
End Property");

    [Fact] public void EndPropertyLineContinuation() => AssertNoDiagnostics(@"
Property Get Test()

End _
Property");

    [Fact] public void ExitPropertyLineContinuation() => AssertNoDiagnostics(@"
Public Property Get Test()
    Exit _
    Property
End Property");

    [Fact] public void EndIfLineContinuation() => AssertNoDiagnostics(@"
Function Test()
    If 1 = 1 Then
    End _
    If
End Function");

    [Fact] public void EndSelectLineContinuation() => AssertNoDiagnostics(@"
Property Get Test()
    Select Case 1 = 2
    End _
    Select
End Property");

    [Fact] public void EndWithLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
  With Application
  End _
  With
End Sub");

    [Fact] public void ExitDoLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    Do While True
        Exit _
        Do
    Loop
End Sub");

    [Fact] public void ExitForLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    For i = 1 To 2
        Exit _
        For
    Next i
End Sub");

    [Fact] public void LineInputLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    Line _
    Input #1, TextLine
End Sub");

    [Fact] public void ReadWriteKeywordLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    Open ""TESTFILE"" For Random Access Read _
    Write As #1
End Sub");

    [Fact] public void LockReadKeywordLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    Open ""TESTFILE"" For Random Lock _
    Read As #1
End Sub");

    [Fact] public void LockWriteKeywordLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    Open ""TESTFILE"" For Random Lock _
    Write As #1
End Sub");

    [Fact] public void LockReadWriteKeywordLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
    Open ""TESTFILE"" For Random Lock _
    Read _
    Write As #1
End Sub");

    [Fact] public void OnErrorLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
On _
Error GoTo a
a:
End Sub");

    [Fact] public void OnLocalErrorLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
On _
Local _
Error GoTo a
a:
End Sub");

    [Fact] public void OptionBaseLineContinuation() => AssertNoDiagnostics(@"
Option _
Base _
1");

    [Fact] public void OptionExplicitLineContinuation() => AssertNoDiagnostics(@"
Option _
Explicit");

    [Fact] public void OptionCompareLineContinuation() => AssertNoDiagnostics(@"
Option _
Compare _
Text");

    [Fact] public void OptionPrivateModuleLineContinuation() => AssertNoDiagnostics(@"
Option _
Private _
Module");

    [Fact] public void DictionaryAccessExprLineContinuation() => AssertNoDiagnostics(@"
Public Sub Test()
    Set result = myObj _
    ! _
    call
End Sub");

    [Fact] public void WithDictionaryAccessExprLineContinuation() => AssertNoDiagnostics(@"
Public Sub Test()
    With Application
        ! _ 
  Activate
    End With
End Sub");

    [Fact] public void LetStmtLineContinuation() => AssertNoDiagnostics(@"
Public Sub Test()
    x = ( _
            1 / _
            1 _
        ) * 1
End Sub");

    [Fact] public void MemberAccessExprLineContinuation() => AssertNoDiagnostics(@"
Public Sub Test()
    Debug.Print Foo.Bar _
                   . _
                    FooBar.Baz
End Sub");

    [Fact] public void WithMemberAccessExprLineContinuation() => AssertNoDiagnostics(@"
Public Sub Test()
    With Application
        . _
    Activate
    End With
End Sub");

    [Fact] public void CallStmtLineContinuation() => AssertNoDiagnostics(@"
Sub Test()
	fun(1) _
	.fun(2) _
	.fun(3)
End Sub");

    [Fact] public void DeclareLineContinuation() => AssertNoDiagnostics(
        "Private Declare Function ABC Lib \"shell32.dll\" Alias _\n" +
        "\"ShellExecuteA\"(ByVal a As Long, ByVal b As String, _\n" +
        "ByVal c As String, ByVal d As String, ByVal e As String, ByVal f As Long) As Long");

    // ── Blank lines inside loop bodies (plasma demo bug-sweep) ──
    // A For / For Each body parses via `unterminatedBlock`; its inter-statement separator must
    // absorb blank lines (more than one end-of-statement). Before the fix it allowed exactly one,
    // so a blank line ended the body early and orphaned the rest before NEXT, producing cascading
    // false "syntax error" diagnostics on valid code (vb6.exe compiled the same source cleanly).

    // Minimal: a blank line between two trivial statements inside a For loop.
    [Fact] public void ForBody_BlankLineBetweenStatements_NoFalseError() => AssertNoDiagnostics(@"
Sub Test()
    For px = 0 To 10
        v = a + b

        r = c
    Next px
End Sub");

    // The original symptom: a line-continued multi-term sum, a blank line, then another
    // statement, inside nested loops.
    [Fact] public void ForBody_BlankLineAfterLineContinuedSum_NoFalseError() => AssertNoDiagnostics(@"
Sub Test()
    Dim v As Double, r As Long
    Dim px As Long, py As Long, t As Double, a As Double
    For py = 0 To 10
        For px = 0 To 10
            v = Sin(px * a) _
              + Sin(py * a) _
              + Sin((px + py) * a + t) _
              + Sin(Sqr(px * px + py * py) * a + t)

            r = Int((Sin(v) * 0.5 + 0.5) * 255)
        Next px
    Next py
End Sub");

    // Same effect with the expression on one line — confirms the trigger is the blank line in
    // the loop body, not the line-continuation (which was incidental in the plasma code).
    [Fact] public void ForBody_BlankLineAfterSingleLineExpression_NoFalseError() => AssertNoDiagnostics(@"
Sub Test()
    Dim v As Double, r As Long, px As Long, py As Long, t As Double, a As Double
    For px = 0 To 10
        v = Sin(px * a) + Sin(py * a) + Sin((px + py) * a + t)

        r = Int(v)
    Next px
End Sub");

    // For Each bodies use the same rule — guard the blank line there too.
    [Fact] public void ForEachBody_BlankLineBetweenStatements_NoFalseError() => AssertNoDiagnostics(@"
Sub Test()
    Dim item As Variant, coll As Collection
    For Each item In coll
        v = a + b

        r = c
    Next item
End Sub");

    [Fact] public void EndEnumMultipleWhiteSpace() => AssertNoDiagnostics(@"
Enum Test
    anything
End               Enum");

    [Fact] public void EndTypeMultipleWhiteSpace() => AssertNoDiagnostics(@"
Type Test
    anything As Integer
End             Type");

    // ── Line Numbers and Labels ───────────────────────────────────────────────

    [Fact] public void LineNumber_EndSub() => AssertNoDiagnostics(@"
Sub DoSomething()
10 End Sub");

    [Fact] public void LineNumber_EndFunction() => AssertNoDiagnostics(@"
Function DoSomething()
10 End Function");

    [Fact] public void LineNumber_EndProperty() => AssertNoDiagnostics(@"
Property Get DoSomething()
10 End Property");

    [Fact] public void LineNumber_IfStmt() => AssertNoDiagnostics(@"
Sub DoSomething()
10 If True Then Debug.Print 42
End Sub");

    [Fact] public void LineNumber_ElseStmt() => AssertNoDiagnostics(@"
Sub DoSomething()
10 If True Then
11     Debug.Print 42
20 Else
21     Debug.Print 42
30 End If
End Sub");

    [Fact] public void LineNumber_SelectCase() => AssertNoDiagnostics(@"
Sub DoSomething()
10 Select Case False
20 Case True
21     Debug.Print 42
30 Case False
31     Debug.Print 42
40 End Select
End Sub");

    [Fact] public void LineNumber_ForNextLoop() => AssertNoDiagnostics(@"
Sub DoSomething()
10 For i = 1 To 10
20     Debug.Print 42
30 Next
End Sub");

    [Fact] public void LineNumber_ForEachLoop() => AssertNoDiagnostics(@"
Sub DoSomething()
10 For Each foo In bar
20     Debug.Print 42
30 Next
End Sub");

    [Fact] public void LineNumber_DoLoop() => AssertNoDiagnostics(@"
Sub DoSomething()
10 Do
20     Debug.Print 42
30 Loop While False
End Sub");

    [Fact] public void LineNumber_WithBlock() => AssertNoDiagnostics(@"
Sub DoSomething()
10 With New Collection
20     Debug.Print 42
30 End With
End Sub");

    [Fact] public void LineNumber_WhileLoop() => AssertNoDiagnostics(@"
Sub DoSomething()
10 While False
20     Debug.Print 42
30 Wend
End Sub");

    [Fact] public void LineLabelStatement() => AssertNoDiagnostics(@"
Sub Test()
a:
10:
154
12 b:
52'comment
644 _

71Rem stupid Rem comment
22 

77 _
 : 
42whatever
End Sub");

    [Fact] public void LineLabelStatementWithCodeOnSameLine() => AssertNoDiagnostics(@"
Sub Test()
a: foo
10: bar: foo
15 bar
12 b: foo: bar
77 _
 : bar
End Sub");

    [Fact] public void CombinedForNextStatement() => AssertNoDiagnostics(@"
Sub Test()
    For n = 1 To 10
        For m = 1 To 20
            a = m + n
        Next m _
    , n%
End Sub");

    [Fact] public void CombinedForNextStatementWithIntermediateCode() => AssertNoDiagnostics(@"
Sub Test()
    For n = 1 To 10
        b = n
        For m = 1 To 20
            a = m + n
    Next m,n%
End Sub");

    [Fact] public void CombinedForEachStatement() => AssertNoDiagnostics(@"
Sub Test()
    Dim foo As Collection
    Dim bar As Collection
    For Each n In foo
        For Each m In bar
            a = m + n
    Next m _
        , _
        n%
End Sub");

    [Fact] public void CombinedForEachStatementWithIntermediateCode() => AssertNoDiagnostics(@"
Sub Test()
    Dim foo As Collection
    Dim bar As Collection
    For Each n In foo
        b = n
        For Each m In bar
            a = m + n
    Next m,n%
End Sub");

    [Fact] public void MixedCombinedForEachAndForNext() => AssertNoDiagnostics(@"
Sub Test()
    Dim foo As Collection
    Dim bar As Collection
    For n = 1 To 10
        b = n
        For Each c In foo
            For m = 1 To 20
                For Each d In bar
                    a = m + n + c + d
                        For k = 0 To 100
                            t = a + k
    Next k, d, m, _
            c, _
            n
End Sub");

    [Fact] public void MixedRegularAndCombinedForEachAndForNext() => AssertNoDiagnostics(@"
Sub Test()
    Dim foo As Collection
    Dim bar As Collection
    For n = 1 To 10
        For Each c In foo
        Next c
        For m = 1 To 20
            For k = 0 To 100
                t = a + k
            Next
            For Each d In bar
                For l = 15 To 23
                   a = m + n + d + l
            Next l, d                   
    Next m, n
End Sub");

    [Fact] public void DoEventKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
DoEvents: MsgBox
End Sub");

    [Fact] public void EndKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
End: MsgBox
End Sub");

    [Fact] public void CloseKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Close: MsgBox
End Sub");

    [Fact] public void DoKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Do: MsgBox : Loop
End Sub");

    [Fact] public void ElseKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
If True Then
Else: MsgBox
End If
End Sub");

    [Fact] public void LoopKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Do Until False
Loop: MsgBox
End Sub");

    [Fact] public void NextKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
For i = 1 To 10
Next: MsgBox
End Sub");

    [Fact] public void RandomizeKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Randomize: MsgBox
End Sub");

    [Fact] public void RemKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Rem: MsgBox
End Sub");

    [Fact] public void ResumeKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Resume: MsgBox
End Sub");

    [Fact] public void ReturnKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Return: MsgBox
End Sub");

    [Fact] public void StopKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
Stop: MsgBox
End Sub");

    [Fact] public void WendKeywordDoesNotParseAsLineLabel() => AssertNoDiagnostics(@"
Sub DoSomething()
While True
Wend: MsgBox
End Sub");

    // ── Single-line If edge cases ─────────────────────────────────────────────

    [Fact] public void SingleLineIfEmptyThenEmptyElse() => AssertNoDiagnostics(@"
Sub Test()
    If False Then Else
End Sub");

    [Fact] public void SingleLineIfEmptyThenEndOfStatement() => AssertNoDiagnostics(@"
Sub Test()
    If False Then: Else
End Sub");

    [Fact] public void SingleLineIfMultipleThenNoElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then MsgBox False: MsgBox False Else
End Sub");

    [Fact] public void SingleLineIfMultipleThenMultipleElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then MsgBox False: MsgBox False Else MsgBox False: MsgBox False
End Sub");

    [Fact] public void SingleLineIfEmptyThen() => AssertNoDiagnostics(@"
Sub Test()
      If False Then Else MsgBox True
End Sub");

    [Fact] public void SingleLineIfSingleThenEmptyElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then MsgBox True Else
End Sub");

    [Fact] public void SingleLineIfSingleEmptyThenEmptyElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then:: Else:
End Sub");

    [Fact] public void SingleLineIfSingleEmptyMultiLineThenEmptyElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then: _
      : Else:
End Sub");

    [Fact] public void SingleLineIfSingleEmptyThenEmptyMultiLineElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then Else _
      :
End Sub");

    [Fact] public void SingleLineIfSingleEmptyThenElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then Else _
      : Bar
End Sub");

    [Fact] public void SingleLineIfNestedEmptyThenEmptyElse() => AssertNoDiagnostics(@"
Sub Test()
      If True Then If False Then If True Then Else
End Sub");

    [Fact] public void SingleLineIfNestedThenEmptyElse() => AssertNoDiagnostics(@"
Sub Test()
      If True Then If False Then If True Then Bar Else
End Sub");

    [Fact] public void SingleLineIfNestedEmptyThenElse() => AssertNoDiagnostics(@"
Sub Test()
      If True Then If False Then If True Then Else Bar
End Sub");

    [Fact] public void SingleLineIfSingleEmptyMultiLineThenEmptyMultiLineElse() => AssertNoDiagnostics(@"
Sub Test()
      If False Then: _
      Else _
      :
End Sub");

    [Fact] public void SingleLineIfImplicitGoTo() => AssertNoDiagnostics(@"
Sub Test()
      ' This actually means: If True Then GoTo 5 Else GoTo 10
      If True Then 5 Else 10
End Sub");

    [Fact] public void SingleLineIfDoLoop() => AssertNoDiagnostics(@"
Sub Test()
      If True Then Do: Loop Else
End Sub");

    [Fact] public void SingleLineIfWendLoop() => AssertNoDiagnostics(@"
Sub Test()
      If True Then While True: Beep: Wend Else
End Sub");

    [Fact] public void SingleLineIfElseBlockOnSameLine() => AssertNoDiagnostics(@"
Sub Test()
    If True Then
    ElseIf False Then Debug.Print 5
    Else
    End If
End Sub");

    [Fact] public void SingleLineIfRealWorldExample1() => AssertNoDiagnostics(@"
Sub Test()
      On Local Error Resume Next: If Not Empty Is Nothing Then Do While Null: ReDim i(True To False) As Currency: Loop: Else Debug.Assert CCur(CLng(CInt(CBool(False Imp True Xor False Eqv True)))): Stop: On Local Error GoTo 0
End Sub");

    [Fact] public void SingleLineIfRealWorldExample2() => AssertNoDiagnostics(@"
Sub Test()
    With Application
        If bUpdate Then .Calculation = xlCalculationAutomatic: .Cursor = xlDefault Else .Calculation = xlCalculationManual: .Cursor = xlWait: .EnableEvents = bUpdate: .ScreenUpdating = bUpdate: .DisplayAlerts = bUpdate: .CutCopyMode = False: .StatusBar = False
    End With
End Sub");

    [Fact] public void SingleLineIfRealWorldExample4() => AssertNoDiagnostics(@"
Sub Test()
    If Err Then Set oP_Window = Nothing: TurnOff Else If oP_Window Is Nothing Then TurnOn
End Sub");

    // ── Module Config (form .frm format) ─────────────────────────────────────

    [Fact] public void EmptyForm() => AssertNoDiagnostics(@"
VERSION 5.00
Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End
Attribute VB_Name = ""Form1""
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False");

    [Fact] public void VBFormModuleConfig() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void VBFormWithHexLiteralModuleConfig() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   BackColor = &H00FFFFFF&
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End");
    
    [Fact] public void VBFormWithAbsoluteResourcePathConfig() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   BackColor = &H00FFFFFF&
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""C:\Test\Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void VBFormWithDnsUncResourcePathConfig() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   BackColor = &H00FFFFFF&
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""\\initech.com\server01\c$\Test\Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void VBFormWithIpUncResourcePathConfig() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   BackColor = &H00FFFFFF&
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""\\127.0.0.1\Test\Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void VBFormWithDollarPrependedResourceConfig() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   $""Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void VBFormWithAlphaLeadingHexResourceOffset() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":ACBD
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void VBFormWithNumericLeadingHexResourceOffset() => AssertNoDiagnostics(@"
Begin VB.Form Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":9ABC
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void NestedVbFormModuleConfig() => AssertNoDiagnostics(@"
VERSION 5.00
Begin VB.Form Form1
   Caption = ""Main""
   ClientHeight = 2970
   ClientLeft = 60
   ClientTop = 450
   ClientWidth = 8250
   LinkTopic = ""Form1""
   ScaleHeight = 2970
   ScaleWidth = 8250
   StartUpPosition = 2  'CenterScreen
   Begin VB.CommandButton cmdDelete
      Caption = ""Delete""
      Height = 495
      Left = 1320
      TabIndex = 9
      Top = 2280
      Width = 1215
   End
End");

    [Fact] public void NestedVbFormModuleConfigWithObjectDeclarations() => AssertNoDiagnostics(@"
VERSION 5.00
Object = ""{67397AA1-7FB1-11D0-B148-00A0C922E820}#6.0#0""; ""MSADODC.OCX""
Object = ""{CDE57A40-8B86-11D0-B3C6-00A0C90AEA82}#1.0#0""; ""MSDATGRD.OCX""
Begin VB.Form Form1
   Caption = ""Main""
   ClientHeight = 2970
   ClientLeft = 60
   ClientTop = 450
   ClientWidth = 8250
   LinkTopic = ""Form1""
   ScaleHeight = 2970
   ScaleWidth = 8250
   StartUpPosition = 2  'CenterScreen
   Begin VB.CommandButton cmdDelete
      Caption = ""Delete""
      Height = 495
      Left = 1320
      TabIndex = 9
      Top = 2280
      Width = 1215
   End
End");

    [Fact] public void NestedVbFormModuleConfigWithMultipleChildren() => AssertNoDiagnostics(@"
VERSION 5.00
Begin VB.Form Form1
   Caption = ""Main""
   ClientHeight = 2970
   ClientLeft = 60
   ClientTop = 450
   ClientWidth = 8250
   LinkTopic = ""Form1""
   ScaleHeight = 2970
   ScaleWidth = 8250
   StartUpPosition = 2  'CenterScreen
   Begin VB.CommandButton cmdDelete
      Caption = ""Delete""
      Height = 495
      Left = 1320
      TabIndex = 9
      Top = 2280
      Width = 1215
   End
   Begin MSDataGridLib.DataGrid DataGrid1
      Bindings = ""frmMain.frx"":0000
      Height = 2055
      Left = 2520
      TabIndex = 0
      Top = 120
      Width = 5655
      _ExtentX = 9975
      _ExtentY = 3625
      _Version = 393216
      HeadLines = 1
      RowHeight = 15
      AllowAddNew = -1  'True
   End
End");

    [Fact] public void NestedVbFormModuleConfigWithProperty() => AssertNoDiagnostics(@"
Begin VB.Form Form1
   Caption = ""Main""
   ClientHeight = 2970
   ClientLeft = 60
   ClientTop = 450
   ClientWidth = 8250
   LinkTopic = ""Form1""
   ScaleHeight = 2970
   ScaleWidth = 8250
   StartUpPosition = 2  'CenterScreen
   Begin MSDataGridLib.DataGrid DataGrid1
      Bindings = ""frmMain.frx"":0000
      Height = 2055
      Left = 2520
      TabIndex = 0
      Top = 120
      Width = 5655
      _ExtentX = 9975
      _ExtentY = 3625
      _Version = 393216
      HeadLines = 1
      RowHeight = 15
      AllowAddNew = -1  'True
      BeginProperty HeadFont {0BE35203-8F91-11CE-9DE3-00AA004BB851}
            Name = ""MS Sans Serif""
         Size = 8.25
         Charset = 0
         Weight = 400
         Underline = 0   'False
         Italic = 0   'False
         Strikethrough = 0   'False
      EndProperty
   End
End");

    [Fact] public void GermanStyleFloatingPointsInFormsPart() => AssertNoDiagnostics(@"
Begin VB.Form Form1
   Caption = ""Main""
   ClientHeight = 2970
   ClientLeft = 60
   ClientTop = 450
   ClientWidth = 8250
   Begin MSDataGridLib.DataGrid DataGrid1
      BeginProperty HeadFont {0BE35203-8F91-11CE-9DE3-00AA004BB851}
         Size = 8,25
         Weight = 400
      EndProperty
   End
End");

    [Fact] public void NestedVbFormModuleConfigWithNestedProperties() => AssertNoDiagnostics(@"
VERSION 5.00
Begin VB.Form Form1
   Caption = ""Main""
   ClientHeight = 2970
   ClientLeft = 60
   ClientTop = 450
   ClientWidth = 8250
   Begin MSDataGridLib.DataGrid DataGrid1
      BeginProperty Column00 
         DataField       =   """"
         Caption         =   """"
         BeginProperty DataFormat {6D835690-900B-11D0-9484-00A0C91110ED} 
            Type            =   0
            Format          =   """"
            HaveTrueFalseNull=   0
            FirstDayOfWeek  =   0
            FirstWeekOfYear =   0
            LCID            =   1033
            SubFormatType   =   0
         EndProperty
      EndProperty
   End
End");

    [Fact] public void VbFormWithAnEmptyNestedProperty() => AssertNoDiagnostics(@"
VERSION 5.00
Object = ""{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.1#0""; ""MSCOMCTL.OCX""
Begin VB.Form Form1 
   Caption         =   ""Form1""
   ClientHeight    =   3195
   ClientLeft      =   60
   ClientTop       =   345
   ClientWidth     =   4680
   StartUpPosition =   3  'Windows Default
   Begin MSComctlLib.StatusBar StatusBar1 
      Align           =   2  'Align Bottom
      Height          =   375
      Left            =   0
      TabIndex        =   0
      Top             =   2820
      Width           =   4680
      BeginProperty Panels {8E3867A5-8586-11D1-B16A-00C0F0283628} 
         NumPanels       =   1
         BeginProperty Panel1 {8E3867AB-8586-11D1-B16A-00C0F0283628} 
         EndProperty
      EndProperty
   End
End
Attribute VB_Name = ""Form1""
Attribute VB_GlobalNameSpace = False
Attribute VB_Creatable = False
Attribute VB_PredeclaredId = True
Attribute VB_Exposed = False");

    [Fact] public void IndexedPropertyInFormConfig() => AssertNoDiagnostics(@"
VERSION 5.00
Begin VB.Form Form1
   Begin ComctlLib.ListView lvFilter 
      BeginProperty ColumnHeader(1) {0713E8C7-850A-101B-AFC0-4210102A8DA7} 
         Caption      =   ""ID""
      EndProperty
   End
End");

    [Theory]
    [InlineData(@"^A")] [InlineData(@"^Z")] [InlineData(@"{F1}")] [InlineData(@"{F12}")]
    [InlineData(@"^{F1}")] [InlineData(@"^{F12}")] [InlineData(@"+{F1}")] [InlineData(@"+{F12}")]
    [InlineData(@"+^{F1}")] [InlineData(@"+^{F12}")] [InlineData(@"^{INSERT}")] [InlineData(@"+{INSERT}")]
    [InlineData(@"{DEL}")] [InlineData(@"+{DEL}")] [InlineData(@"%{BKSP}")]
    public void VBFormWithMenuShortcut(string shortcut) => AssertNoDiagnostics($@"
Begin VB.Form Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
   Begin VB.Menu FileMenu 
      Caption         =   ""File""
      Begin VB.Menu FileOpenMenu
         Caption     = ""Open""
         Shortcut    =   {shortcut}
      End
   End 
End");

    [Fact] public void ModuleConfig_Begin_End() => AssertNoDiagnostics(@"
BEGIN
  MultiUse = -1  'True
END");

    // ── Attribute Statements ──────────────────────────────────────────────────

    [Fact] public void AttributeFirstLine() => AssertNoDiagnostics(@"
Attribute VB_Name = ""Form1""
VERSION 5.00");

    [Fact] public void AttributeAfterModuleHeader() => AssertNoDiagnostics(@"
VERSION 5.00
Attribute VB_Name = ""Form1""
Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End");

    [Fact] public void AttributeAfterModuleConfig() => AssertNoDiagnostics(@"
Begin {C62A69F0-16DC-11CE-9E98-00AA00574A4F} Form1 
   Caption         =   ""Form1""
   ClientHeight    =   2640
   ClientLeft      =   45
   ClientTop       =   375
   ClientWidth     =   4710
   OleObjectBlob   =   ""Form1.frx"":0000
   StartUpPosition =   1  'CenterOwner
End
Attribute VB_Name = ""Form1""
Private this As TProgressIndicator");

    [Fact] public void AttributeInsideModuleDeclarations() => AssertNoDiagnostics(@"
Public WithEvents colCBars As Office.CommandBars
Attribute colCBars.VB_VarHelpID = -1
Public WithEvents colCBars2 As Office.CommandBars");

    [Fact] public void AttributeAfterModuleDeclarations() => AssertNoDiagnostics(@"
Private this As TProgressIndicator
Attribute VB_Name = ""Form1""
Public Sub Test()
    Attribute VB_Name = ""Form1""
End Sub");

    [Fact] public void AttributeInsideProcedure() => AssertNoDiagnostics(@"
Public Sub Test()
    Attribute VB_Name = ""Form1""
End Sub");

    [Fact] public void AttributeEndOfFile() => AssertNoDiagnostics(@"
Public Sub Test()
End Sub
Attribute VB_Name = ""Form1""");

    [Fact] public void AttributeNameIsMemberAccessExpr() => AssertNoDiagnostics(@"
Attribute view.VB_VarHelpID = -1");

    // ── DefDirective ──────────────────────────────────────────────────────────

    [Fact] public void DefDirectiveSingleLetter() => AssertNoDiagnostics(
        "DefBool B: DefByte Y: DefInt I: DefLng L: DefLngLng N: DefLngPtr P: DefCur C: DefSng G: DefDbl D: DefDate T: DefStr E: DefObj O: DefVar V");

    [Fact] public void DefDirectiveSameDefMultipleLetterSpec() => AssertNoDiagnostics("DefBool B, C, D");

    [Fact] public void DefDirectiveLetterRange() => AssertNoDiagnostics(
        "DefBool A-C: DefByte Y-X: DefInt I-J: DefLng L-M: DefLngLng N-O: DefLngPtr P-Q: DefCur C-D: DefSng G-H: DefDbl D-E: DefDate T-U: DefStr E-F: DefObj O-P: DefVar V-W");

    [Fact] public void DefDirectiveUniversalLetterRange() => AssertNoDiagnostics("DefBool A-Z");

    // ── Module Options ────────────────────────────────────────────────────────

    [Fact] public void ModuleOption_Explicit() => AssertNoDiagnostics(@"
Option Explicit

Sub DoSomething()
End Sub");

    [Fact] public void ModuleOption_Explicit_Indented() => AssertNoDiagnostics(@"
    Option Explicit

    Sub DoSomething()
    End Sub");

    [Fact] public void ModuleHeader() => AssertNoDiagnostics("VERSION 1.0 CLASS");

    [Fact] public void TrivialColon() => AssertNoDiagnostics(":");

    // ── Comments ──────────────────────────────────────────────────────────────

    [Fact] public void EmptyComment() => AssertNoDiagnostics("'");

    [Fact] public void EmptyRemComment() => AssertNoDiagnostics("Rem");

    [Fact] public void OneCharRemComment() => AssertNoDiagnostics("Rem a");

    [Fact] public void OneCharComment() => AssertNoDiagnostics("'a");

    [Fact] public void CommentThatLooksLikeAnnotation() => AssertNoDiagnostics("'@param foo; the value of something");

    [Fact] public void Annotations() => AssertNoDiagnostics(@"
'@Folder a @Folder b
Sub Test()
    ' Test Comment
    Dim someString As String * 255 '@Folder c @Folder d
End Sub");

    // ── Miscellaneous ─────────────────────────────────────────────────────────

    [Fact] public void ForeignIdentifiers() => AssertNoDiagnostics(@"
Sub FooFoo()
  [Sheet2!A2]
  [[Book2]Sheet1!A1]
  [Book2!NamedRange]
  [""Hello World!""]
  [""!""]
  [""[]""]
  []
  a = [A1] + [A2]
End Sub");

    [Fact] public void FixedLengthString() => AssertNoDiagnostics(@"
Sub Test()
    Dim someString As String * 255
End Sub");

    [Fact] public void EraseStatement() => AssertNoDiagnostics(@"
Public Sub EraseTwoArrays()
Erase someArray(), someOtherArray()
End Sub");

    [Fact] public void DoLoopUntil() => AssertNoDiagnostics(@"
Sub Test()
    Do
    Loop Until var > 10
End Sub");

    [Fact] public void ForNextWithTypeSuffix() => AssertNoDiagnostics(@"
Sub Test()
    For n = 1 To 10
    Next n%
End Sub");

    [Fact] public void EndStatement() => AssertNoDiagnostics(@"
Sub Test()
    End
End Sub");

    [Fact] public void ReDimStatement() => AssertNoDiagnostics(@"
Sub Test()
    ReDim strArray(1 To 10)
End Sub");

    [Fact] public void ArrayWithTypeSuffix() => AssertNoDiagnostics(@"
Sub Test()
    Dim a!()
    Dim a@()
    Dim a#()
    Dim a$()
    Dim a%()
    Dim a^()
    Dim a&()
End Sub");

    [Fact] public void OpenStatement() => AssertNoDiagnostics(@"
Sub Test()
    Open ""TESTFILE"" For Binary Access Read Lock Read As #1 Len = 2
End Sub");

    [Fact] public void CloseStatement() => AssertNoDiagnostics(@"
Sub Test()
    Close #1, 2, 3
End Sub");

    [Fact] public void SeekStatement() => AssertNoDiagnostics(@"
Sub Test()
    Seek #1, 2
End Sub");

    [Fact] public void SeekFunction() => AssertNoDiagnostics(@"
Sub Test()
    anything = Seek(50)
End Sub");

    [Fact] public void LockStatement() => AssertNoDiagnostics(@"
Sub Test()
    Lock #1, 2
End Sub");

    [Fact] public void UnlockStatement() => AssertNoDiagnostics(@"
Sub Test()
    Unlock #1, 2
End Sub");

    [Fact] public void LineInputStatement() => AssertNoDiagnostics(@"
Sub Test()
    Line Input #2, ""ABC""
End Sub");

    [Fact] public void WidthStatement() => AssertNoDiagnostics(@"
Sub Test()
    Width #2, 5
End Sub");

    [Fact] public void PrintStatement() => AssertNoDiagnostics(@"
Sub Test()
    Print #2, Spc(5) ;
End Sub");

    [Fact] public void ObjectPrintStatement() => AssertNoDiagnostics(@"
Sub Test()
    Dim obj As Object
    obj.Print ""Hello "";""World"", ""!"" ;
End Sub");

    [Fact] public void DebugPrintNoArguments() => AssertNoDiagnostics(@"
Sub Test()
    Debug.Print
End Sub");

    [Fact] public void DebugPrintWithArgument() => AssertNoDiagnostics(@"
Sub Test()
    Debug.Print ""Anything""
End Sub");

    [Fact] public void DebugPrintOutputItemSemicolon() => AssertNoDiagnostics(@"
Sub Test()
    Debug.Print 1;
End Sub");

    [Fact] public void DebugPrintOutputItemComma() => AssertNoDiagnostics(@"
Sub Test()
    Debug.Print 1,
End Sub");

    [Fact] public void DebugPrintRealWorldLoop() => AssertNoDiagnostics(@"
Sub Test()
    For Each fld In tdf.Fields
        Debug.Print fld.Name,
        Debug.Print FieldTypeName(fld),
        Debug.Print fld.Size,
        Debug.Print GetDescrip(fld)
    Next
End Sub");

    [Fact] public void DebugPrintRealWorldWithIf() => AssertNoDiagnostics(@"
Sub Test()
    If Not pFault Then
        Debug.Print ""FirstO: "" & vbCr & ans(0) & vbCr
        Debug.Print ""SecondO:""; ans(1)
    End If
End Sub");

    [Fact] public void WriteStatement() => AssertNoDiagnostics(@"
Sub Test()
    Write #1, ""ABC"", 234
End Sub");

    [Fact] public void InputStatement() => AssertNoDiagnostics(@"
Sub Test()
    Input #1, ""ABC""
End Sub");

    [Fact] public void InputFunction() => AssertNoDiagnostics(@"
Sub Test()
    s = Input(LOF(file1), #file1)
    s = Input$(LOF(file1), #file1)
End Sub");

    [Fact] public void NameStatement() => AssertNoDiagnostics(@"
Sub Test()
    Dim sOldPath, sOldName As String
    Dim sNewPath, sNewName As String
    Name sOldPath + sOldName As sNewPath + sNewName
End Sub");

    [Fact] public void ProcedureNamedName() => AssertNoDiagnostics(@"
Sub Name()
End Sub

Sub Test()
    Name
End Sub");

    [Fact] public void CircleSpecialFormWithAllArgs() => AssertNoDiagnostics(@"
Sub Test()
    Me.Circle Step (1, 2), 3, 4, 5, 6, 7
End Sub");

    [Fact] public void CircleSpecialFormWithoutOptionalArgs() => AssertNoDiagnostics(@"
Sub Test()
    Me.Circle Step (1, 2), 3
End Sub");

    [Fact] public void CircleSpecialFormWithoutStep() => AssertNoDiagnostics(@"
Sub Test()
    Me.Circle (1, 2), 3, 4, 5, 6, 7
End Sub");

    [Theory]
    [InlineData(", 4, 5, 6, 7")] [InlineData(",,,, 7")] [InlineData(",, 5, 6, 7")]
    [InlineData(", , , 6, 7")] [InlineData(",,,,")] [InlineData(",, 5,, 7")]
    [InlineData(", 4, 5,, 7")] [InlineData(", 4, 5, 6,")] [InlineData(", 4,,, 7")]
    [InlineData("")] [InlineData(", 4,")] [InlineData(", 4, 5")] [InlineData(", 4, 5, 6")]
    public void CircleSpecialFormOptionalArgs(string optionalPart) => AssertNoDiagnostics($@"
Sub Test()
    Me.Circle Step (1, 2), 3{optionalPart}
End Sub");

    [Fact] public void LineSpecialForm() => AssertNoDiagnostics(@"
Sub Test()
    Me.Line Step(1, 1)-Step(2, 2), vbBlack, B
End Sub");

    [Fact] public void LineSpecialFormWithoutOptionalArgs() => AssertNoDiagnostics(@"
Sub Test()
    Me.Line (1, 1)-(2, 2)
End Sub");

    [Fact] public void LineSpecialFormWithoutStartingTuple() => AssertNoDiagnostics(@"
Sub Test()
    Me.Line -(2, 2)
End Sub");

    [Fact] public void ScaleSpecialForm() => AssertNoDiagnostics(@"
Sub Test()
    Scale (1, 2)-(3, 4)
End Sub");

    [Fact] public void PSetSpecialForm() => AssertNoDiagnostics(@"
Sub Test()
    PSet Step(1, 2), vbBlack
End Sub");

    [Fact] public void PSetSpecialFormWithoutStep() => AssertNoDiagnostics(@"
Sub Test()
    Me.PSet (1, 2), vbBlack
End Sub");

    [Fact] public void PSetSpecialFormWithoutOptionalArgs() => AssertNoDiagnostics(@"
Sub Test()
    Me.PSet (1, 2)
End Sub");

    [Fact] public void StringFunction() => AssertNoDiagnostics(@"
Sub Test()
    a = String(5, ""a"")
End Sub");

    [Fact] public void ResetStatement() => AssertNoDiagnostics(@"
Sub Test()
    Reset
End Sub");
}
