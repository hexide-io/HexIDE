using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// An error carries the module and line it was raised at (#955). The dialog named the statement but not where it is, and
/// once it closed neither a person nor get_last_runtime_error could reach the line.
/// </summary>
public class ErrorLocationTests : BaseVBTestFixture
{
    private async Task<ErrorLocation?> RaisedAt(Func<Task> run)
    {
        Exception? raised = null;
        try { await run(); }
        catch (Exception e) { raised = e; }
        raised.Should().NotBeNull("the program raises");
        return ErrorLocation.Of(raised);
    }

    [Fact]
    public async Task An_error_in_a_called_procedure_is_placed_where_it_was_raised_not_at_the_call()
    {
        var at = await RaisedAt(() => RunModules(
            "Dim n As Integer\nn = 1\nModule2.Divide\n",
            ("Module2", "Public Sub Divide()\n    Dim d As Integer\n    Debug.Print 10 \\ d\nEnd Sub\n")));

        at.Should().Be(new ErrorLocation("Module2", 3));
    }

    [Fact]
    public async Task An_error_in_a_nested_block_is_placed_on_its_own_line()
    {
        var at = await RaisedAt(() => Run("Dim d As Integer\nIf True Then\n    d = 0\n    Debug.Print 1 / d\nEnd If\n"));

        at.Should().Be(new ErrorLocation("Module1", 4));
    }

    [Fact]
    public async Task An_error_in_an_ElseIf_condition_is_placed_on_the_ElseIf()
    {
        var at = await RaisedAt(() => Run("Dim d As Integer\nIf False Then\n    d = 1\nElseIf 1 / d > 0 Then\n    d = 2\nEnd If\n"));

        at.Should().Be(new ErrorLocation("Module1", 4));
    }

    [Fact]
    public async Task An_error_in_a_single_line_If_tail_is_placed_on_that_line()
    {
        var at = await RaisedAt(() => Run("Dim d As Integer\n\nIf True Then d = 1: Debug.Print 1 \\ 0\n"));

        at.Should().Be(new ErrorLocation("Module1", 3));
    }

    [Fact]
    public async Task An_error_a_built_in_raises_is_placed_on_the_statement_that_called_it()
    {
        var at = await RaisedAt(() => Run("Dim i As Integer\ni = CInt(40000)\n"));

        at.Should().Be(new ErrorLocation("Module1", 2));
    }

    // As the IDE runs, with a halt for an error nothing handles: placed on the line the debugger stops on.
    private async Task<ErrorLocation?> HaltedAt(string code)
    {
        var (vb, dbg) = NewDebuggable(code);
        var halts = new List<ErrorLocation?>();
        dbg.HaltHost = e =>
        {
            halts.Add(ErrorLocation.Of(e));
            return Task.FromResult(false);   // End
        };
        try { await vb.RunStartupSubMain(); }
        catch (Exception) { /* End unwinds the run */ }
        return halts.Should().ContainSingle().Subject;
    }

    [Theory]
    [InlineData("    Debug.Print 1 \\ d\n", 3)]
    [InlineData("    If 1 / d > 0 Then\n        d = 1\n    End If\n", 3)]
    [InlineData("    If False Then\n        d = 1\n    ElseIf 1 / d > 0 Then\n        d = 2\n    End If\n", 5)]
    [InlineData("    If 1 / d Then d = 1\n", 3)]
    [InlineData("    Do While 1 / d\n    Loop\n", 3)]
    [InlineData("    Do\n        d = d\n    Loop Until 1 / d\n", 5)]
    [InlineData("    Debug.Print 1 + _\n        1 \\ d\n", 3)]
    public async Task A_halted_error_is_placed_where_the_debugger_stops(string body, int line)
    {
        var at = await HaltedAt("Sub Main()\n    Dim d As Integer\n" + body + "End Sub\n");

        at.Should().Be(new ErrorLocation("Module1", line));
    }

    // Raised from a statement loop's own catch, where its filter cannot see it: a handler reached by falling into it.
    [Fact]
    public async Task Resume_without_an_error_is_placed_on_the_Resume_not_at_the_call()
    {
        var at = await RaisedAt(() => RunModules(
            "Dim n As Integer\nModule2.Fall\n",
            ("Module2", "Public Sub Fall()\n    Dim n As Integer\n    Resume Next\nEnd Sub\n")));

        at.Should().Be(new ErrorLocation("Module2", 3));
    }

    [Fact]
    public async Task A_compile_error_met_mid_run_is_placed_too()
    {
        var at = await RaisedAt(() => Run("Debug.Print 1\nNoSuchProcedure\n"));

        at.Should().Be(new ErrorLocation("Module1", 2));
    }
}
