using System;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Tests.IDE;

/// <summary>
/// The text the runtime-error dialog shows. A form and Sub Main now share it, and before #754 Sub Main showed a
/// VB6 error without the statement that raised it.
/// </summary>
public class RunErrorTextTests
{
    private static ILocalizationService Localization()
    {
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString("Str.ProjectRunner.NotImplementedYet").Returns("Not implemented yet: {0}");
        return localization;
    }

    [Fact]
    public void Something_HexIDE_does_not_implement_says_so_and_carries_no_error_number()
    {
        // VB6 would have run it, so a VB6 error number would blame the program for HexIDE's gap.
        var text = RunErrorText.ForFault(new NotImplementedException("GoSub not implemented"), Localization());

        text.Should().Be("Not implemented yet: GoSub not implemented");
    }

    [Theory]
    [InlineData("AppActivate \"Notepad\"\n")]
    [InlineData("Sub Probe()\n    AppActivate \"Notepad\"\nEnd Sub\nProbe\n")]
    [InlineData("Sub Probe()\n    On Error Resume Next\n    If True Then\n        AppActivate \"Notepad\"\n    End If\nEnd Sub\nProbe\n")]
    [InlineData("Sub Probe()\n    On Error GoTo Handler\n    AppActivate \"Notepad\"\nHandler:\nEnd Sub\nProbe\n")]
    public async Task An_unimplemented_construct_names_its_innermost_statement(string code)
    {
        var interpreter = new BasicInterpreter(Substitute.For<IBasicStandardLibrary>(),
            new ModuleExecutionContext(), new ExecutionEnvironment(), code);
        var act = async () => await interpreter.Execute();

        var raised = (await act.Should().ThrowAsync<NotImplementedException>()).Which;

        RunErrorText.ForFault(raised, Localization()).ReplaceLineEndings("\n").Should().Be(
            "Not implemented yet: AppActivate not implemented\n\nat AppActivate \"Notepad\"");
    }

    [Fact]
    public void Any_other_fault_is_VB6s_internal_error_with_what_a_bug_report_needs()
    {
        var text = RunErrorText.ForFault(new InvalidOperationException("the engine fell over"), Localization());

        text.Should().Be("Run-time error '51':\n\nInternal error\n\n"
                         + "System.InvalidOperationException: the engine fell over");
    }

    [Fact]
    public async Task A_VB6_runtime_error_names_the_whole_statement_that_raised_it()
    {
        // ANTLR's StopIndex is inclusive. Reading it as exclusive cut the last character off every statement,
        // which is why this asserts the whole statement rather than only that an "at" line exists (#220).
        // `Error "x"` rather than `Error 5`: a raised number goes through Err.Raise, which has no statement to
        // name, while a non-numeric argument is a type mismatch raised at the statement itself.
        var interpreter = new BasicInterpreter(Substitute.For<IBasicStandardLibrary>(),
            new ModuleExecutionContext(), new ExecutionEnvironment(), "Error \"x\"\n");
        VBRunTimeException? raised = null;
        try
        {
            await interpreter.Execute();
        }
        catch (VBRunTimeException e)
        {
            raised = e;
        }

        raised.Should().NotBeNull();
        RunErrorText.For(raised!).Should().Be("Run-time error '13':\n\nType mismatch\n\nat Error \"x\"");
    }
}
