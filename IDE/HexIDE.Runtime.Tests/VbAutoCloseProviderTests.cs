// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.Runtime.Tests.

using HexIDE.Runtime.Editor;

namespace HexIDE.Runtime.Tests;

public class VbAutoCloseProviderTests
{
    // ── Sub ──────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Sub Foo()")]
    [InlineData("Public Sub Foo()")]
    [InlineData("Private Sub Foo()")]
    [InlineData("Friend Sub Foo()")]
    [InlineData("Public Static Sub Foo()")]
    [InlineData("Private Static Sub Foo()")]
    [InlineData("    Sub Foo(ByVal x As Integer)")]
    [InlineData("Sub Foo(x As Integer, y As String)")]
    public void Sub_ReturnsEndSub(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End Sub");
    }

    // ── Function ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Function Foo() As Integer")]
    [InlineData("Public Function Foo()")]
    [InlineData("Private Function Bar() As String")]
    [InlineData("Friend Function Baz()")]
    [InlineData("Public Static Function Foo()")]
    [InlineData("    Function Foo(ByVal x As Integer) As Long")]
    public void Function_ReturnsEndFunction(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End Function");
    }

    // ── Property ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Property Get Name() As String")]
    [InlineData("Public Property Get Name() As String")]
    [InlineData("Private Property Let Name(ByVal value As String)")]
    [InlineData("Property Set Obj(ByVal value As Object)")]
    [InlineData("Friend Property Get X()")]
    public void Property_ReturnsEndProperty(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End Property");
    }

    // ── If/Then (multi-line) ─────────────────────────────────────────────────

    [Theory]
    [InlineData("If x > 0 Then")]
    [InlineData("If x > 0 And y < 10 Then")]
    [InlineData("    If condition Then")]
    [InlineData("If x Then  ' some comment")]
    public void IfThen_MultiLine_ReturnsEndIf(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End If");
    }

    [Theory]
    [InlineData("If x > 0 Then y = 1")]
    [InlineData("If x Then MsgBox \"hi\"")]
    [InlineData("If x Then Exit Sub")]
    public void IfThen_SingleLine_ReturnsNull(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().BeNull();
    }

    // ── ElseIf (mid-block, NOT auto-closed) ──────────────────────────────────

    [Theory]
    [InlineData("ElseIf x > 0 Then")]
    [InlineData("    ElseIf condition Then")]
    public void ElseIf_ReturnsNull(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().BeNull();
    }

    // ── Select Case ──────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Select Case x")]
    [InlineData("    Select Case myVar")]
    [InlineData("Select Case GetValue()")]
    public void SelectCase_ReturnsEndSelect(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End Select");
    }

    // ── For / For Each ───────────────────────────────────────────────────────

    [Theory]
    [InlineData("For i = 1 To 10")]
    [InlineData("For i = 0 To UBound(arr)")]
    [InlineData("    For i = 1 To 10 Step 2")]
    [InlineData("For Each item In collection")]
    [InlineData("    For Each x In arr")]
    public void For_ReturnsNext(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("Next");
    }

    // ── Do Loop ──────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Do")]
    [InlineData("Do While x > 0")]
    [InlineData("Do Until x = 0")]
    [InlineData("    Do")]
    [InlineData("    Do While condition")]
    public void Do_ReturnsLoop(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("Loop");
    }

    // ── While ────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("While x > 0")]
    [InlineData("    While condition")]
    [InlineData("While Not EOF(1)")]
    public void While_ReturnsWend(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("Wend");
    }

    // ── With ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("With myObject")]
    [InlineData("    With .Controls(0)")]
    [InlineData("With Me")]
    public void With_ReturnsEndWith(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End With");
    }

    // ── Type (UDT) ───────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Type MyRecord")]
    [InlineData("Private Type InternalRec")]
    [InlineData("Public Type PublicRec")]
    public void Type_ReturnsEndType(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End Type");
    }

    // ── Enum ─────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Enum Colors")]
    [InlineData("Public Enum Severity")]
    [InlineData("Private Enum Internal")]
    public void Enum_ReturnsEndEnum(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("End Enum");
    }

    // ── #If preprocessor ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("#If DEBUG Then")]
    [InlineData("#If VBA7 Then")]
    [InlineData("    #If Win64 Then")]
    public void HashIf_ReturnsHashEndIf(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be("#End If");
    }

    // ── Declare (API declaration — NOT a block) ──────────────────────────────

    [Theory]
    [InlineData("Declare Sub Sleep Lib \"kernel32\" (ByVal ms As Long)")]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long")]
    [InlineData("Public Declare Sub memcpy Lib \"msvcrt\" Alias \"memcpy\" (dest As Any, src As Any, ByVal n As Long)")]
    public void Declare_ReturnsNull(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().BeNull();
    }

    // ── Non-block lines ──────────────────────────────────────────────────────

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("' This is a comment")]
    [InlineData("Dim x As Integer")]
    [InlineData("x = 42")]
    [InlineData("MsgBox \"Hello\"")]
    [InlineData("Exit Sub")]
    [InlineData("End Sub")]
    [InlineData("End Function")]
    [InlineData("Next")]
    [InlineData("Loop")]
    [InlineData("Wend")]
    [InlineData("Else")]
    [InlineData("Case 1")]
    public void NonBlock_ReturnsNull(string line)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().BeNull();
    }

    // ── Case sensitivity ─────────────────────────────────────────────────────

    [Theory]
    [InlineData("sub foo()", "End Sub")]
    [InlineData("PUBLIC SUB FOO()", "End Sub")]
    [InlineData("if x then", "End If")]
    [InlineData("SELECT CASE X", "End Select")]
    [InlineData("for i = 1 to 10", "Next")]
    [InlineData("do while true", "Loop")]
    public void CaseInsensitive_MatchesCorrectly(string line, string expected)
    {
        VbAutoCloseProvider.GetClosingStatement(line).Should().Be(expected);
    }

    // ── Continuation detection ───────────────────────────────────────────────

    [Theory]
    [InlineData("Private Sub Foo( _", true)]
    [InlineData("    x = 1 + _", true)]
    [InlineData("    x = 1 + _  ", true)]
    [InlineData("Sub Foo()", false)]
    [InlineData("' comment _", true)]  // VB6 treats _ as continuation even in comments
    [InlineData("", false)]
    [InlineData("   ", false)]
    public void IsContinuationLine_DetectsCorrectly(string line, bool expected)
    {
        VbAutoCloseProvider.IsContinuationLine(line).Should().Be(expected);
    }

    // ── Logical line assembly ────────────────────────────────────────────────

    [Fact]
    public void AssembleLogicalLine_SingleLine_ReturnsAsIs()
    {
        var lines = new[] { "Sub Foo()" };
        var result = VbAutoCloseProvider.AssembleLogicalLine(i => lines[i], 0);
        result.Should().Be("Sub Foo()");
    }

    [Fact]
    public void AssembleLogicalLine_TwoContinuationLines_JoinsCorrectly()
    {
        var lines = new[]
        {
            "Private Sub Foo( _",
            "    ByVal x As Integer)"
        };
        var result = VbAutoCloseProvider.AssembleLogicalLine(i => lines[i], 1);
        result.Should().Be("Private Sub Foo( ByVal x As Integer)");
    }

    [Fact]
    public void AssembleLogicalLine_ThreeContinuationLines_JoinsAll()
    {
        var lines = new[]
        {
            "Private Sub Foo( _",
            "    ByVal x As Integer, _",
            "    ByVal y As String)"
        };
        var result = VbAutoCloseProvider.AssembleLogicalLine(i => lines[i], 2);
        result.Should().Be("Private Sub Foo( ByVal x As Integer, ByVal y As String)");
    }

    [Fact]
    public void AssembleLogicalLine_NonContinuationPrecedingLines_IgnoresThem()
    {
        var lines = new[]
        {
            "Dim x As Integer",
            "Sub Foo()"
        };
        var result = VbAutoCloseProvider.AssembleLogicalLine(i => lines[i], 1);
        result.Should().Be("Sub Foo()");
    }

    // ── Indentation helpers ──────────────────────────────────────────────────

    [Theory]
    [InlineData("Sub Foo()", "")]
    [InlineData("    Sub Foo()", "    ")]
    [InlineData("        x = 1", "        ")]
    [InlineData("\tSub Foo()", "\t")]
    [InlineData("", "")]
    public void GetIndentation_ExtractsLeadingWhitespace(string line, string expected)
    {
        VbAutoCloseProvider.GetIndentation(line).Should().Be(expected);
    }

    [Theory]
    [InlineData("", "    ")]
    [InlineData("    ", "        ")]
    public void GetBodyIndent_AddsIndentSize(string current, string expected)
    {
        VbAutoCloseProvider.GetBodyIndent(current).Should().Be(expected);
    }

    // ── Inline comment stripping ─────────────────────────────────────────────

    [Fact]
    public void InlineComment_StrippedBeforeMatching()
    {
        // "If x Then  ' some comment" should match as multi-line If
        VbAutoCloseProvider.GetClosingStatement("If x > 0 Then ' comment").Should().Be("End If");
    }

    [Fact]
    public void QuotedApostrophe_NotTreatedAsComment()
    {
        // A string containing ' should not be treated as a comment
        VbAutoCloseProvider.GetClosingStatement("If x = \"it's\" Then").Should().Be("End If");
    }

    // ── Continuation + auto-close integration ────────────────────────────────

    [Fact]
    public void ContinuedSubDeclaration_AssemblesAndCloses()
    {
        var lines = new[]
        {
            "Public Sub LongName( _",
            "    ByVal param1 As Integer, _",
            "    ByVal param2 As String)"
        };
        var logicalLine = VbAutoCloseProvider.AssembleLogicalLine(i => lines[i], 2);
        VbAutoCloseProvider.GetClosingStatement(logicalLine).Should().Be("End Sub");
    }

    [Fact]
    public void ContinuedIfStatement_AssemblesAndCloses()
    {
        var lines = new[]
        {
            "If condition1 _",
            "    And condition2 Then"
        };
        var logicalLine = VbAutoCloseProvider.AssembleLogicalLine(i => lines[i], 1);
        VbAutoCloseProvider.GetClosingStatement(logicalLine).Should().Be("End If");
    }
}
