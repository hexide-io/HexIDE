using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// The qualifiers a save writes into members' attribute runs (hexide-io/HexIDE#273 task 4.4; design.md, "Renames
/// (4.4)"): a procedure's run and the run a rename of a one-name variable line left name their member as
/// declared, and nothing else changes.
/// </summary>
/// <remarks>
/// <para>
/// Where a shape is one VB6 wrote or saved on the oracle guest, the test says which probe, and the expected text
/// is VB6's own saved text for it (oracle, "Member attributes: where VB6 attaches them, and what its IDE
/// writes"). Where HexIDE knowingly writes something else, the row says so: those are the comma-list rules and
/// the renamed kind's weaknesses the design records, pinned here rather than left implicit.
/// </para>
/// <para>
/// The save path that calls this, its gate on unsaved changes and the open window following it, are
/// <c>MemberAttributesFollowARenameTests</c>'.
/// </para>
/// </remarks>
public class MemberAttributeQualifiersTests
{
    /// <summary>The header VB6 writes for a class: every row below is the code that follows it.</summary>
    private const string ClassHeader =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "  Persistable = 0  'NotPersistable\r\n" +
        "  DataBindingBehavior = 0  'vbNone\r\n" +
        "  DataSourceBehavior  = 0  'vbNone\r\n" +
        "  MTSTransactionMode  = 0  'NotAnMTSObject\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Shapes\"\r\n" +
        "Attribute VB_GlobalNameSpace = False\r\n" +
        "Attribute VB_Creatable = True\r\n" +
        "Attribute VB_PredeclaredId = False\r\n" +
        "Attribute VB_Exposed = False\r\n";

    private static string Rewritten(string code) => MemberAttributeQualifiers.Rewrite(ClassHeader, code);

    // -- A procedure's run: rewritten to the procedure's name, whatever it said --------------------------

    public static TheoryData<string, string, string> ProcedureShapes => new()
    {
        {
            "C2: the name retyped on the declaration",
            "Option Explicit\r\n\r\nPrivate mSum As Long\r\n\r\nPublic Function GrandTotal() As Long\r\n" +
            "Attribute Total.VB_Description = \"Adds things up.\"\r\n    Total = mSum\r\nEnd Function\r\n",
            "Option Explicit\r\n\r\nPrivate mSum As Long\r\n\r\nPublic Function GrandTotal() As Long\r\n" +
            "Attribute GrandTotal.VB_Description = \"Adds things up.\"\r\n    Total = mSum\r\nEnd Function\r\n"
        },
        {
            "NamesNothing: a loaded qualifier naming nothing",
            "Option Explicit\r\n' p4 marker2\r\n\r\nPublic Function Total() As Long\r\n" +
            "Attribute Gone.VB_Description = \"Names nothing.\"\r\n    Total = 1\r\nEnd Function\r\n",
            "Option Explicit\r\n' p4 marker2\r\n\r\nPublic Function Total() As Long\r\n" +
            "Attribute Total.VB_Description = \"Names nothing.\"\r\n    Total = 1\r\nEnd Function\r\n"
        },
        {
            "NamesOther: a loaded qualifier naming another member, which gets nothing",
            "Option Explicit\r\n' p4 marker2\r\n\r\nPublic Function Total() As Long\r\n" +
            "Attribute Other.VB_Description = \"Names Other.\"\r\n    Total = 1\r\nEnd Function\r\n\r\n" +
            "Public Function Other() As Long\r\n    Other = 2\r\nEnd Function\r\n",
            "Option Explicit\r\n' p4 marker2\r\n\r\nPublic Function Total() As Long\r\n" +
            "Attribute Total.VB_Description = \"Names Other.\"\r\n    Total = 1\r\nEnd Function\r\n\r\n" +
            "Public Function Other() As Long\r\n    Other = 2\r\nEnd Function\r\n"
        },
        {
            "LowerCase: a loaded qualifier in another case, re-cased",
            "Option Explicit\r\n' p4 marker2\r\n\r\nPublic Function Total() As Long\r\n" +
            "Attribute total.VB_Description = \"Lower-case qualifier.\"\r\n    Total = 1\r\nEnd Function\r\n",
            "Option Explicit\r\n' p4 marker2\r\n\r\nPublic Function Total() As Long\r\n" +
            "Attribute Total.VB_Description = \"Lower-case qualifier.\"\r\n    Total = 1\r\nEnd Function\r\n"
        },
    };

    [Theory]
    [MemberData(nameof(ProcedureShapes))]
    public void AProceduresRunIsRewrittenToTheNameItsDeclarationSpells(string shape, string code, string vb6Saved)
    {
        // Each expected text is VB6's own: the body of the file it saved (IDE probes C2 and B).
        Rewritten(code).Should().Be(vb6Saved, shape);
    }

    [Fact]
    public void EveryLineOfAProceduresRunIsRewrittenAndOnlyItsQualifier()
    {
        // E's Full, in the order VB6 writes a procedure's run, with the qualifier before the first dot of
        // VB_ProcData.VB_Invoke_Property the only part that is the member's name.
        const string code =
            "Public Property Get Level() As Long\r\n" +
            "Attribute Full.VB_Description = \"Every option.\"\r\n" +
            "Attribute full.VB_HelpID = 7\r\n" +
            "Attribute Full.VB_ProcData.VB_Invoke_Property = \";Behavior\"\r\n" +
            "Attribute FULL.VB_UserMemId = 0\r\n" +
            "Attribute Full.VB_MemberFlags = \"127c\"\r\n" +
            "    Level = 1\r\n" +
            "End Property\r\n";

        Rewritten(code).Should().Be(code
            .Replace("Attribute Full.", "Attribute Level.", StringComparison.Ordinal)
            .Replace("Attribute full.", "Attribute Level.", StringComparison.Ordinal)
            .Replace("Attribute FULL.", "Attribute Level.", StringComparison.Ordinal));
    }

    [Fact]
    public void APropertysGetAndLetRunsAreEachRewritten()
    {
        // B2's BothRuns: each accessor with its own run, both kept by VB6, each under its own accessor.
        const string code =
            "Public Property Get Rate() As Double\r\n" +
            "Attribute Tariff.VB_Description = \"Read it.\"\r\n" +
            "    Rate = mRate\r\n" +
            "End Property\r\n" +
            "Public Property Let Rate(ByVal Value As Double)\r\n" +
            "Attribute Tariff.VB_Description = \"Write it.\"\r\n" +
            "    mRate = Value\r\n" +
            "End Property\r\n";

        Rewritten(code).Should().Be(code.Replace("Attribute Tariff.", "Attribute Rate.", StringComparison.Ordinal));
    }

    [Fact]
    public void AQualifierOnTheSecondPhysicalLineOfAnAttributeLineIsRewrittenWhereItIs()
    {
        // p11b: `Attribute _` above the name, which VB6 reads as one attribute line. Whether its IDE rejoins the
        // line at a save was not measured, so it is left continued and only the name changes.
        const string code =
            "Public Sub Wrapped()\r\n" +
            "Attribute _\r\n" +
            "Rapped.VB_HelpID = 3\r\n" +
            "End Sub\r\n";

        Rewritten(code).Should().Be(code.Replace("Rapped.VB_HelpID", "Wrapped.VB_HelpID", StringComparison.Ordinal));
    }

    [Fact]
    public void ARunUnderAContinuedDeclarationTakesTheNameOnItsFirstLine()
    {
        // A's Average: VB6 writes the run under the declaration's last physical line, qualified with the name on
        // its first.
        const string code =
            "Public Function Mean(ByVal First As Long, _\r\n" +
            "                        ByVal Second As Long) As Double\r\n" +
            "Attribute Average.VB_Description = \"Mean of two values.\"\r\n" +
            "    Mean = (First + Second) / 2\r\n" +
            "End Function\r\n";

        Rewritten(code).Should().Be(code.Replace("Attribute Average.", "Attribute Mean.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheRewritesAreExactlyEachQualifierInOrderOfOffset()
    {
        const string code =
            "Public Tally As Long\r\n" +
            "Attribute Count.VB_VarDescription = \"Retyped.\"\r\n" +
            "Public Sub Run()\r\n" +
            "Attribute Go.VB_Description = \"Starts it.\"\r\n" +
            "Attribute Go.VB_HelpID = 2\r\n" +
            "End Sub\r\n";
        var text = ClassHeader + code;
        int At(string what) => text.IndexOf(what, StringComparison.Ordinal);

        MemberAttributeQualifiers.Rewrites(text, ClassHeader.Length).Should().Equal(
            new TextChange(At("Count."), "Count".Length, "Tally"),
            new TextChange(At("Go.VB_Description"), "Go".Length, "Run"),
            new TextChange(At("Go.VB_HelpID"), "Go".Length, "Run"));
    }

    [Fact]
    public void NothingToRewriteGivesBackTheSameText()
    {
        const string code =
            "Public Function Total() As Long\r\n" +
            "Attribute Total.VB_Description = \"Adds things up.\"\r\n" +
            "End Function\r\n";

        MemberAttributeQualifiers.Rewrite(ClassHeader, code).Should().BeSameAs(code,
            "the save asks whether anything changed by reference, before it renders the document to ask whether it is dirty");
    }

    // -- A variable's run --------------------------------------------------------------------------------

    [Fact]
    public void TheRunANameRetypedOnAOneNameLineLeavesIsRewrittenToTheNewName()
    {
        // V1, VB6's input and its save: Count retyped Tally keeps the description, re-qualified.
        const string code =
            "Option Explicit\r\n\r\n" +
            "Public Tally As Long\r\n" +
            "Attribute Count.VB_VarDescription = \"Var1 Count, described.\"\r\n\r\n" +
            "Public Sub Other1()\r\n    Debug.Print 1\r\nEnd Sub\r\n";

        Rewritten(code).Should().Be(code.Replace("Attribute Count.", "Attribute Tally.", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("A's two declarators, the first's run and the second's (VB6-written)",
        "Public A As Long, B As Long\r\nAttribute A.VB_VarMemberFlags = \"40\"\r\nAttribute A.VB_VarHelpID = 5\r\n" +
        "Attribute B.VB_VarDescription = \"Second counter.\"\r\n")]
    [InlineData("E's colon line, for the declaration after it (VB6-written)",
        "Public A As Long: Public B As Long\r\nAttribute B.VB_VarDescription = \"After a colon.\"\r\n")]
    [InlineData("WithEvents, with the line VB6 adds by itself",
        "Public WithEvents Clock As Timer\r\nAttribute Clock.VB_VarHelpID = -1\r\n")]
    [InlineData("a constant (V5's ConstMulti)",
        "Public Const C1 = 1, C2 = 2\r\nAttribute C2.VB_VarDescription = \"Second.\"\r\n")]
    [InlineData("a qualifier in another case, whose re-casing VB6 was not measured doing",
        "Public Count As Long\r\nAttribute count.VB_VarDescription = \"How many.\"\r\n")]
    public void AVariableRunThatNamesItsVariablesIsLeftAsWritten(string shape, string code)
    {
        Rewritten(code).Should().BeSameAs(code, shape);
    }

    [Theory]
    [InlineData("BlankGap: under a blank line",
        "Public Function Total() As Long\r\n\r\nAttribute Gone.VB_Description = \"x\"\r\n    Total = 1\r\nEnd Function\r\n")]
    [InlineData("CommentGap: under a comment",
        "Public Function Total() As Long\r\n' note\r\nAttribute Gone.VB_Description = \"x\"\r\nEnd Function\r\n")]
    [InlineData("InBody: under a statement",
        "Public Function Total() As Long\r\n    Total = 1\r\nAttribute Gone.VB_Description = \"x\"\r\nEnd Function\r\n")]
    [InlineData("SplitRun: the half after a blank line",
        "Public Function Total() As Long\r\nAttribute Total.VB_Description = \"x\"\r\n\r\nAttribute Gone.VB_UserMemId = 0\r\nEnd Function\r\n")]
    [InlineData("a procedure attribute under a variable",
        "Public Tally As Long\r\nAttribute Gone.VB_Description = \"x\"\r\n")]
    [InlineData("a mixed run (p09n)",
        "Public Sub Ring()\r\nAttribute Gone.VB_Description = \"x\"\r\nAttribute Gone.VB_VarHelpID = -1\r\nEnd Sub\r\n")]
    [InlineData("VarAway: a variable's line under a procedure",
        "Public B As Long\r\n\r\nPublic Sub Foo()\r\nAttribute B.VB_VarDescription = \"x\"\r\nEnd Sub\r\n")]
    [InlineData("a variable's line naming one declared elsewhere",
        "Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"x\"\r\nPublic Count As Long\r\n")]
    [InlineData("V2's Multi2: the second declarator retyped, B to C, on a line declaring two",
        "Public A As Long, C As Long\r\nAttribute B.VB_VarDescription = \"Multi2 B, described.\"\r\n")]
    [InlineData("a run under an Enum line",
        "Public Enum Sizes\r\nAttribute Gone.VB_VarDescription = \"x\"\r\n    Small = 1\r\nEnd Enum\r\n")]
    public void AnOrphanIsNeverTouched(string shape, string code)
    {
        // Inert text, kept byte for byte, where VB6 would drop or move it at its next save (IDE probe B).
        Rewritten(code).Should().BeSameAs(code, shape);
    }

    [Fact]
    public void ACommaListsRenamedFirstDeclaratorLeavesItsRunNamingTheOldName()
    {
        // The one loss the design records. V2's MultiP: P retyped r, and VB6 saved `Attribute r.VB_VarDescription`.
        // HexIDE cannot tell this run from one naming a renamed later declarator, which VB6 drops (Multi2, above),
        // and carrying that one to the first declarator would give it a description that was never its own. So
        // it is an orphan, visible and editable, and the save leaves it naming P.
        const string code =
            "Option Explicit\r\n\r\n" +
            "Public r As Long, Q As Long\r\n" +
            "Attribute P.VB_VarDescription = \"MultiP P, described.\"\r\n\r\n" +
            "Public Sub OtherP()\r\n    Debug.Print 0\r\nEnd Sub\r\n";

        Rewritten(code).Should().BeSameAs(code, "VB6 writes `Attribute r.`; HexIDE keeps the text");
    }

    // -- The renamed kind's weaknesses (design.md, "What the module declares"), pinned ---------------------

    [Fact]
    public void AVariableLineNamingNothingIsTakenForARenamesLeftover()
    {
        // p09f's shape: Gone.VB_VarDescription under Public A, which VB6's compiler attaches to nothing. Text alone
        // cannot tell it from the run a rename by retyping leaves, so the save gives A a description it never had.
        // What VB6's IDE does with this shape at a save was not measured.
        const string code = "Public A As Long\r\nAttribute Gone.VB_VarDescription = \"Gone's.\"\r\n";

        Rewritten(code).Should().Be(code.Replace("Gone.", "A.", StringComparison.Ordinal));
    }

    [Fact]
    public void TheGuardIsANameDeclaredNowSoARetypeElsewhereHandsADescriptionOn()
    {
        // While Count stands below, the run under Tally is an orphan: VB6 would move it under Count. Retype Count
        // to Zed and the same run is Tally's leftover, which the save hands to Tally, where VB6, by V1's rule,
        // would have carried Count's description to Zed.
        const string standing = "Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"Count's.\"\r\nPublic Count As Long\r\n";
        const string retyped = "Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"Count's.\"\r\nPublic Zed As Long\r\n";

        Rewritten(standing).Should().BeSameAs(standing);
        Rewritten(retyped).Should().Be(retyped.Replace("Count.", "Tally.", StringComparison.Ordinal));
    }

    [Fact]
    public void TwoOneNameLinesCarryingOneStaleNameAreEachRewrittenToTheirOwn()
    {
        const string code =
            "Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"First.\"\r\n" +
            "Public Score As Long\r\nAttribute Count.VB_VarDescription = \"Second.\"\r\n";

        Rewritten(code).Should().Be(
            "Public Tally As Long\r\nAttribute Tally.VB_VarDescription = \"First.\"\r\n" +
            "Public Score As Long\r\nAttribute Score.VB_VarDescription = \"Second.\"\r\n");
    }

    [Theory]
    [InlineData("a parameter's", "Public Sub Go(ByVal Gone As Long)\r\nEnd Sub\r\n")]
    [InlineData("a label's", "Public Sub Go()\r\nGone:\r\nEnd Sub\r\n")]
    [InlineData("an Enum's", "Private Enum Gone\r\n    Small = 1\r\nEnd Enum\r\n")]
    [InlineData("a Type's", "Private Type Gone\r\n    X As Long\r\nEnd Type\r\n")]
    public void AStaleNameTheReaderDoesNotSeeDeclaredIsTakenForARenamesLeftover(string whose, string elsewhere)
    {
        // The reader never sees a parameter, a label, or an Enum's or a Type's name, so a run naming one reads as a
        // rename's leftover and is rewritten. VB6 binds a VB_Var line by name, to nothing for any of these.
        var code = "Public Spare As Long\r\nAttribute Gone.VB_VarDescription = \"x\"\r\n" + elsewhere;

        Rewritten(code).Should().Be(code.Replace("Attribute Gone.", "Attribute Spare.", StringComparison.Ordinal), whose);
    }

    [Fact]
    public void AStaleNameThatIsAControlsIsLeftAloneBecauseTheWholeFileIsRead()
    {
        // The designer block's names are the module's too, so a run naming a control is an orphan, which VB6 would
        // move to nothing (a control takes no VB_Var line). Read without the designer block, the same body would
        // take the name for a rename's leftover.
        const string designer =
            "VERSION 5.00\r\n" +
            "Begin VB.Form frmShapes \r\n" +
            "   Caption         =   \"Shapes\"\r\n" +
            "   Begin VB.CommandButton cmdGo \r\n" +
            "      Caption         =   \"Go\"\r\n" +
            "   End\r\n" +
            "End\r\n";
        const string code =
            "Attribute VB_Name = \"frmShapes\"\r\n" +
            "Attribute VB_Exposed = False\r\n" +
            "Option Explicit\r\n" +
            "Public Spare1 As Long\r\n" +
            "Attribute cmdGo.VB_VarDescription = \"names a control\"\r\n";

        MemberAttributeQualifiers.Rewrite(designer, code).Should().BeSameAs(code);
        MemberAttributeQualifiers.Rewrite("", code).Should().NotBe(code, "the premise: the body alone reads the run differently");
    }
}
