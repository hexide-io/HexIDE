using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Interpreter-advanced Phase 1 — project-aware execution (the multi-module registry). Cross-module procedure
/// resolution (unqualified + qualified), library qualifiers (VBA/VB, incl. the transparent library.module.member
/// chain), VB6 precedence (current module first, other modules' Public, ambiguity → error, user Public shadows a
/// builtin), ByRef + mutual recursion across modules, and the per-activation `With` isolation this phase folds in.
/// Scoped to a single execution context; cross-*form* shared module state is deferred (spec limitation E1).
/// </summary>
public class MultiModuleTests : BaseVBTestFixture
{
    [Fact]
    public async Task CrossModule_UnqualifiedPublicSub()
    {
        await RunModules(
            "PrintGreeting\n",
            ("Module2", "Public Sub PrintGreeting()\nDebug.Print 42\nEnd Sub\n"));
        AssertDebugLog([42]);
    }

    [Fact]
    public async Task CrossModule_UnqualifiedPublicFunction()
    {
        await RunModules(
            "Debug.Print Add(2, 3)\n",
            ("MathHelpers", "Public Function Add(a As Integer, b As Integer) As Integer\nAdd = a + b\nEnd Function\n"));
        AssertDebugLog([5]);
    }

    [Fact]
    public async Task Qualified_ModuleDotSub_Statement()
    {
        await RunModules(
            "Module2.Greet\n",
            ("Module2", "Public Sub Greet()\nDebug.Print 7\nEnd Sub\n"));
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task Qualified_ModuleDotFunction_Expression()
    {
        await RunModules(
            "Debug.Print MathHelpers.Add(10, 20)\n",
            ("MathHelpers", "Public Function Add(a As Integer, b As Integer) As Integer\nAdd = a + b\nEnd Function\n"));
        AssertDebugLog([30]);
    }

    [Fact]
    public async Task Qualified_SelfModule()
    {
        // The primary module is registered as "Module1", so it can qualify its own members.
        await Run("Module1.Show\nSub Show()\nDebug.Print 9\nEnd Sub\n");
        AssertDebugLog([9]);
    }

    [Fact]
    public async Task Qualified_PublicConstCrossModule()
    {
        // The per-module init pass runs Consts' top-level, filling MaxItems before the primary body reads it.
        await RunModules(
            "Debug.Print Consts.MaxItems\n",
            ("Consts", "Public Const MaxItems = 100\n"));
        AssertDebugLog([100]);
    }

    [Fact]
    public async Task CurrentModuleWins_OverOtherModulePublic()
    {
        // Module1 (primary) defines its own Foo; Module2 also has a Public Foo. From Module1, its own wins — no
        // ambiguity, because the current module is consulted before the global namespace.
        await RunModules(
            "Sub Foo()\nDebug.Print 1\nEnd Sub\nFoo\n",
            ("Module2", "Public Sub Foo()\nDebug.Print 2\nEnd Sub\n"));
        AssertDebugLog([1]);
    }

    [Fact]
    public async Task UserPublicFunction_ShadowsBuiltin_CrossModule()
    {
        // A user Public Function named Left in another module beats the VBA intrinsic (builtins resolve last).
        await RunModules(
            "Debug.Print Left(\"ignored\", 1)\n",
            ("Overrides", "Public Function Left(s As String, n As Integer) As String\nLeft = \"USER\"\nEnd Function\n"));
        AssertDebugLog(["USER"]);
    }

    [Fact]
    public async Task Ambiguity_TwoPublicSameName_Throws()
    {
        Func<Task> act = () => RunModules(
            "DoThing\n",
            ("ModuleA", "Public Sub DoThing()\nDebug.Print 1\nEnd Sub\n"),
            ("ModuleB", "Public Sub DoThing()\nDebug.Print 2\nEnd Sub\n"));
        (await act.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Contain("Ambiguous");
    }

    [Fact]
    public async Task Private_NotVisibleViaQualifier()
    {
        // A Private Sub in another module is not reachable as Module2.Helper.
        Func<Task> act = () => RunModules(
            "Module2.Helper\n",
            ("Module2", "Private Sub Helper()\nDebug.Print 1\nEnd Sub\n"));
        await act.Should().ThrowAsync<VBRunTimeException>();
    }

    [Fact]
    public async Task LibraryQualifier_VBA_Abs()
    {
        await Run("Debug.Print VBA.Abs(-5)\n");
        AssertDebugLog([5]);
    }

    [Fact]
    public async Task LibraryQualifier_VBA_Math_Abs_TransparentSegment()
    {
        // The intermediate `Math` module segment is transparent: VBA.Math.Abs ≡ VBA.Abs.
        await Run("Debug.Print VBA.Math.Abs(-7)\n");
        AssertDebugLog([7]);
    }

    [Fact]
    public async Task LibraryQualifier_Constant()
    {
        // VBRUN, not VBA. `vbKeyA` is declared by VBRUN.KeyCodeConstants and by nothing else, and
        // measured, `VBA.vbKeyA` is ILLEGAL in VB6 — "Method or data member not found". This test used
        // to assert `VBA.vbKeyA` = 65 and passed only because the library qualifier was resolved
        // transparently: it was pinning a false acceptance, not a fact about VB6.
        await Run("Debug.Print VBRUN.vbKeyA\n");
        // Long, not Integer and not Double. The type libraries declare these I4 and VB6 reports TypeName
        // "Long" even for small values; the old flat table built them through Vb6Value(int) and its
        // magnitude rule, so they came back Integer. Spelled out rather than written `65L`, because a
        // long literal in a collection expression widens to double and would assert Double instead.
        AssertDebugLog([new Vb6Value(65L)]);
    }

    [Fact]
    public async Task ByRef_AcrossModules()
    {
        // A ByRef param in Module2 aliases a variable owned by the primary module (slots are program-global).
        await RunModules(
            "Dim x\nx = 10\nModule2.Triple x\nDebug.Print x\n",
            ("Module2", "Public Sub Triple(ByRef n)\nn = n * 3\nEnd Sub\n"));
        AssertDebugLog([30]);
    }

    [Fact]
    public async Task CrossModuleMutualRecursion()
    {
        // IsEven (primary) calls IsOdd (Module2), which calls IsEven back — the current module threads correctly
        // per activation across the boundary.
        await RunModules(
            "Debug.Print IsEven(4)\n" +
            "Function IsEven(n As Integer) As Boolean\n" +
            "If n = 0 Then\nIsEven = True\nElse\nIsEven = IsOdd(n - 1)\nEnd If\n" +
            "End Function\n",
            ("Module2", "Public Function IsOdd(n As Integer) As Boolean\n" +
                        "If n = 0 Then\nIsOdd = False\nElse\nIsOdd = IsEven(n - 1)\nEnd If\n" +
                        "End Function\n"));
        AssertDebugLog([true]);
    }

    [Fact]
    public async Task With_DoesNotLeakIntoCallee_IsError91()
    {
        // A `With` in the caller must NOT be visible inside a called Sub — a leading `.Print` there is Error 91.
        // (This is the per-activation With fix folded into Phase 1.)
        Func<Task> act = () => Run(
            "Sub UsesLeadingDot()\n.Print 1\nEnd Sub\n" +
            "With Debug\nUsesLeadingDot\nEnd With\n");
        (await act.Should().ThrowAsync<VBRunTimeException>()).Which.Error.ErrNo.Should().Be(91);
    }

    // --- regression tests for the Phase-1 code-review findings ---

    [Fact]
    public async Task QualifiedMember_BaseTypeTokenHead_DoesNotNRE()
    {
        // `.Currency` lexes as the CURRENCY baseType token (not an ambiguousIdentifier), so the member name
        // accessor must fall through to a clean compile error, never a NullReferenceException.
        Func<Task> act = () => Run("Dim x\nx = VBA.Currency(5)\n");
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task LibraryQualified_UnknownMember_Statement_Errors()
    {
        // A library-qualified unknown member invoked as a STATEMENT must error (as the expression form does),
        // not silently no-op.
        Func<Task> act = () => Run("VBA.NoSuchThing 5\n");
        await act.Should().ThrowAsync<VBRunTimeException>();
    }

    [Fact]
    public async Task Err_ConsistentAcrossRecompile()
    {
        // The fixture shares one context + rootEnv across Run() calls (a recompile-over-reused-rootEnv). The
        // second run's trap must write the same Err object user code reads — regression for the shared-slot
        // refresh that keeps the Err field and slot one instance.
        await Run("On Error Resume Next\nErr.Raise 5\n");
        await Run("On Error Resume Next\nErr.Raise 6\nDebug.Print Err.Number\n");
        AssertDebugLog([new Vb6Value(6L)]);
    }
}
