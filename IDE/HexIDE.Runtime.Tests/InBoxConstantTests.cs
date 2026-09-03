using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

/// <summary>
/// VB6's in-box constants, addressed the way VB6 addresses them. Every expectation measured against real
/// vb6.exe — see <c>docs/vb6-inbox-constants.md</c> for the inventory and the oracle's <i>In-box
/// constants</i> section for the semantics.
///
/// <para>
/// <b>What this replaced.</b> A flat 713-entry name→value dictionary that was library-blind. It could not
/// be right, because the qualifier <i>selects</i>: <c>vbCancel</c> is 2 in <c>VBA.VbMsgBoxResult</c> and 0
/// in <c>VBRUN.DragConstants</c>, so answering the same value for both is a wrong value however the table
/// is filled in. It also typed every numeric constant through <c>Vb6Value(int)</c>'s magnitude rule, so
/// small ones came back Integer where VB6 reports Long.
/// </para>
///
/// <para>
/// <b>Why none of this was caught before.</b> The corpus cases covering library-qualified addressing were
/// all module-scope, so they wrap in <c>Sub Main</c> — which the interpreter cannot run (#210) — and were
/// therefore never behaviourally gated. The replacements here are statement-scope and are.
/// </para>
/// </summary>
public class InBoxConstantTests : BaseVBTestFixture
{
    // ---- the qualifier selects ------------------------------------------------------------------

    [Fact]
    public async Task ABareNameResolvesThroughVbaBeforeVbrun()
    {
        // The only genuinely ambiguous name of the 728. Default reference order gives VBA precedence.
        await Run("Debug.Print vbCancel");
        AssertDebugLog([new Vb6Value(2L)]);
    }

    [Fact]
    public async Task TheLibraryQualifierPicksTheLibrarysOwnValue()
    {
        // The case the whole change exists for: the same name, two libraries, two values.
        await Run("Debug.Print VBA.vbCancel\nDebug.Print VBRUN.vbCancel");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value(0L)]);
    }

    [Fact]
    public async Task TheContainerQualifierSelectsOnItsOwn()
    {
        // No library needed — all 79 container names are unique across the four libraries.
        await Run("Debug.Print VbMsgBoxResult.vbCancel\nDebug.Print DragConstants.vbCancel");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value(0L)]);
    }

    [Fact]
    public async Task TheFullLibraryEnumMemberFormSelectsToo()
    {
        await Run("Debug.Print VBA.VbMsgBoxResult.vbCancel\nDebug.Print VBRUN.DragConstants.vbCancel");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value(0L)]);
    }

    [Fact]
    public async Task AConsistentDuplicateIsUnaffectedByTheQualifier()
    {
        // vbNormal is the other name declared twice, and both say 0. The control: it distinguishes "the
        // qualifier selects a value" from "the qualifier is merely checked for membership".
        await Run("Debug.Print vbNormal\nDebug.Print VBA.vbNormal\nDebug.Print VBRUN.vbNormal");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(0L), new Vb6Value(0L)]);
    }

    // ---- both levels are real scopes, and refuse a mismatch -------------------------------------

    [Fact]
    public async Task AContainerUnderTheWrongLibraryIsRefused()
    {
        // VbMsgBoxResult is VBA's. The old code stepped over an unrecognised middle segment, so this
        // quietly answered 2.
        var act = async () => await Run("Debug.Print VBRUN.VbMsgBoxResult.vbCancel");
        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [Fact]
    public async Task AMemberUnderALibraryThatDoesNotDeclareItIsRefused()
    {
        // `VBA.vbKeyA` is the real-world shape of this: vbKeyA is VBRUN's, and an interpreter test used
        // to assert `VBA.vbKeyA` = 65 — pinning the transparent lookup rather than VB6.
        var act = async () => await Run("Debug.Print VBA.vbKeyA");
        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [Fact]
    public async Task AMemberUnderTheWrongContainerIsRefused()
    {
        // vbYes belongs to VbMsgBoxResult, not DragConstants.
        var act = async () => await Run("Debug.Print DragConstants.vbYes");
        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    [Fact]
    public async Task TheVbLibraryDeclaresNothingSoEveryMemberUnderItIsRefused()
    {
        // VB - "Visual Basic objects and procedures" - really does declare zero constants. It was the one
        // library the old hand-written pair recognised, while VBRUN, which declares 590, was not.
        var act = async () => await Run("Debug.Print VB.vbKeyA");
        await act.Should().ThrowAsync<VBMethodOrDataMemberNotFoundException>();
    }

    // ---- constant modules --------------------------------------------------------------------------

    [Fact]
    public async Task AConstantModuleQualifiesItsMembersJustAsAnEnumDoes()
    {
        await Run("Debug.Print Len(Constants.vbCrLf)\nDebug.Print Len(VBA.Constants.vbCrLf)");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value(2L)]);
    }

    [Fact]
    public async Task TheStringConstantsThatUsedToBeMissingEntirely()
    {
        // vbTab, vbNullString and five others were absent from the old table, so under Option Explicit
        // they were undeclared-variable errors — recorded as Dies in MISSING_LANGUAGE.md. They are
        // members of the VBA.Constants MODULE, which is why collecting only TKIND_ENUM would have
        // missed them.
        await Run("Debug.Print Asc(vbTab)\nDebug.Print Asc(vbBack)\nDebug.Print Asc(vbFormFeed)\n"
                + "Debug.Print Asc(vbVerticalTab)\nDebug.Print Len(vbNullString)\nDebug.Print Len(vbNewLine)");
        AssertDebugLog([new Vb6Value(9), new Vb6Value(8), new Vb6Value(12),
                        new Vb6Value(11), new Vb6Value(0L), new Vb6Value(2L)]);
    }

    // ---- type position -----------------------------------------------------------------------------

    [Fact]
    public async Task AnInBoxEnumIsUsableAsAType()
    {
        await Run("Dim x As VbMsgBoxResult\nx = vbCancel\nDebug.Print x\nDebug.Print TypeName(x)");
        AssertDebugLog([new Vb6Value(2L), new Vb6Value("Long")]);
    }

    [Fact]
    public async Task AnInBoxEnumTypeMayBeLibraryQualified()
    {
        // Measured legal — and note the ASYMMETRY with user enums, where a module-qualified type name
        // (`Dim p As Module2.MyEnum`) is illegal because an Enum's identity is project-scoped. A library
        // is not a module.
        await Run("Dim x As VBA.VbMsgBoxResult\nx = vbCancel\nDebug.Print x");
        AssertDebugLog([new Vb6Value(2L)]);
    }

    [Fact]
    public async Task AConstantModuleIsNotAType()
    {
        // `Dim x As Constants` is illegal in VB6 - "Automation type not supported in Visual Basic". An
        // enum name is a type AND a qualifier; a module name is only a qualifier.
        var act = async () => await Run("Dim x As Constants\nDebug.Print 1");
        await act.Should().ThrowAsync<VBCompileErrorException>();
    }

    [Fact]
    public async Task AnInBoxEnumTypedVariableAcceptsAValueOutsideTheEnum()
    {
        // Same as a user enum (#207): the declared type is a Long, not a constrained set.
        await Run("Dim x As VbMsgBoxResult\nx = 999\nDebug.Print x");
        AssertDebugLog([new Vb6Value(999L)]);
    }

    // ---- the project wins over the libraries -------------------------------------------------------

    [Fact]
    public async Task AUserConstShadowsAnInBoxConstant()
    {
        // Measured: 7, and as an INTEGER — the user's Const is typed by its own literal, not by the
        // library member it shadows.
        await Run("Const vbCancel = 7\nDebug.Print vbCancel\nDebug.Print TypeName(vbCancel)");
        AssertDebugLog([new Vb6Value(7), new Vb6Value("Integer")]);
    }

    [Fact]
    public async Task AUserEnumShadowsAnInBoxEnumOfTheSameName()
    {
        // Measured: 42. A project Enum named after an in-box one wins, and so does its member — which is
        // why TryResolveQualifier checks the user's Enums table BEFORE the in-box libraries. Getting that
        // order wrong would make every project that declares a VbMsgBoxResult read the library's values.
        await Run("Private Enum VbMsgBoxResult\n    vbCancel = 42\nEnd Enum\nDebug.Print vbCancel");
        AssertDebugLog([new Vb6Value(42L)]);
    }

    // ---- every numeric constant is a Long ----------------------------------------------------------

    [Fact]
    public async Task EveryNumericInBoxConstantIsALongNotAnInteger()
    {
        // The old table produced Integer for anything that fitted Int16 — a wrong TYPE on roughly 700
        // values. Sampled across all four libraries and both container kinds rather than spot-checked
        // on one, because the defect was uniform and a single probe would have looked like a one-off.
        await Run("Debug.Print TypeName(vbCancel)\nDebug.Print TypeName(vbAlignBottom)\n"
                + "Debug.Print TypeName(vbKeyA)\nDebug.Print TypeName(vbObjectError)\n"
                + "Debug.Print TypeName(Default)");
        AssertDebugLog([new Vb6Value("Long"), new Vb6Value("Long"), new Vb6Value("Long"),
                        new Vb6Value("Long"), new Vb6Value("Long")]);
    }

    [Fact]
    public async Task TheStdoleConstantsAreReachableBareDespiteTheirGenericNames()
    {
        // stdole contributes seven members whose names are not vb-prefixed — Default, Color, Gray,
        // Checked, Unchecked, Monochrome, VgaColor. Measured: they ARE bare-resolvable in VB6, even
        // under Option Explicit, so including them is faithful rather than reckless. A user declaration
        // of the same name still wins.
        await Run("Debug.Print Default\nDebug.Print Color\nDebug.Print Checked");
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(4L), new Vb6Value(1L)]);
    }

    // ---- the data itself ---------------------------------------------------------------------------

    [Fact]
    public void TheEmbeddedInventoryMatchesTheCatalogue()
    {
        // Guards the embedded resource against silently failing to load or being regenerated wrongly:
        // without this, every lookup above would still pass on a partially-loaded table as long as the
        // handful of names they use survived.
        var containers = VB6InBoxLibraries.AllContainers.ToList();
        containers.Count(c => c.IsEnum).Should().Be(77, "the inventory holds 77 enums");
        containers.Count(c => !c.IsEnum).Should().Be(2, "and 2 constant modules");
        containers.Sum(c => c.Members.Count).Should().Be(728, "for 728 constants in total");

        // VB6's own reference order. VBA, VBRUN and VB are implicit, irremovable and fixed in that
        // sequence — they never appear as Reference= lines in a .vbp — while stdole is an ordinary listed
        // reference that may be removed or reordered but never placed ahead of the fixed three, so it is
        // always last of the four.
        //
        // This was WRONG on the first pass: the generator hand-wrote VBA, VBRUN, stdole, VB and this test
        // then asserted it as though it had been measured. Nothing observable changed — VB declares no
        // constants and stdole shares no name with the others, so only the VBA-before-VBRUN step has a
        // consequence today. Asserted correctly anyway: the day a fifth library or a project reference
        // joins the table, an order that was merely plausible would start deciding values.
        VB6InBoxLibraries.LibraryOrder.Should().Equal(["VBA", "VBRUN", "VB", "stdole"],
            "bare resolution is first-wins in VB6's reference order, which is what makes vbCancel answer "
          + "VBA's 2 rather than VBRUN's 0");
    }

    [Fact]
    public void EveryContainerNameIsUniqueAcrossTheFourLibraries()
    {
        // The assumption the unqualified `Enum.Member` form rests on. If two libraries ever declared an
        // enum of one name, a single flat container index would silently pick one — so this is asserted
        // rather than assumed, and it is the test that would fail if the data changed underneath.
        var names = VB6InBoxLibraries.AllContainers.Select(c => c.Name).ToList();
        names.Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public void TheOnlyNameDeclaredTwiceWithDifferentValuesIsVbCancel()
    {
        // The premise of the whole change, asserted against the data so it cannot quietly stop being
        // true. If a regenerated inventory introduced a second ambiguous name, bare precedence would
        // start deciding a value nobody had measured.
        var byName = VB6InBoxLibraries.AllContainers
            .SelectMany(c => c.Members.Select(m => (m.Key, m.Value)))
            .GroupBy(t => t.Key, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(t => t.Value.Value?.ToString()).Distinct().Count() > 1)
            .Select(g => g.Key)
            .ToList();

        byName.Should().BeEquivalentTo(["vbCancel"],
            "vbCancel is 2 in VBA.VbMsgBoxResult and 0 in VBRUN.DragConstants; any other ambiguous name "
          + "would need its own measurement of which library wins bare");
    }
}
