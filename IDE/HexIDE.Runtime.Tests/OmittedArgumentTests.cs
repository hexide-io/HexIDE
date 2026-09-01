using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Issue #135 — a blank argument keeps its place.
///
/// <para>VB6 lets a caller skip a positional argument by leaving it blank: <c>Foo 1, , 3</c>. The
/// grammar's <c>argsCall</c> makes each <c>argCall</c> optional, so a blank produced <b>no parse node at
/// all</b> — and reading <c>context.argCall()</c> yielded only the arguments that were written. Everything
/// after a blank therefore shifted one position left and bound to the wrong parameter, silently.</para>
///
/// <para>It was found through <c>MsgBox "hello", , vbCritical</c>, which set the <i>icon</i>: a constant
/// written in the Title position arriving as Buttons. But the argument list is built generically, so this
/// was never a MsgBox defect — it reached every call using that form.</para>
///
/// <para>A blank slot now holds <c>Vb6Value.Missing</c>, which is deliberately its own type rather than
/// <c>Empty</c>. The binder has to tell "not supplied" from "supplied as Empty": conflating them would
/// override an <c>Optional</c> parameter's declared default with Empty, trading one silent
/// wrong-value bug for another.</para>
/// </summary>
public class OmittedArgumentTests : BaseVBTestFixture
{
    [Fact]
    public async Task An_argument_after_a_blank_keeps_its_position()
    {
        // The heart of it: 3 must arrive as the THIRD argument, not the second.
        await Run("""
            Sub Show(a, Optional b, Optional c)
                Debug.Print a
                Debug.Print c
            End Sub
            Show 1, , 3
            """);

        AssertDebugLog([1, 3]);
    }

    [Fact]
    public async Task A_blank_slot_takes_the_parameters_declared_default()
    {
        // Position alone is not enough. A blank is "not supplied", so an Optional parameter must still
        // fall back to its default — if the placeholder were treated as a value, b would come out Empty.
        await Run("""
            Sub Show(a, Optional b = 99, Optional c = 0)
                Debug.Print b
            End Sub
            Show 1, , 3
            """);

        AssertDebugLog([99]);
    }

    [Fact]
    public async Task Several_blanks_each_hold_their_own_place()
    {
        await Run("""
            Sub Show(a, Optional b = 11, Optional c = 22, Optional d = 33)
                Debug.Print a
                Debug.Print b
                Debug.Print c
                Debug.Print d
            End Sub
            Show 1, , , 4
            """);

        AssertDebugLog([1, 11, 22, 4]);
    }

    [Fact]
    public async Task A_trailing_blank_is_still_a_slot()
    {
        await Run("""
            Sub Show(a, Optional b = 7)
                Debug.Print b
            End Sub
            Show 1,
            """);

        AssertDebugLog([7]);
    }

    [Fact]
    public async Task Calls_with_no_blanks_are_unaffected()
    {
        // The slot walk rebuilds every call's arguments, not just those containing a blank, so the
        // ordinary case has to keep working — this is the regression guard for that rewrite.
        await Run("""
            Sub Show(a, b, c)
                Debug.Print a
                Debug.Print b
                Debug.Print c
            End Sub
            Show 1, 2, 3
            """);

        AssertDebugLog([1, 2, 3]);
    }

    [Fact]
    public async Task A_single_argument_call_is_unaffected()
    {
        await Run("""
            Sub Show(a)
                Debug.Print a
            End Sub
            Show 42
            """);

        AssertDebugLog([42]);
    }

    [Fact]
    public async Task A_blank_for_a_required_parameter_is_an_error()
    {
        // "Not supplied" has to mean the same thing however it was written: leaving the slot blank is no
        // more legal than stopping short of a required parameter.
        var act = async () => await Run("""
            Sub Show(a, b)
                Debug.Print b
            End Sub
            Show 1,
            """);

        await act.Should().ThrowAsync<VBCompileErrorException>()
            .WithMessage("*not optional*");
    }

    [Fact]
    public async Task A_function_call_in_an_expression_honours_blanks_too()
    {
        // EvaluateCallArgs and ResolveCallArgs are separate paths — expressions go through the first,
        // statement calls the second, and both had the defect.
        await Run("""
            Function Pick(a, Optional b = 5, Optional c = 6)
                Pick = c
            End Function
            Debug.Print Pick(1, , 3)
            """);

        AssertDebugLog([3]);
    }
}
