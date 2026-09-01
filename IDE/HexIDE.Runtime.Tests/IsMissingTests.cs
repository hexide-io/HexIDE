using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>IsMissing</c>, and what an omitted argument actually is.
///
/// <para>Every expectation is measured against real <c>vb6.exe</c> — see <i>IsMissing and the shape of an
/// omitted argument</i> in <c>docs/vb6-fidelity-oracle.md</c>. The measurements matter because the obvious
/// guesses are wrong twice over: an omitted argument is <b>not</b> Empty, and an <c>Optional</c> with a
/// declared type or a default is <b>never</b> missing.</para>
///
/// <para>It became expressible only once #135 gave a blank argument its own value rather than dropping it,
/// which is why this sits on top of that work.</para>
/// </summary>
public class IsMissingTests : BaseVBTestFixture
{
    [Fact]
    public async Task An_omitted_Optional_is_missing()
    {
        await Run("""
            Function Probe(a, Optional v)
                Probe = IsMissing(v)
            End Function
            Debug.Print Probe(1)
            """);

        AssertDebugLog([true]);
    }

    [Fact]
    public async Task A_supplied_Optional_is_not_missing()
    {
        await Run("""
            Function Probe(a, Optional v)
                Probe = IsMissing(v)
            End Function
            Debug.Print Probe(1, 2)
            """);

        AssertDebugLog([false]);
    }

    [Fact]
    public async Task An_Optional_with_a_default_is_never_missing()
    {
        // The default supplies it, so nothing is missing — and the value is the default, not Missing.
        await Run("""
            Function Probe(a, Optional v = 5)
                Probe = CStr(IsMissing(v)) & " " & CStr(v)
            End Function
            Debug.Print Probe(1)
            """);

        AssertDebugLog(["False 5"]);
    }

    [Fact]
    public async Task A_typed_Optional_is_never_missing()
    {
        // There is no Missing to hold in an Integer: an omitted typed Optional gets the type's zero.
        await Run("""
            Function Probe(a, Optional n As Integer)
                Probe = CStr(IsMissing(n)) & " " & CStr(n)
            End Function
            Debug.Print Probe(1)
            """);

        AssertDebugLog(["False 0"]);
    }

    [Fact]
    public async Task A_required_parameter_is_never_missing()
    {
        await Run("""
            Function Probe(a)
                Probe = IsMissing(a)
            End Function
            Debug.Print Probe(1)
            """);

        AssertDebugLog([false]);
    }

    [Fact]
    public async Task A_blank_argument_in_the_middle_is_missing()
    {
        // Ties back to #135: the blank keeps its position, and what sits there is Missing — so the
        // parameter it lands on reports missing while the one after it does not.
        await Run("""
            Function Probe(a, Optional b, Optional c)
                Probe = CStr(IsMissing(b)) & " " & CStr(IsMissing(c))
            End Function
            Debug.Print Probe(1, , 3)
            """);

        AssertDebugLog(["True False"]);
    }

    [Fact]
    public async Task A_missing_value_reports_as_an_Error_variant()
    {
        // NOT "Empty"/0. VB6 gives an omitted argument the vbError subtype, which is exactly the
        // distinction that makes IsMissing expressible at all.
        await Run("""
            Function Probe(a, Optional v)
                Probe = TypeName(v) & " " & CStr(VarType(v))
            End Function
            Debug.Print Probe(1)
            """);

        AssertDebugLog(["Error 10"]);
    }

    [Fact]
    public async Task A_missing_value_is_neither_Empty_nor_Null()
    {
        await Run("""
            Function Probe(a, Optional v)
                Probe = CStr(IsEmpty(v)) & " " & CStr(IsNull(v))
            End Function
            Debug.Print Probe(1)
            """);

        AssertDebugLog(["False False"]);
    }

    [Fact]
    public async Task A_missing_value_is_an_error_value()
    {
        // Measured True. Missing is the only vbError value the model has; CVErr is still unimplemented.
        await Run("""
            Function Probe(a, Optional v)
                Probe = IsError(v)
            End Function
            Debug.Print Probe(1)
            """);

        AssertDebugLog([true]);
    }

    [Fact]
    public async Task IsError_is_still_False_for_ordinary_values()
    {
        await Run("""
            Debug.Print IsError(5)
            Debug.Print IsError("x")
            Debug.Print IsError(Empty)
            """);

        AssertDebugLog([false, false, false]);
    }
}
