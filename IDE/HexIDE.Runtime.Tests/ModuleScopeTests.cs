using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// Module scope for Types and Enums — issue #180, and the measurements that turned it from a guess into a
/// specification.
///
/// <para>
/// Every module's Types and Enums used to be copied into ONE program-wide table, so a second declaration
/// of a name silently replaced the first and <c>Private</c> was never read. The rule VB6 actually applies
/// is the one <c>TryResolveProcedure</c> had already implemented for procedures: own module first at any
/// visibility, then other modules' <c>Public</c> only, and two foreign Publics is "Ambiguous name
/// detected".
/// </para>
///
/// <para>
/// Types and Enums then DIVERGE, and the difference is measured rather than reasoned: <b>a UDT's type
/// identity is module-scoped and an Enum's is project-scoped.</b> So two modules may each own a
/// <c>Public Type Point</c>, <c>Module2.Point</c> names one of them, and neither is a clash — while two
/// modules may NOT both export a <c>Public Enum</c> of one name, and <c>Module2.MyEnum</c> is not a type
/// name at all. An enum's MEMBERS are still hoisted per-module, which is why the module qualifies them as
/// values though not as a type.
/// </para>
///
/// <para>
/// None of this is visible to the conformance corpus, which is a parse check — every case here parses
/// either way. That is why they are runtime tests.
/// </para>
/// </summary>
public class ModuleScopeTests : BaseVBTestFixture
{
    private const string PointX = "Public Type Point\n    X As Long\nEnd Type\n";
    private const string PointLat = "Public Type Point\n    Latitude As Double\nEnd Type\n";

    [Fact]
    public async Task AUdtIsReachableThroughItsDeclaringModule()
    {
        // `Dim p As Module2.Point` — measured legal, and it used to fail with "User-defined type not
        // defined: Module2.Point". It PARSED, so the corpus gate reported it clean.
        await RunModules("Dim p As Module2.Point\np.X = 5\nDebug.Print p.X\n", ("Module2", PointX));
        // NB Integer, not Long. A UDT field declared `As Long` does not coerce the stored value to its
        // declared width here — a separate gap, unrelated to module scope, asserted as it behaves so this
        // test measures the one thing it is for.
        AssertDebugLog([new Vb6Value(5)]);
    }

    [Fact]
    public async Task TwoModulesMayEachOwnAPrivateTypeOfTheSameName()
    {
        // THE #180 case, in its measured shape: BOTH modules declare their own Private Point, and each
        // uses its own. Two unrelated types — so a collision check that ignored visibility would have
        // refused this, which is the damaging direction.
        //
        // The using module has to declare one of them. A first version left Point private in two OTHER
        // modules and expected it to resolve, which is the opposite of what Private means — the code was
        // right and the test was wrong.
        await RunModules("Private Type Point\n    X As Long\nEnd Type\nDim p As Point\np.X = 1\nDebug.Print p.X\n",
            ("Module2", "Private Type Point\n    Latitude As Double\nEnd Type\n"));
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task AForeignPrivateTypeIsNotVisible()
    {
        var act = async () => await RunModules("Dim p As Point\n",
            ("Module2", "Private Type Point\n    X As Long\nEnd Type\n"));
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("User-defined type not defined");
    }

    [Fact]
    public async Task AForeignPrivateTypeIsNotVisibleEvenWhenQualified()
    {
        // Naming the declaring module is a disambiguator, not a back door — measured.
        var act = async () => await RunModules("Dim p As Module2.Point\n",
            ("Module2", "Private Type Point\n    X As Long\nEnd Type\n"));
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task TwoForeignPublicTypesOfOneNameAreAmbiguous()
    {
        // Nothing local to disambiguate, so the bare reference is refused — where it used to silently take
        // whichever module loaded last.
        var act = async () => await RunModules("Dim p As Point\n",
            ("Module2", PointX), ("Module3", PointLat));
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("Ambiguous name detected");
    }

    [Fact]
    public async Task AQualifiedTypeNameOverridesALocalDeclaration()
    {
        // Measured by construction: the local Point has X, the foreign one has Latitude, so reading
        // `.Latitude` says which one won.
        await RunModules("Private Type Point\n    X As Long\nEnd Type\n" +
                         "Dim p As Module2.Point\np.Latitude = 2.5\nDebug.Print p.Latitude\n",
            ("Module2", PointLat));
        AssertDebugLog([new Vb6Value(2.5)]);
    }

    [Fact]
    public async Task ALocalTypeWinsWhenTheReferenceIsUnqualified()
    {
        await RunModules("Private Type Point\n    X As Long\nEnd Type\nDim p As Point\np.X = 7\nDebug.Print p.X\n",
            ("Module2", PointLat));
        AssertDebugLog([new Vb6Value(7)]);
    }

    [Fact]
    public async Task TwoModulesMayNotBothExportAnEnumOfOneName()
    {
        // An Enum's identity is PROJECT-scoped, so this is refused at the declaration — VB6 reports it with
        // no use involved, and so does this, at module load.
        var act = async () => await RunModules("Debug.Print 1\n",
            ("Module2", "Public Enum EKind\n    kA = 1\nEnd Enum\n"),
            ("Module3", "Public Enum EKind\n    kB = 2\nEnd Enum\n"));
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("Ambiguous name detected");
    }

    [Fact]
    public async Task TwoModulesMayEachOwnAPrivateEnumOfTheSameName()
    {
        // The Private twin of the previous test — legal, because neither reaches the project namespace.
        await RunModules("Debug.Print 1\n",
            ("Module2", "Private Enum EKind\n    kA = 1\nEnd Enum\n"),
            ("Module3", "Private Enum EKind\n    kB = 2\nEnd Enum\n"));
        AssertDebugLog([new Vb6Value(1)]);
    }

    [Fact]
    public async Task AMemberNameDeclaredByTwoEnumsIsAmbiguousWhenBare()
    {
        // Both declarations are legal and the USE is the error — measured. The second member used to
        // silently overwrite the first, handing back the wrong enum's value.
        var act = async () => await Run(
            "Public Enum EOne\n    shared_ = 11\nEnd Enum\nPublic Enum ETwo\n    shared_ = 22\nEnd Enum\n" +
            "Debug.Print shared_\n");
        (await act.Should().ThrowAsync<VBCompileErrorException>())
            .Which.Message.Should().Contain("Ambiguous name detected");
    }

    [Fact]
    public async Task TheQualifiedFormsStayUsableWhenAMemberNameIsAmbiguous()
    {
        // The ambiguity is a property of the bare reference only.
        await Run("Public Enum EOne\n    shared_ = 11\nEnd Enum\nPublic Enum ETwo\n    shared_ = 22\nEnd Enum\n" +
                  "Debug.Print EOne.shared_\nDebug.Print ETwo.shared_\n");
        AssertDebugLog([new Vb6Value(11L), new Vb6Value(22L)]);
    }

    [Fact]
    public async Task TheProjectNameQualifiesATypeAndAnEnumMember()
    {
        // `Project1.Point` in type position and `Project1.Module2.MyEnum.Foo` in value position are both
        // legal VB6 — a whole qualifier level HexIDE had no concept of. The project level is accepted and
        // then ignored: there is one project, so naming it cannot change which declaration is found.
        await RunModules("Dim p As Project1.Point\np.X = 3\nDebug.Print p.X\n" +
                         "Debug.Print Project1.Module2.MyEnum.Foo\nDebug.Print Project1.Foo\n",
            ("Module2", PointX + "\nPublic Enum MyEnum\n    Foo = 9\nEnd Enum\n"));
        AssertDebugLog([new Vb6Value(3), new Vb6Value(9L), new Vb6Value(9L)]);
    }
}
