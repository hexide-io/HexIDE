using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// <c>Sub Main</c> as a project's startup object — what <c>Startup="Sub Main"</c> in a .vbp selects, and
/// how a code-only Standard EXE begins. Every expectation measured against real vb6.exe
/// (<c>corpus/conformance/sub-main-startup.json</c>), issue #210.
///
/// <para>
/// <b>The lookup is NOT ordinary procedure resolution</b>, which is the whole reason this needed
/// measuring rather than delegating to <c>CallProcedure</c>. It searches every standard module and
/// <b>ignores visibility</b> — a <c>Private Sub Main</c> in a module other than the primary is a valid
/// startup, where ordinary resolution sees a foreign module's <c>Public</c> only and would have found
/// nothing. And two declarations of <c>Main</c> are ambiguous even when one is <c>Private</c> and the
/// other <c>Public</c>, which ordinary resolution would not treat as a clash at all.
/// </para>
///
/// <para>
/// <b>Why the interpreter had none of this.</b> HexIDE modelled the startup object as a nullable FORM
/// reference throughout, so a project with no forms could not run — F5 gave "Must have a startup form or
/// Sub Main()", a string that already promised the capability. The consequence for fidelity was larger
/// than the missing feature: 85 corpus cases measured against vb6.exe could not be gated, because their
/// entry point was never called, and the Integer-for-Long defect on ~700 in-box constants hid behind them
/// for exactly that reason.
/// </para>
/// </summary>
public class SubMainStartupTests : BaseVBTestFixture
{
    private async Task RunMain(string primary, params (string Name, string Code)[] modules)
    {
        var vb = new BasicInterpreter(new MockLib(debug), context, rootEnv, primary, "Module1",
            modules.Length == 0 ? null : modules);
        await vb.RunStartupSubMain();
    }

    private BasicInterpreter Build(string primary, params (string Name, string Code)[] modules) =>
        new(new MockLib(debug), context, rootEnv, primary, "Module1",
            modules.Length == 0 ? null : modules);

    private sealed class MockLib(List<Vb6Value> log) : IBasicStandardLibrary
    {
        public Task<HexIDE.IDE.MessageBoxResult> MsgBox(string t, string? c,
            HexIDE.IDE.MessageBoxButtons b, HexIDE.IDE.MessageBoxIcon i) => Task.FromResult(default(HexIDE.IDE.MessageBoxResult));
        public Task<string?> InputBox(string p, string? t, string d) => Task.FromResult<string?>(null);
        public void DebugPrint(Vb6Value v) { lock (log) log.Add(v); }
    }

    [Fact]
    public async Task RunsSubMainInTheOnlyModule()
    {
        await RunMain("Sub Main()\n    Debug.Print \"RAN\"\nEnd Sub\n");
        AssertDebugLog([new Vb6Value("RAN")]);
    }

    [Fact]
    public async Task FindsSubMainInAnotherModule()
    {
        // The startup is found PROJECT-WIDE — it is not tied to the module the project happens to name
        // first. Measured with Main in Module2 and nothing in Module1.
        await RunMain("Public Sub Helper()\nEnd Sub\n",
            ("Module2", "Sub Main()\n    Debug.Print \"FROM MODULE2\"\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value("FROM MODULE2")]);
    }

    [Fact]
    public async Task FindsAPrivateSubMainInAnotherModule()
    {
        // THE case that fixes the rule. `Private` in a FOREIGN module is still a valid startup, so the
        // search cannot be ordinary resolution — which would see Public only and refuse a project VB6
        // runs. A Private Main in the primary module proves nothing here, since own-module visibility
        // would explain it anyway.
        await RunMain("Public Sub Helper()\nEnd Sub\n",
            ("Module2", "Private Sub Main()\n    Debug.Print \"PRIVATE ELSEWHERE\"\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value("PRIVATE ELSEWHERE")]);
    }

    [Fact]
    public async Task APrivateSubMainInTheStartupModuleRuns()
    {
        await RunMain("Private Sub Main()\n    Debug.Print \"PRIVATE RAN\"\nEnd Sub\n");
        AssertDebugLog([new Vb6Value("PRIVATE RAN")]);
    }

    [Fact]
    public async Task TwoModulesDeclaringMainIsAmbiguous()
    {
        var act = async () => await RunMain("Sub Main()\nEnd Sub\n", ("Module2", "Sub Main()\nEnd Sub\n"));
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Ambiguous name detected: Main*");
    }

    [Fact]
    public async Task APrivateAndAPublicMainInTwoModulesIsStillAmbiguous()
    {
        // Ordinary resolution would not call this a clash — a Private procedure is module-local. The
        // startup search does, because it is choosing between two candidates rather than resolving a
        // reference. Measured.
        var act = async () => await RunMain("Private Sub Main()\nEnd Sub\n",
            ("Module2", "Public Sub Main()\nEnd Sub\n"));
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Ambiguous name detected: Main*");
    }

    [Fact]
    public async Task AFunctionNamedMainIsNotAStartup()
    {
        // The lookup is by KIND as well as name.
        var act = async () => await RunMain("Function Main() As Long\n    Main = 1\nEnd Function\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Must have startup form or Sub Main()*");
    }

    [Fact]
    public async Task ASubMainWithARequiredArgumentIsNotAStartup()
    {
        var act = async () => await RunMain("Sub Main(ByVal n As Long)\nEnd Sub\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Must have startup form or Sub Main()*");
    }

    [Fact]
    public async Task ASubMainWithAnOptionalArgumentIsAlsoNotAStartup()
    {
        // The distinction that had to be measured rather than reasoned: it is not "must be callable with
        // no arguments", it is "must declare none". An all-Optional parameter list still disqualifies.
        var act = async () => await RunMain("Sub Main(Optional ByVal n As Long = 7)\nEnd Sub\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Must have startup form or Sub Main()*");
    }

    [Fact]
    public async Task NoMainAtAllIsRefused()
    {
        var act = async () => await RunMain("Public Sub Helper()\n    Debug.Print \"NEVER\"\nEnd Sub\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .WithMessage("*Must have startup form or Sub Main()*");
        debug.Should().BeEmpty("nothing should have run");
    }

    [Fact]
    public async Task SubMainMayCallAProcedureInAnotherModule()
    {
        // The ordinary shape of a code-only project, and what the 12 multi-module corpus cases need:
        // entering through Main leaves cross-module resolution working normally.
        await RunMain("Sub Main()\n    Module2.Work\n    Debug.Print \"DONE\"\nEnd Sub\n",
            ("Module2", "Public Sub Work()\n    Debug.Print \"WORKED\"\nEnd Sub\n"));
        AssertDebugLog([new Vb6Value("WORKED"), new Vb6Value("DONE")]);
    }

    [Fact]
    public async Task ModuleLevelDeclarationsAreHoistedBeforeMainRuns()
    {
        // A regression guard for a wrong value this change INTRODUCED and the gate caught: entering
        // through Main skipped the declaration hoisting that Execute() does as a side effect of running
        // top-level blocks, so the startup module's own Const read as Empty. Ten corpus cases moved from
        // wrong to right when it was fixed, which is more than the two that made it visible.
        await RunMain("Private Const LIMIT = 7\n\nSub Main()\n    Debug.Print LIMIT\nEnd Sub\n");
        AssertDebugLog([new Vb6Value(7)]);
    }

    [Fact]
    public async Task ADeclarationInAnotherModuleIsHoistedToo()
    {
        await RunMain("Sub Main()\n    Debug.Print Module2.SHARED_LIMIT\nEnd Sub\n",
            ("Module2", "Public Const SHARED_LIMIT = 42\n"));
        AssertDebugLog([new Vb6Value(42)]);
    }

    [Fact]
    public void HasStartupSubMainAgreesWithTheRunRules()
    {
        // The IDE needs to know whether to OFFER Sub Main in the Startup Object list, and it must agree
        // with what running would do — an offer that then refuses to run is worse than no offer.
        Build("Sub Main()\nEnd Sub\n").HasStartupSubMain().Should().BeTrue();
        Build("Private Sub Main()\nEnd Sub\n").HasStartupSubMain().Should().BeTrue();
        Build("Public Sub Helper()\nEnd Sub\n").HasStartupSubMain().Should().BeFalse();
        Build("Function Main() As Long\nEnd Function\n").HasStartupSubMain().Should().BeFalse();
        Build("Sub Main(ByVal n As Long)\nEnd Sub\n").HasStartupSubMain().Should().BeFalse();
        Build("Sub Main(Optional ByVal n As Long = 1)\nEnd Sub\n").HasStartupSubMain().Should().BeFalse();
        Build("Sub Main()\nEnd Sub\n", ("Module2", "Sub Main()\nEnd Sub\n"))
            .HasStartupSubMain().Should().BeFalse("two candidates is ambiguous, so not usable");
        Build("Public Sub Helper()\nEnd Sub\n", ("Module2", "Private Sub Main()\nEnd Sub\n"))
            .HasStartupSubMain().Should().BeTrue("a Private Main in a foreign module IS a valid startup");
    }
}
