using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// A whole replacement and a member's attribute run (hexide-io/HexIDE#273 task 4.5b; design.md, "Whole
/// replacements"): a run may move with its member, be renamed with it, and go with its declaration, and any other
/// change, addition or loss is refused with a reason naming each member. Orphans are free.
/// </summary>
/// <remarks>
/// Every replacement here is the code alone, as a caller composing code sends it, so the class header is kept in
/// front and only the runs decide. The shapes are hand-written unless a comment cites the probe that measured them.
/// </remarks>
public class GuardedContentMemberRunTests
{
    private const string ClassHeader =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Order\"\r\n";

    private const string Code =
        "Option Explicit\r\n" +
        "Public Total As Long\r\n" +
        "Attribute Total.VB_VarDescription = \"The running total\"\r\n" +
        "Private m As Long\r\n" +
        "\r\n" +
        "Public Sub Halt()\r\n" +
        "Attribute Halt.VB_Description = \"Stops the order\"\r\n" +
        "    Total = 0\r\n" +
        "End Sub\r\n" +
        "\r\n" +
        "Public Sub Pause()\r\n" +
        "End Sub\r\n";

    /// <summary>A property whose Get and Let each carry a run, the shape a save keeps (IDE probe B2, BothRunsGetFirst).</summary>
    private const string Property =
        "Option Explicit\r\n" +
        "Private m As Long\r\n" +
        "\r\n" +
        "Public Property Get Value() As Long\r\n" +
        "Attribute Value.VB_Description = \"The value\"\r\n" +
        "    Value = m\r\n" +
        "End Property\r\n" +
        "\r\n" +
        "Public Property Let Value(ByVal v As Long)\r\n" +
        "Attribute Value.VB_HelpID = 7\r\n" +
        "    m = v\r\n" +
        "End Property\r\n";

    private static ContentReplacement Replace(string code, string incoming) =>
        GuardedContent.Replace(ClassHeader + code, ClassHeader.Length, incoming);

    private static void Applied(ContentReplacement result, string incoming)
    {
        result.Refusal.Should().BeNull();
        result.NewText.Should().Be(ClassHeader + incoming);
    }

    private static string Refused(ContentReplacement result)
    {
        result.NewText.Should().BeNull("nothing is written when the replacement is refused");
        result.Refusal.Should().NotBeNull();
        return result.Refusal!;
    }

    // -- What passes -------------------------------------------------------------------------------------

    [Fact]
    public void AnEditToTheCodeThatLeavesEveryRunAsItWasIsApplied()
    {
        var incoming = Code.Replace("    Total = 0", "    Total = -1");

        Applied(Replace(Code, incoming), incoming);
    }

    [Fact]
    public void ARunsLineEndingsAreNotAChange()
    {
        var incoming = Code.Replace("\r\n", "\n");

        Replace(Code, incoming).Refusal.Should().BeNull();
    }

    [Fact]
    public void AContinuedAttributeLinesLineEndingsAreNotAChange()
    {
        // VB6 reads an attribute line continued after its word as one line (oracle probe p11b), so the terminator
        // inside it is part of the line the comparison reads.
        var code = Code.Replace("Attribute Halt.VB_Description", "Attribute _\r\n    Halt.VB_Description");

        Replace(code, code.Replace("\r\n", "\n")).Refusal.Should().BeNull();
    }

    [Fact]
    public void AProcedureMovedWithItsRunIsApplied()
    {
        const string halt =
            "Public Sub Halt()\r\n" +
            "Attribute Halt.VB_Description = \"Stops the order\"\r\n" +
            "    Total = 0\r\n" +
            "End Sub\r\n" +
            "\r\n";
        var incoming = Code.Replace(halt, "") + "\r\n" + halt;

        Applied(Replace(Code, incoming), incoming);
    }

    [Fact]
    public void AProcedureDeletedWithItsRunIsApplied()
    {
        var incoming = Code.Replace(
            "Public Sub Halt()\r\nAttribute Halt.VB_Description = \"Stops the order\"\r\n    Total = 0\r\nEnd Sub\r\n\r\n", "");

        Applied(Replace(Code, incoming), incoming);
    }

    [Fact]
    public void ADeclarationCommentedOutLeavesItsRunAnOrphanAndIsApplied()
    {
        // The run's text stays, describing nothing, which is what the same edit leaves in the code window.
        var incoming = Code.Replace("Public Sub Halt()", "'Public Sub Halt()");

        Applied(Replace(Code, incoming), incoming);
    }

    [Theory]
    [InlineData("the qualifier renamed too", "Halt", "Stop")]
    [InlineData("the qualifier left for the save to re-write", "Public Sub Halt()", "Public Sub Stop()")]
    public void AProcedureRenamedWithItsRunIsApplied(string _, string from, string to)
    {
        var incoming = Code.Replace(from, to);

        Applied(Replace(Code, incoming), incoming);
    }

    [Fact]
    public void AProceduresQualifierChangedAloneIsApplied()
    {
        // VB6 gives a procedure attribute to the procedure above it whatever it names (oracle probes p04, p05a), and
        // the save writes the procedure's name there (task 4.4), so the qualifier is not compared.
        var incoming = Code.Replace("Attribute Halt.", "Attribute Pause.");

        Applied(Replace(Code, incoming), incoming);
    }

    [Theory]
    [InlineData("the qualifier renamed too", "Total", "Sum")]
    [InlineData("the qualifier left, a rename's leftover", "Public Total As Long", "Public Sum As Long")]
    public void AVariableRenamedWithItsRunIsApplied(string _, string from, string to)
    {
        var code = Code.Replace("    Total = 0\r\n", "");
        var incoming = code.Replace(from, to);

        Applied(Replace(code, incoming), incoming);
    }

    [Fact]
    public void AVariableWhoseQualifierIsInAnotherCaseIsRenamedWithItToo()
    {
        // The reader takes a VB_Var line for its variable's without case, as VB6 compares names, so the comparison
        // reads that qualifier as standing for the variable the same way.
        var code = Code.Replace("    Total = 0\r\n", "").Replace("Attribute Total.", "Attribute total.");
        var incoming = code.Replace("Public Total As Long", "Public Sum As Long");

        Applied(Replace(code, incoming), incoming);
    }

    [Theory]
    [InlineData("in another case", "Attribute total.")]
    [InlineData("to a name declared nowhere, which the save writes back (task 4.4)", "Attribute Gone.")]
    public void AVariablesOwnQualifierChangedSoThatItStillDescribesTheVariablePasses(string _, string qualifier)
    {
        // The cost of reading a variable's own qualifier as the member, accepted (design.md, "Settled while building
        // 4.5b"): the run still describes Total, as a VB_Var line without case or a rename's leftover, so nothing
        // VB6 binds has changed.
        var code = Code.Replace("    Total = 0\r\n", "");
        var incoming = code.Replace("Attribute Total.", qualifier);

        Applied(Replace(code, incoming), incoming);
    }

    [Theory]
    [InlineData("Public Function Halt() As Long", "End Function")]
    [InlineData("Public Property Get Halt() As Long", "End Property")]
    public void AProcedureRetypedKeepsItsRun(string declaration, string end)
    {
        // VB6 keeps a procedure's run across Function to Sub and back (IDE probe V3). A property accessor retyped
        // goes the same way: its declaration went and another came, and the run came with it.
        var incoming = Code.Replace("Public Sub Halt()", declaration).Replace("    Total = 0\r\nEnd Sub", "    Total = 0\r\n" + end);

        Applied(Replace(Code, incoming), incoming);
    }

    [Fact]
    public void AnOrphanChangedOrRemovedIsFree()
    {
        // Under a blank line a procedure attribute reaches nothing (oracle probe p01), so it is inert text.
        var code = Code.Replace("Public Sub Pause()\r\n", "Public Sub Pause()\r\n\r\nAttribute Pause.VB_Description = \"x\"\r\n");

        Applied(Replace(code, code.Replace("\"x\"", "\"y\"")), code.Replace("\"x\"", "\"y\""));
        Applied(Replace(code, Code), Code);
    }

    // -- The property: matched by its accessor, not by the run's ordinal ---------------------------------

    [Fact]
    public void APropertysAccessorsSwappedEachWithItsRunAreApplied()
    {
        const string get =
            "Public Property Get Value() As Long\r\n" +
            "Attribute Value.VB_Description = \"The value\"\r\n" +
            "    Value = m\r\n" +
            "End Property\r\n" +
            "\r\n";
        var incoming = Property.Replace(get, "") + "\r\n" + get.TrimEnd('\r', '\n') + "\r\n";

        Applied(Replace(Property, incoming), incoming);
    }

    [Fact]
    public void APropertysFirstAccessorDeletedWithItsRunLeavesTheOthersRunAsItWas()
    {
        // The Let's run is now the property's first, so a key's ordinal would compare it with the Get's.
        var incoming = Property.Replace(
            "Public Property Get Value() As Long\r\nAttribute Value.VB_Description = \"The value\"\r\n    Value = m\r\nEnd Property\r\n\r\n", "");

        Applied(Replace(Property, incoming), incoming);
    }

    [Fact]
    public void APropertysRunMovedFromItsGetToItsLetIsRefused()
    {
        // VB6 reads a property's description from its first accessor alone, so the run under the Let would reach
        // nothing (oracle probe p14e), though the property's name still carries it.
        var code = Property.Replace("Attribute Value.VB_HelpID = 7\r\n", "");
        var incoming = code.Replace("Attribute Value.VB_Description = \"The value\"\r\n", "")
            .Replace("Public Property Let Value(ByVal v As Long)\r\n", "Public Property Let Value(ByVal v As Long)\r\nAttribute Value.VB_Description = \"The value\"\r\n");

        var refusal = Refused(Replace(code, incoming));

        refusal.Should().Contain("Property Get Value are removed, though its declaration stays")
            .And.Contain("Property Let Value are added");
    }

    [Fact]
    public void APropertyRenamedWithBothItsRunsIsApplied()
    {
        var incoming = Property.Replace("Value", "Amount");

        Applied(Replace(Property, incoming), incoming);
    }

    [Fact]
    public void ARunAddedToAPropertysSecondAccessorIsRefusedNamingThatAccessor()
    {
        var code = Property.Replace("Attribute Value.VB_HelpID = 7\r\n", "");

        Refused(Replace(code, Property)).Should().Contain("Attribute lines describing Property Let Value are added")
            .And.NotContain("Property Get Value");
    }

    // -- What is refused, and how the member is named ----------------------------------------------------

    [Fact]
    public void AChangedDescriptionIsRefusedNamingTheMember()
    {
        var refusal = Refused(Replace(Code, Code.Replace("Stops the order", "Ends the order")));

        refusal.Should().Contain("the Attribute lines describing Halt are changed")
            .And.Contain("which only the IDE changes").And.EndWith("Nothing was changed.");
    }

    [Fact]
    public void ARunRemovedWhileItsDeclarationStaysIsRefused()
    {
        var refusal = Refused(Replace(Code, Code.Replace("Attribute Halt.VB_Description = \"Stops the order\"\r\n", "")));

        refusal.Should().Contain("the Attribute lines describing Halt are removed, though its declaration stays");
    }

    [Fact]
    public void ARunSeparatedFromItsDeclarationIsRefused()
    {
        // Under a comment the run reaches nothing (oracle probe p02), so this loses it as surely as deleting it.
        var refusal = Refused(Replace(Code, Code.Replace("Public Sub Halt()\r\n", "Public Sub Halt()\r\n' Stops it\r\n")));

        refusal.Should().Contain("describing Halt are removed");
    }

    [Fact]
    public void AVariablesRunPointedAtAnotherDeclaredVariableIsRefused()
    {
        // A VB_Var line binds by the name it carries, so a line naming m is no longer Total's, wherever it sits.
        var refusal = Refused(Replace(Code, Code.Replace("Attribute Total.", "Attribute m.")));

        refusal.Should().Contain("describing Total are removed");
    }

    [Fact]
    public void ARenameThatAlsoChangesTheDescriptionIsRefusedAsLinesAddedToTheNewName()
    {
        // Text cannot tell a rename from a deletion beside a new procedure. Halt's run went with Halt's declaration,
        // which is allowed; Stop's is a run the document does not have.
        var incoming = Code.Replace("Halt", "Stop").Replace("Stops the order", "Ends the order");

        var refusal = Refused(Replace(Code, incoming));

        refusal.Should().Contain("Attribute lines describing Stop are added").And.NotContain("Halt")
            .And.Contain("Renaming a member and changing its Attribute lines in one replacement");
    }

    [Fact]
    public void ARunMovedOntoAMemberTheDocumentAlreadyDeclaresIsRefused()
    {
        // Halt deleted and Pause given its run: not a rename, because Pause was declared before.
        var incoming = Code
            .Replace("Public Sub Halt()\r\nAttribute Halt.VB_Description = \"Stops the order\"\r\n    Total = 0\r\nEnd Sub\r\n\r\n", "")
            .Replace("Public Sub Pause()\r\n", "Public Sub Pause()\r\nAttribute Pause.VB_Description = \"Stops the order\"\r\n");

        Refused(Replace(Code, incoming)).Should().Contain("Attribute lines describing Pause are added").And.NotContain("Halt");
    }

    [Fact]
    public void ARunMovedOntoANewMemberWhileItsOwnStaysDeclaredIsRefused()
    {
        // Stop is new, but Halt is still declared, so Halt's run did not go with a rename.
        var incoming = Code
            .Replace("Attribute Halt.VB_Description = \"Stops the order\"\r\n", "")
            .Replace("Public Sub Pause()\r\n", "Public Sub Stop()\r\nAttribute Stop.VB_Description = \"Stops the order\"\r\nEnd Sub\r\n\r\nPublic Sub Pause()\r\n");

        Refused(Replace(Code, incoming)).Should().Contain("describing Halt are removed, though its declaration stays")
            .And.Contain("describing Stop are added");
    }

    [Fact]
    public void ALinePastedUnderABareDeclarationIsRefusedAsAnAddition()
    {
        // In the code window such a line is a run at once (task 4.5a). In a replacement it is text the caller wrote.
        var incoming = Code.Replace("Public Sub Pause()\r\n", "Public Sub Pause()\r\nAttribute Pause.VB_UserMemId = 0\r\n");

        Refused(Replace(Code, incoming)).Should().Contain("Attribute lines describing Pause are added");
    }

    [Fact]
    public void AnOrphanTurnedIntoARunIsRefusedAsAnAddition()
    {
        // The orphan is free text, so a caller that could promote one could add any run in two calls.
        var code = Code.Replace("Public Sub Pause()\r\n", "Public Sub Pause()\r\n\r\nAttribute Pause.VB_Description = \"x\"\r\n");
        var incoming = code.Replace("Public Sub Pause()\r\n\r\n", "Public Sub Pause()\r\n");

        Refused(Replace(code, incoming)).Should().Contain("Attribute lines describing Pause are added");
    }

    [Fact]
    public void EveryMemberIsNamed()
    {
        var incoming = Code.Replace("The running total", "The total")
            .Replace("Attribute Halt.VB_Description = \"Stops the order\"\r\n", "")
            .Replace("Public Sub Pause()\r\n", "Public Sub Pause()\r\nAttribute Pause.VB_Description = \"Waits\"\r\n");

        var refusal = Refused(Replace(Code, incoming));

        refusal.Should().Contain("describing Total are changed")
            .And.Contain("describing Halt are removed")
            .And.Contain("describing Pause are added");
    }

    [Fact]
    public void AVariablesRunNamesEveryVariableItDescribes()
    {
        // One run under a line declaring two, each line naming its own: a hand-written comma list.
        const string code =
            "Option Explicit\r\n" +
            "Public A As Long, B As Long\r\n" +
            "Attribute A.VB_VarDescription = \"First\"\r\n" +
            "Attribute B.VB_VarDescription = \"Second\"\r\n";

        Refused(Replace(code, code.Replace("Second", "Other"))).Should().Contain("the Attribute lines describing A and B are changed");
    }

    [Fact]
    public void AVariablesQualifierIsReadAsWrittenWhereItNamesAnotherDeclarator()
    {
        // Each line of that run names the declarator it binds to, so pointing B's line at A changes what it describes.
        const string code =
            "Option Explicit\r\n" +
            "Public A As Long, B As Long\r\n" +
            "Attribute A.VB_VarDescription = \"First\"\r\n" +
            "Attribute B.VB_VarDescription = \"Second\"\r\n";

        Refused(Replace(code, code.Replace("Attribute B.", "Attribute A."))).Should().Contain("describing A");
    }

    [Fact]
    public void TheWholeFileSentWithAChangedRunIsRefusedToo()
    {
        var current = ClassHeader + Code;

        var result = GuardedContent.Replace(current, ClassHeader.Length, current.Replace("Stops the order", "Ends"));

        result.NewText.Should().BeNull();
        result.Refusal.Should().Contain("describing Halt are changed");
    }

    [Fact]
    public void AFormsRunsAreCheckedBelowItsAttributeLines()
    {
        // A form's header straddles its prefix: the designer block, then the code's leading Attribute lines.
        const string designer =
            "VERSION 5.00\r\n" +
            "Begin VB.Form frmOrders \r\n" +
            "   Caption         =   \"Orders\"\r\n" +
            "End\r\n";
        const string code =
            "Attribute VB_Name = \"frmOrders\"\r\n" +
            "Attribute VB_PredeclaredId = True\r\n" +
            "Option Explicit\r\n" +
            "Public Sub Halt()\r\n" +
            "Attribute Halt.VB_Description = \"Stops\"\r\n" +
            "End Sub\r\n";

        var result = GuardedContent.Replace(designer + code, designer.Length, code.Replace("Stops", "Ends"));

        result.Refusal.Should().Contain("describing Halt are changed");
    }

    // -- Counting declarations, on text VB6 refuses -----------------------------------------------------
    //
    // A name declared twice is "Ambiguous name detected" to VB6, but a caller may send it, and the guard stays sound
    // on it: one declaration gone lets one run go or be renamed, not two. On a module VB6 accepts, no declaration is
    // written twice, so each count is at most one.

    private const string HaltTwice =
        "Option Explicit\r\n" +
        "Public Sub Halt()\r\n" +
        "Attribute Halt.VB_Description = \"First\"\r\n" +
        "End Sub\r\n" +
        "Public Sub Halt()\r\n" +
        "Attribute Halt.VB_Description = \"Second\"\r\n" +
        "End Sub\r\n";

    [Fact]
    public void OneDeclarationGoneLetsOneRunGo()
    {
        Refused(Replace(HaltTwice, "Option Explicit\r\nPublic Sub Halt()\r\nEnd Sub\r\n"))
            .Should().Contain("describing Halt are removed");
    }

    [Fact]
    public void ARenameSpendsTheDeclarationItTookTheRunFrom()
    {
        const string incoming =
            "Option Explicit\r\n" +
            "Public Sub Stop()\r\n" +
            "Attribute Stop.VB_Description = \"First\"\r\n" +
            "End Sub\r\n" +
            "Public Sub Halt()\r\n" +
            "End Sub\r\n";

        Refused(Replace(HaltTwice, incoming)).Should().Contain("describing Halt are removed").And.NotContain("Stop");
    }

    [Fact]
    public void ARenameSpendsTheDeclarationItGaveTheRunTo()
    {
        // Stop was declared once and is declared twice now, so one run may arrive under it by a rename, not two.
        const string code =
            "Option Explicit\r\n" +
            "Public Sub Halt()\r\nAttribute Halt.VB_Description = \"x\"\r\nEnd Sub\r\n" +
            "Public Sub Pause()\r\nAttribute Pause.VB_Description = \"x\"\r\nEnd Sub\r\n" +
            "Public Sub Stop()\r\nEnd Sub\r\n";
        const string incoming =
            "Option Explicit\r\n" +
            "Public Sub Stop()\r\nAttribute Stop.VB_Description = \"x\"\r\nEnd Sub\r\n" +
            "Public Sub Stop()\r\nAttribute Stop.VB_Description = \"x\"\r\nEnd Sub\r\n";

        Refused(Replace(code, incoming)).Should().Contain("describing Stop are added");
    }

    // -- Naming a run's member for a range writer's refusal ----------------------------------------------

    [Fact]
    public void ARunsRegionNamesItsMemberAndTheHeadersNamesNone()
    {
        var text = ClassHeader + Property;
        var regions = ReadOnlyRegions.Of(text, ClassHeader.Length);

        regions.Select(r => GuardedContent.MemberNamedBy(text, ClassHeader.Length, r))
            .Should().Equal(null, "Property Get Value", "Property Let Value");
    }
}
