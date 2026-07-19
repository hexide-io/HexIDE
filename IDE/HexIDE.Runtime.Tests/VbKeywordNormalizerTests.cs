// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.Runtime.Tests.

using HexIDE.Runtime.Editor;

namespace HexIDE.Runtime.Tests;

public class VbKeywordNormalizerTests
{
    // ── Basic keyword casing ─────────────────────────────────────────────────

    [Theory]
    [InlineData("dim x as integer", "Dim x As Integer")]
    [InlineData("private sub foo()", "Private Sub foo()")]
    [InlineData("public function bar() as string", "Public Function bar() As String")]
    [InlineData("if x > 0 then", "If x > 0 Then")]
    [InlineData("for i = 1 to 10 step 2", "For i = 1 To 10 Step 2")]
    [InlineData("do while x > 0", "Do While x > 0")]
    [InlineData("select case x", "Select Case x")]
    [InlineData("end sub", "End Sub")]
    [InlineData("end function", "End Function")]
    [InlineData("end if", "End If")]
    [InlineData("exit sub", "Exit Sub")]
    [InlineData("exit function", "Exit Function")]
    [InlineData("on error goto handler", "On Error GoTo handler")]
    [InlineData("option explicit", "Option Explicit")]
    public void Keywords_NormalizedToPascalCase(string input, string expected)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().Be(expected);
    }

    // ── Already correct — returns null ───────────────────────────────────────

    [Theory]
    [InlineData("Dim x As Integer")]
    [InlineData("Private Sub Foo()")]
    [InlineData("End Sub")]
    [InlineData("")]
    public void AlreadyCorrect_ReturnsNull(string input)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().BeNull();
    }

    // ── Non-keywords left alone ──────────────────────────────────────────────

    [Theory]
    [InlineData("myVariable = 42")]
    [InlineData("Call MyFunction(x, y)")]
    public void NonKeywords_Untouched(string input)
    {
        // "Call" is a keyword but already correct; variables are not keywords
        VbKeywordNormalizer.NormalizeLine(input).Should().BeNull();
    }

    [Fact]
    public void UserIdentifiers_NotChanged()
    {
        // "myDim" contains "dim" as a substring — must NOT be changed
        var result = VbKeywordNormalizer.NormalizeLine("myDim = 1");
        result.Should().BeNull();
    }

    // ── String literals preserved ────────────────────────────────────────────

    [Theory]
    [InlineData("x = \"dim as integer\"", null)]
    [InlineData("dim x as string", "Dim x As String")]
    public void StringLiterals_NotTouched(string input, string? expected)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().Be(expected);
    }

    [Fact]
    public void MixedKeywordsAndStrings_OnlyKeywordsNormalized()
    {
        var result = VbKeywordNormalizer.NormalizeLine("dim x as string : x = \"hello dim world\"");
        // Keywords outside string corrected; string content untouched
        result.Should().Be("Dim x As String : x = \"hello dim world\"");
    }

    // ── Comments preserved ───────────────────────────────────────────────────

    [Fact]
    public void CommentContent_NotTouched()
    {
        var result = VbKeywordNormalizer.NormalizeLine("dim x as integer ' this is a dim comment");
        result.Should().Be("Dim x As Integer ' this is a dim comment");
    }

    [Fact]
    public void FullLineComment_NotChanged()
    {
        var result = VbKeywordNormalizer.NormalizeLine("' dim x as integer");
        result.Should().BeNull();
    }

    // ── Indentation preserved ────────────────────────────────────────────────

    [Fact]
    public void LeadingWhitespace_Preserved()
    {
        var result = VbKeywordNormalizer.NormalizeLine("    dim x as integer");
        result.Should().Be("    Dim x As Integer");
    }

    // ── Built-in functions ───────────────────────────────────────────────────

    [Theory]
    [InlineData("x = len(s)", "x = Len(s)")]
    [InlineData("x = ucase(s)", "x = UCase(s)")]
    [InlineData("x = instr(1, s, \"x\")", "x = InStr(1, s, \"x\")")]
    [InlineData("msgbox \"hello\"", "MsgBox \"hello\"")]
    [InlineData("if isnull(x) then", "If IsNull(x) Then")]
    public void BuiltInFunctions_Normalized(string input, string expected)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().Be(expected);
    }

    // ── Type keywords ────────────────────────────────────────────────────────

    [Theory]
    [InlineData("dim x as boolean", "Dim x As Boolean")]
    [InlineData("dim x as long", "Dim x As Long")]
    [InlineData("dim x as variant", "Dim x As Variant")]
    [InlineData("dim x as double", "Dim x As Double")]
    [InlineData("dim x as currency", "Dim x As Currency")]
    public void TypeKeywords_Normalized(string input, string expected)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().Be(expected);
    }

    // ── Operator keywords ────────────────────────────────────────────────────

    [Theory]
    [InlineData("if x and y or z then", "If x And y Or z Then")]
    [InlineData("if not x then", "If Not x Then")]
    [InlineData("x = a mod b", "x = a Mod b")]
    [InlineData("if x like \"*test*\" then", "If x Like \"*test*\" Then")]
    public void OperatorKeywords_Normalized(string input, string expected)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().Be(expected);
    }

    // ── Declare statement ────────────────────────────────────────────────────

    [Fact]
    public void DeclareStatement_KeywordsNormalized()
    {
        var result = VbKeywordNormalizer.NormalizeLine(
            "private declare function GetTickCount lib \"kernel32\" () as long");
        result.Should().Be(
            "Private Declare Function GetTickCount Lib \"kernel32\" () As Long");
    }

    // ── Property procedures ──────────────────────────────────────────────────

    [Theory]
    [InlineData("public property get myName() as string", "Public Property Get myName() As String")]
    [InlineData("property let myName(byval value as string)", "Property Let myName(ByVal value As String)")]
    [InlineData("property set obj(byval value as object)", "Property Set obj(ByVal value As Object)")]
    public void PropertyProcedures_KeywordsNormalized(string input, string expected)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().Be(expected);
    }

    // ── Literals / special values ────────────────────────────────────────────

    [Theory]
    [InlineData("x = true", "x = True")]
    [InlineData("x = false", "x = False")]
    [InlineData("x = nothing", "x = Nothing")]
    [InlineData("if x is nothing then", "If x Is Nothing Then")]
    [InlineData("if isempty(x) then", "If IsEmpty(x) Then")]
    public void SpecialValues_Normalized(string input, string expected)
    {
        VbKeywordNormalizer.NormalizeLine(input).Should().Be(expected);
    }

    // ── ALL CAPS input ───────────────────────────────────────────────────────

    [Fact]
    public void AllCapsInput_NormalizedToPascalCase()
    {
        var result = VbKeywordNormalizer.NormalizeLine("DIM X AS INTEGER");
        result.Should().Be("Dim X As Integer");
    }

    // ── Null / empty ─────────────────────────────────────────────────────────

    [Fact]
    public void NullInput_ReturnsNull()
    {
        VbKeywordNormalizer.NormalizeLine(null!).Should().BeNull();
    }

    [Fact]
    public void EmptyInput_ReturnsNull()
    {
        VbKeywordNormalizer.NormalizeLine("").Should().BeNull();
    }

    [Fact]
    public void WhitespaceOnly_ReturnsNull()
    {
        VbKeywordNormalizer.NormalizeLine("    ").Should().BeNull();
    }
}
