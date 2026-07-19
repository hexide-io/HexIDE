// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors

using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// Tests for VbScopeAnalyzer: Option Explicit / undeclared variable detection.
/// </summary>
public class VbScopeAnalyzerTests
{
    // ── No Option Explicit → never warn ──────────────────────────────────────

    [Fact]
    public void NoOptionExplicit_UndeclaredAssignment_NoDiagnostics()
    {
        const string code = """
            Sub Test()
                x = 42
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Option Explicit + all declared → no warnings ─────────────────────────

    [Fact]
    public void OptionExplicit_AllVariablesDeclared_NoDiagnostics()
    {
        const string code = """
            Option Explicit
            Sub Test()
                Dim x As Integer
                x = 42
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    [Fact]
    public void OptionExplicit_ModuleLevelDimUsedInSub_NoDiagnostics()
    {
        const string code = """
            Option Explicit
            Dim counter As Long

            Sub Increment()
                counter = counter + 1
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    [Fact]
    public void OptionExplicit_ConstantAssignmentTarget_NoDiagnostics()
    {
        // Const names should not be flagged if somehow referenced
        const string code = """
            Option Explicit
            Const MaxVal As Integer = 100

            Function GetMax() As Integer
                GetMax = MaxVal
            End Function
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Option Explicit + undeclared variable → warning ───────────────────────

    [Fact]
    public void OptionExplicit_UndeclaredVariable_OneWarning()
    {
        const string code = """
            Option Explicit
            Sub Test()
                x = 42
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().HaveCount(1);
        diags[0].Message.Should().Contain("'x'").And.Contain("not declared");
        diags[0].Severity.Should().Be(2); // Warning, not Error
    }

    [Fact]
    public void OptionExplicit_UndeclaredVariable_CorrectPosition()
    {
        const string code = "Option Explicit\r\nSub Test()\r\n    undeclaredVar = 5\r\nEnd Sub";
        // Line 2 (0-based), col 4

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        var warn = diags.Should().ContainSingle(d => d.Message.Contains("'undeclaredVar'")).Subject;
        warn.Range.Start.Line.Should().Be(2);     // 0-based
        warn.Range.Start.Character.Should().Be(4); // after 4 spaces
    }

    [Fact]
    public void OptionExplicit_MultipleUndeclaredVariables_MultipleWarnings()
    {
        const string code = """
            Option Explicit
            Sub Test()
                a = 1
                b = 2
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().HaveCount(2);
        diags.Should().Contain(d => d.Message.Contains("'a'"));
        diags.Should().Contain(d => d.Message.Contains("'b'"));
    }

    [Fact]
    public void OptionExplicit_SetStatement_UndeclaredVariable_Warning()
    {
        const string code = """
            Option Explicit
            Sub Test()
                Set obj = New Collection
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().ContainSingle(d => d.Message.Contains("'obj'"));
    }

    // ── Function return value assignment → no warning ─────────────────────────

    [Fact]
    public void OptionExplicit_FunctionReturnValueAssignment_NoDiagnostics()
    {
        // Assigning to the function name is the VB6 return value idiom
        const string code = """
            Option Explicit
            Function Add(a As Integer, b As Integer) As Integer
                Add = a + b
            End Function
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Procedure parameters → no warning ────────────────────────────────────

    [Fact]
    public void OptionExplicit_ParameterUsedAsAssignmentTarget_NoDiagnostics()
    {
        // ByRef parameters can be assigned to
        const string code = """
            Option Explicit
            Sub Swap(ByRef a As Integer, ByRef b As Integer)
                Dim tmp As Integer
                tmp = a
                a = b
                b = tmp
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Static variable → no warning ─────────────────────────────────────────

    [Fact]
    public void OptionExplicit_StaticVariable_NoDiagnostics()
    {
        const string code = """
            Option Explicit
            Sub Counter()
                Static count As Integer
                count = count + 1
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Public / Private module-level declarations → no warning ──────────────

    [Fact]
    public void OptionExplicit_PublicModuleVariable_NoDiagnostics()
    {
        const string code = """
            Option Explicit
            Public userName As String

            Sub SetUser(name As String)
                userName = name
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Mix: some declared, some not ─────────────────────────────────────────

    [Fact]
    public void OptionExplicit_MixOfDeclaredAndUndeclared_OnlyUndeclaredWarned()
    {
        const string code = """
            Option Explicit
            Sub Test()
                Dim declared As String
                declared = "ok"
                undeclared = "bad"
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().ContainSingle();
        diags[0].Message.Should().Contain("'undeclared'");
    }

    // ── Member access (dot notation) → no warning ────────────────────────────

    [Fact]
    public void OptionExplicit_MemberAccessAssignment_NoDiagnostics()
    {
        // x.Property = value — we only check simple name LHS, not member access
        const string code = """
            Option Explicit
            Sub Test()
                Dim x As Object
                x.Value = 42
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Syntax errors → scope analysis skipped ───────────────────────────────

    [Fact]
    public void SyntaxErrors_ScopeAnalysisSkipped_NoUndeclaredWarnings()
    {
        // @@ is an invalid character, so syntax errors are produced.
        // Even though z is undeclared, no undeclared-var warning should be emitted
        // because we skip scope analysis when syntax is broken.
        const string code = """
            Option Explicit
            Sub Test()
                @@
                z = 1
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        // Should only have syntax errors, not undeclared-var warnings
        diags.Should().NotBeEmpty();
        diags.Should().AllSatisfy(d => d.Severity.Should().Be(1, "only syntax errors"));
    }

    // ── Enum names → no warning ───────────────────────────────────────────────

    [Fact]
    public void OptionExplicit_EnumMembersUsedAsValues_NoDiagnostics()
    {
        const string code = """
            Option Explicit

            Enum Direction
                North
                South
            End Enum

            Sub Test()
                Dim d As Direction
                d = North
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Property Let / Set procedure name → no warning ───────────────────────

    [Fact]
    public void OptionExplicit_PropertyLetReturnAssignment_NoDiagnostics()
    {
        // Property procedures assign their "return" via the property name
        const string code = """
            Option Explicit
            Private mValue As String

            Property Get MyProp() As String
                MyProp = mValue
            End Property

            Property Let MyProp(val As String)
                mValue = val
            End Property
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }

    // ── Expression-level (RHS / conditions / arguments) checks ───────────────

    [Fact]
    public void OptionExplicit_UndeclaredInRhsExpression_Warning()
    {
        const string code = """
            Option Explicit
            Sub Test()
                Dim x As Integer
                x = y + 1
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().ContainSingle(d => d.Message.Contains("'y'"));
    }

    [Fact]
    public void OptionExplicit_UndeclaredInIfCondition_Warning()
    {
        const string code = """
            Option Explicit
            Sub Test()
                If undeclared > 0 Then
                    MsgBox "hi"
                End If
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().ContainSingle(d => d.Message.Contains("'undeclared'"));
    }

    [Fact]
    public void OptionExplicit_UndeclaredInFunctionArgument_Warning()
    {
        const string code = """
            Option Explicit
            Sub Test()
                MsgBox missingVar
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().ContainSingle(d => d.Message.Contains("'missingVar'"));
    }

    [Fact]
    public void OptionExplicit_UndeclaredSameNameMultipleLines_MultipleWarnings()
    {
        // Each occurrence at a unique position should get its own warning
        const string code = """
            Option Explicit
            Sub Test()
                If ghost > 0 Then
                    x = ghost + 1
                End If
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Where(d => d.Message.Contains("'ghost'")).Should().HaveCount(2);
    }

    [Fact]
    public void OptionExplicit_EnumTypeName_NoDiagnostic()
    {
        // The enum type name itself (Direction) must not be flagged
        const string code = """
            Option Explicit

            Enum Direction
                North = 0
                South = 1
            End Enum

            Sub Test()
                Dim d As Direction
                d = North
            End Sub
            """;

        var diags = VbDiagnosticsProvider.GetDiagnostics(code);

        diags.Should().BeEmpty();
    }
}
