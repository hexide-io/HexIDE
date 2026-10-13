using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Serialization;

/// <summary>
/// Which runs of <c>Attribute</c> lines describe a member, and which line each one describes
/// (hexide-io/HexIDE#273 task 4.1; design.md, "Settled before building phase 4").
/// </summary>
/// <remarks>
/// <para>
/// The rule is VB6's as the oracle measured it. A procedure attribute goes to the procedure declared directly
/// above it whatever its qualifier names, and a <c>VB_Var…</c> line to the module-level variable or constant
/// it names. A run describes a member when it sits directly under a declaration and VB6 reads every line of it
/// as that declaration's; anything else is an orphan, which is inert text rather than a read-only region, so
/// every orphan case here also asserts that <see cref="ReadOnlyRegions.Of"/> leaves it out.
/// </para>
/// <para>
/// Where a shape is one VB6 wrote, through Tools > Procedure Attributes on the oracle guest, the test says so.
/// Offsets are asserted exactly, as in <see cref="ReadOnlyRegionsTests"/>: the folds, Enter, deletion and the
/// save's re-qualifying that phase 4 builds on these all start or end at them.
/// </para>
/// </remarks>
public class MemberRunsTests
{
    private const string ClassHeader =
        "VERSION 1.0 CLASS\r\n" +
        "BEGIN\r\n" +
        "  MultiUse = -1  'True\r\n" +
        "END\r\n" +
        "Attribute VB_Name = \"Tide\"\r\n" +
        "Attribute VB_Exposed = False\r\n";

    private static IReadOnlyList<MemberRun> RunsOf(string code) => ReadOnlyRegions.MemberRuns(ClassHeader + code, ClassHeader.Length);

    private static IReadOnlyList<TextRegion> RegionsOf(string code) => ReadOnlyRegions.Of(ClassHeader + code, ClassHeader.Length);

    private static int At(string code, string text) => ClassHeader.Length + code.IndexOf(text, StringComparison.Ordinal);

    // -- Runs that describe a procedure --------------------------------------------------------------

    [Fact]
    public void AProceduresRunDescribesItsDeclaration()
    {
        const string code =
            "Option Explicit\r\n" +
            "Public Function Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"The order total\"\r\n" +
            "Attribute Total.VB_UserMemId = 0\r\n" +
            "    Total = 0\r\n" +
            "End Function\r\n";
        var anchor = At(code, "\r\nAttribute Total.VB_Description");
        var first = At(code, "Attribute Total.VB_Description");
        var second = At(code, "Attribute Total.VB_UserMemId");

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Lines.Should().Equal(
            new AttributeLine(first, second - 2, AttributeLineKind.Procedure, "Total", first + "Attribute ".Length),
            new AttributeLine(second, At(code, "\r\n    Total = 0"), AttributeLineKind.Procedure, "Total", second + "Attribute ".Length));
        run.DescribedStart.Should().Be(At(code, "Public Function"));
        run.Anchor.Should().Be(anchor);
        run.Region.Should().Be(new TextRegion(first, At(code, "    Total = 0"), Anchor: anchor));
        run.LastContentEnd.Should().Be(At(code, "\r\n    Total = 0"));
        run.Declared.Should().Equal(new DeclaredName("Total", At(code, "Total() As"), DeclarationKind.Procedure));
        run.Kind.Should().Be(MemberRunKind.Procedure);
        run.Member.Should().Be(run.Declared[0]);
        run.Key.Should().Be(new MemberRunKey("TOTAL", 0));
        run.DescribesMember.Should().BeTrue();
        RegionsOf(code).Should().HaveCount(2).And.Contain(run.Region, "a run that describes a member is a read-only region");
    }

    [Theory]
    [InlineData("Attribute Gone.VB_Description = \"x\"", "naming nothing (oracle probe p04)")]
    [InlineData("Attribute Go.VB_Description = \"x\"", "naming another procedure of the module (p05a, p05b)")]
    [InlineData("Attribute Value.VB_Description = \"x\"", "naming its own parameter")]
    [InlineData("Attribute HALT.VB_Description = \"x\"", "naming it in another case")]
    [InlineData("Attribute VB_Varnish.VB_Description = \"x\"", "naming a name that starts VB_Var, which is no variable attribute")]
    [InlineData("Attribute Halt.VB_Description = \"x\"\r\nAttribute Go.VB_HelpID = 7", "naming it and then another (p05d)")]
    [InlineData("attribute halt.VB_Description = \"x\"", "in lower case (p12)")]
    [InlineData("    Attribute Halt.VB_Description = \"x\"", "indented (p12d)")]
    public void AProceduresRunDescribesItWhateverItsQualifiersName(string run, string because)
    {
        // VB6 gives a procedure attribute to the procedure declared directly above it and does not read its
        // qualifier at all. So the run is the procedure's, keyed by the procedure's name and not the qualifier's,
        // and its qualifiers are what the save re-writes (4.4).
        var code =
            "Public Sub Go()\r\n" +
            "End Sub\r\n" +
            "Public Sub Halt(ByVal Value As Long)\r\n" +
            run + "\r\n" +
            "End Sub\r\n";

        var described = RunsOf(code).Should().ContainSingle(because).Subject;

        described.Kind.Should().Be(MemberRunKind.Procedure, because);
        described.Member.Should().Be(new DeclaredName("Halt", At(code, "Halt("), DeclarationKind.Procedure), because);
        described.Key.Should().Be(new MemberRunKey("HALT", 0), because);
        RegionsOf(code).Should().Contain(described.Region, because);
    }

    [Theory]
    [InlineData("Private Sub Hidden()", "Hidden")]
    [InlineData("Public Sub Noted() ' trailing comment", "Noted")]
    [InlineData("Friend Function Pal() As Long", "Pal")]
    [InlineData("Public Static Function Kept() As Long", "Kept")]
    [InlineData("Public Property Let WriteOnly(ByVal Value As Long)", "WriteOnly")]
    [InlineData("Public Property Set Target(ByVal o As Object)", "Target")]
    [InlineData("Public Event Clicked()", "Clicked")]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long", "GetTickCount")]
    [InlineData("Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long", "GetTickCount")]
    public void EveryKindOfProcedureDeclarationTakesARun(string declaration, string name)
    {
        // The first eight are lines VB6's own dialog wrote a run under (DeclShapes.cls and Panel.ctl on the
        // oracle guest); PtrSafe is VBA7's Declare, read for the VBA personalities.
        var code = declaration + "\r\nAttribute " + name + ".VB_Description = \"x\"\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Kind.Should().Be(MemberRunKind.Procedure);
        run.Member!.Value.Name.Should().Be(name);
    }

    [Fact]
    public void AContinuedDeclarationIsDescribedFromItsFirstLineAndAnchoredAtItsLast()
    {
        // VB6 wrote this one (Ledger.cls). The trailing spaces after the second continuation are the ones the
        // parity test gives every line, and must not end the logical line.
        const string code =
            "Public Function Average(ByVal First As Long, _\r\n" +
            "                        ByVal Second As Long, _  \r\n" +
            "                        ByVal Third As Long) As Double\r\n" +
            "Attribute Average.VB_Description = \"Mean of three values.\"\r\n" +
            "End Function\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.DescribedStart.Should().Be(At(code, "Public Function"), "the described line starts on its first physical line");
        run.Anchor.Should().Be(At(code, "\r\nAttribute"), "and ends on its last");
        run.Declared.Should().Equal(new DeclaredName("Average", At(code, "Average("), DeclarationKind.Procedure));
        run.Kind.Should().Be(MemberRunKind.Procedure);
    }

    [Fact]
    public void AnAttributeLineContinuedWithAnUnderscoreIsOneLineOfTheRun()
    {
        // Oracle probes p11a and p11b: VB6 reads each as one attribute line, the name on the second physical line
        // in the second, and gives both to the procedure.
        const string code =
            "Public Sub Foo()\r\n" +
            "Attribute Foo.VB_Description = _\r\n" +
            "\"p11a\"\r\n" +
            "Attribute _\r\n" +
            "foo.VB_HelpID = 3\r\n" +
            "End Sub\r\n";
        var first = At(code, "Attribute Foo");
        var second = At(code, "Attribute _");

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Lines.Should().Equal(
            new AttributeLine(first, second - 2, AttributeLineKind.Procedure, "Foo", first + "Attribute ".Length),
            new AttributeLine(second, At(code, "\r\nEnd Sub"), AttributeLineKind.Procedure, "foo", At(code, "foo.VB_HelpID")));
        run.Region.Should().Be(new TextRegion(first, At(code, "End Sub"), Anchor: first - 2), "all four physical lines");
        run.LastContentEnd.Should().Be(At(code, "\r\nEnd Sub"));
        run.Kind.Should().Be(MemberRunKind.Procedure);
    }

    [Theory]
    [InlineData("Public Sub Foo(ByVal a As Long, _\r\nAttribute Foo.VB_Description = \"p07\"\r\nByVal b As Long)\r\nEnd Sub\r\n",
        "inside a continued declaration, where it is code and a syntax error (oracle probe p07)")]
    [InlineData("Public Sub Foo()\r\n' a comment _\r\nAttribute Foo.VB_Description = \"p07c\"\r\nEnd Sub\r\n",
        "continuing a comment, which it is part of (p07c)")]
    public void ALineThatContinuesTheLineAboveIsNeverAnAttributeLine(string code, string because)
    {
        RunsOf(code).Should().BeEmpty(because);
        RegionsOf(code).Should().ContainSingle(because);
    }

    [Fact]
    public void ANameIsAProcedureAttributeUnlessWhatFollowsItsFirstDotStartsVbVar()
    {
        // Both from Panel.ctl, which VB6 wrote: a property's VB_ProcData is a procedure attribute with two dots,
        // and a variable's VB_VarProcData is a variable attribute. The qualifier is the name before the first dot.
        const string code =
            "Public Spare As Long\r\n" +
            "Attribute Spare.VB_VarProcData = \";Behavior\"\r\n" +
            "Attribute spare.vb_vardescription = \"A spare.\"\r\n" +
            "Public Property Get Full() As Long\r\n" +
            "Attribute Full.VB_ProcData.VB_Invoke_Property = \"StandardColor;Appearance\"\r\n" +
            "End Property\r\n";

        var runs = RunsOf(code);

        runs.Select(r => r.Lines.Select(l => (l.Kind, l.Qualifier))).Should().SatisfyRespectively(
            spare => spare.Should().Equal((AttributeLineKind.Variable, "Spare"), (AttributeLineKind.Variable, "spare")),
            full => full.Should().Equal((AttributeLineKind.Procedure, "Full")));
        runs.Select(r => r.Kind).Should().Equal(MemberRunKind.Variable, MemberRunKind.Procedure);
    }

    private const string PropertyPair =
        "Public Property Get Value() As Long\r\n" +
        "Attribute Value.VB_UserMemId = 0\r\n" +
        "    Value = m\r\n" +
        "End Property\r\n" +
        "Public Property Let Value(ByVal v As Long)\r\n" +
        "Attribute value.VB_Description = \"The value\"\r\n" +
        "    m = v\r\n" +
        "End Property\r\n";

    [Fact]
    public void APropertysGetAndLetEachDescribeTheirOwnRunUnderDistinctKeys()
    {
        // A name alone cannot tell a property's procedures apart, so the key carries an ordinal.
        var runs = RunsOf("Private m As Long\r\n" + PropertyPair);

        runs.Should().HaveCount(2).And.OnlyContain(r => r.Kind == MemberRunKind.Procedure);
        runs.Select(r => r.Key).Should().Equal(new MemberRunKey("VALUE", 0), new MemberRunKey("VALUE", 1));
        RegionsOf("Private m As Long\r\n" + PropertyPair).Should().HaveCount(3);
    }

    // -- Runs that describe a variable or a constant -----------------------------------------------------

    [Fact]
    public void AWithEventsVariablesHelpIdRunDescribesItsDeclaration()
    {
        // VB6 writes a VB_VarHelpID line under a WithEvents variable (Tariff.cls, written on the oracle guest).
        const string code =
            "Option Explicit\r\n" +
            "Private mRate As Double\r\n" +
            "Private WithEvents mLedger As Ledger\r\n" +
            "Attribute mLedger.VB_VarHelpID = -1\r\n" +
            "\r\n" +
            "Private Sub Class_Initialize()\r\n" +
            "End Sub\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Should().Equal(new DeclaredName("mLedger", At(code, "mLedger As"), DeclarationKind.Variable));
        run.Kind.Should().Be(MemberRunKind.Variable);
        run.Member.Should().Be(run.Declared[0]);
        run.Key.Should().Be(new MemberRunKey("MLEDGER", 0));
        run.Region.End.Should().Be(At(code, "\r\nPrivate Sub"), "the blank line after the run is the developer's");
    }

    [Fact]
    public void ARunUnderADeclarationWithATrailingCommentDescribesIt()
    {
        // The one member run in the VB98 template tree, in ADDIN.DSR: the comment is part of the described line,
        // so the anchor is after it.
        const string code =
            "Public WithEvents MenuHandler As CommandBarEvents          'command bar event handler\r\n" +
            "Attribute MenuHandler.VB_VarHelpID = -1\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Kind.Should().Be(MemberRunKind.Variable);
        run.Anchor.Should().Be(At(code, "\r\nAttribute"));
        run.Declared.Select(d => d.Name).Should().Equal("MenuHandler");
    }

    [Fact]
    public void ACommaListDeclaresEveryNameAndOneRunMayDescribeSeveralOfThem()
    {
        // Ledger.cls, which VB6 wrote: one run under a two-name line, two lines for A and one for B.
        const string code =
            "Public Owner As String\r\n" +
            "Public A As Long, B As Long\r\n" +
            "Attribute A.VB_VarMemberFlags = \"40\"\r\n" +
            "Attribute A.VB_VarHelpID = 5\r\n" +
            "Attribute B.VB_VarDescription = \"Second counter.\"\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Select(d => d.Name).Should().Equal("A", "B");
        run.Lines.Select(l => l.Qualifier).Should().Equal("A", "A", "B");
        run.Kind.Should().Be(MemberRunKind.Variable);
        run.Member.Should().Be(new DeclaredName("A", At(code, "A As"), DeclarationKind.Variable));
        run.Key.Should().Be(new MemberRunKey("A", 0), "a run naming several declarators is keyed by the first it names");
    }

    [Fact]
    public void ARunIsKeyedByTheFirstDeclaratorItNamesNotTheFirstTheLineDeclares()
    {
        const string code =
            "Public A As Long, B() As String, C(1 To 3, 2) As Integer, WithEvents D As Timer\r\n" +
            "Attribute C.VB_VarDescription = \"c\"\r\n" +
            "Attribute D.VB_VarHelpID = -1\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Select(d => d.Name).Should().Equal("A", "B", "C", "D");
        run.Member!.Value.Name.Should().Be("C");
        run.Key.Should().Be(new MemberRunKey("C", 0));
    }

    [Fact]
    public void ANameOnTheSecondLineOfAContinuedDeclarationIsDeclaredThere()
    {
        const string code =
            "Public A As Long, _\r\n" +
            "       B As Long\r\n" +
            "Attribute B.VB_VarDescription = \"b\"\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Should().Equal(new DeclaredName("A", At(code, "A As"), DeclarationKind.Variable),
            new DeclaredName("B", At(code, "B As"), DeclarationKind.Variable));
        run.Kind.Should().Be(MemberRunKind.Variable);
    }

    [Fact]
    public void EveryStatementOnTheDescribedLineIsRead()
    {
        // Panel.ctl, which VB6 wrote: its dialog put B's run under the line declaring it after a colon (IDE probe E).
        const string code =
            "Public A As Long: Public B As Long\r\n" +
            "Attribute B.VB_VarDescription = \"After a colon.\"\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Declared.Select(d => d.Name).Should().Equal("A", "B");
        run.Kind.Should().Be(MemberRunKind.Variable);
        run.Member.Should().Be(new DeclaredName("B", At(code, "B As"), DeclarationKind.Variable));
    }

    [Theory]
    [InlineData("Public Const C1 = 1, C2 = 2", "C2", "the second of two (ConstMulti.bas)")]
    [InlineData("Private Const Hidden = 5", "Hidden", "private (ConstPriv.bas)")]
    [InlineData("Public Const Typed As Long = 1", "Typed", "typed (ConstTyped.bas)")]
    [InlineData("Const Limit = 5", "Limit", "with no modifier")]
    public void AConstantTakesAVariableRun(string declaration, string name, string because)
    {
        // VB6 keeps a VB_VarDescription under a module-level Const and shows it as the constant's description,
        // though neither its dialog nor its Object Browser offers to make one (IDE probe V5). The first three
        // are files VB6 saved.
        var code = "Option Explicit\r\n" + declaration + "\r\nAttribute " + name + ".VB_VarDescription = \"x\"\r\n";

        var run = RunsOf(code).Should().ContainSingle(because).Subject;

        run.Kind.Should().Be(MemberRunKind.Variable, because);
        run.Member.Should().Be(new DeclaredName(name, At(code, name + " "), DeclarationKind.Constant), because);
    }

    [Fact]
    public void AnEventOrADeclareBeforeAVariableLeavesItInTheDeclarationsSection()
    {
        // Ledger.cls, which VB6 wrote, declares its event above its variables. Neither an Event nor a Declare opens
        // a procedure's body, so what follows is still module-level.
        const string code =
            "Public Event Changed(ByVal Amount As Long)\r\n" +
            "Private Declare Function GetTickCount Lib \"kernel32\" () As Long\r\n" +
            "Public Owner As String\r\n" +
            "Attribute Owner.VB_VarDescription = \"Who\"\r\n";

        RunsOf(code).Should().ContainSingle().Which.Kind.Should().Be(MemberRunKind.Variable);
    }

    // -- The run a rename by retyping leaves --------------------------------------------------------------

    [Fact]
    public void AVariableRenamedByRetypingKeepsItsRunUnderTheNameItsLineDeclares()
    {
        // IDE probe V1: Count retyped as Tally on its declaration. VB6 kept the description, and its save wrote it
        // re-qualified as Tally. Until the save, the run names the old name, which nothing declares any more.
        const string code =
            "Option Explicit\r\n" +
            "\r\n" +
            "Public Tally As Long\r\n" +
            "Attribute Count.VB_VarDescription = \"Var1 Count, described.\"\r\n" +
            "Attribute count.VB_VarHelpID = 3\r\n" +
            "\r\n" +
            "Public Sub Other1()\r\n" +
            "    Debug.Print 1\r\n" +
            "End Sub\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.Kind.Should().Be(MemberRunKind.RenamedVariable);
        run.Member.Should().Be(new DeclaredName("Tally", At(code, "Tally"), DeclarationKind.Variable),
            "the name the save re-qualifies the run to");
        run.Key.Should().Be(new MemberRunKey("TALLY", 0), "keyed by the name its line declares, not the stale one");
        run.Lines.Select(l => (l.Qualifier, l.QualifierOffset)).Should().Equal(
            ("Count", At(code, "Count.VB_")), ("count", At(code, "count.VB_")));
        RegionsOf(code).Should().Contain(run.Region);
    }

    [Theory]
    [InlineData("Public Count As Long\r\n", "another variable of the module")]
    [InlineData("Private Const Count = 1\r\n", "a constant")]
    [InlineData("Public Sub Count()\r\nEnd Sub\r\n", "a procedure")]
    [InlineData("Public Sub Other1()\r\n    Dim Count As Long\r\nEnd Sub\r\n", "a procedure's own local")]
    public void ARunNamingANameTheModuleDeclaresElsewhereIsNotOneARenameLeft(string elsewhere, string because)
    {
        // VB6 binds a VB_Var line by name wherever it sits, so this run is the other Count's. Reading it as Tally's
        // would hand that description to Tally at the next save. A local is not a VB_Var line's target, but the
        // module's names err towards too many, which errs towards an orphan.
        var code =
            "Public Tally As Long\r\n" +
            "Attribute Count.VB_VarDescription = \"x\"\r\n" +
            elsewhere;

        var run = RunsOf(code)[0];

        run.Kind.Should().Be(MemberRunKind.Orphan, because);
        RegionsOf(code).Should().NotContain(r => r.Start == run.Region.Start, because);
    }

    [Fact]
    public void ARunNamingAControlTheDesignerBlockDeclaresIsNotOneARenameLeft()
    {
        const string designer =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Begin VB.CommandButton cmdGo \r\n" +
            "   End\r\n" +
            "End\r\n";
        var text = designer +
                   "Attribute VB_Name = \"Form1\"\r\n" +
                   "Public Spare As Long\r\n" +
                   "Attribute cmdGo.VB_VarDescription = \"x\"\r\n";

        ReadOnlyRegions.MemberRuns(text, designer.Length).Should().ContainSingle()
            .Which.Kind.Should().Be(MemberRunKind.Orphan);
        ReadOnlyRegions.MemberRuns(text.Replace("Attribute cmdGo.", "Attribute cmdStop.", StringComparison.Ordinal), designer.Length)
            .Should().ContainSingle().Which.Kind.Should().Be(MemberRunKind.RenamedVariable, "the control: a name declared nowhere");
    }

    [Theory]
    [InlineData("Public A As Long, C As Long\r\nAttribute B.VB_VarDescription = \"x\"\r\n",
        "a line declaring two, one of them renamed (IDE probe V2)")]
    [InlineData("Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"x\"\r\nAttribute Total.VB_VarHelpID = 3\r\n",
        "lines naming two different names")]
    [InlineData("Public Tally As Long\r\nAttribute Count.VB_VarDescription = \"x\"\r\nAttribute Tally.VB_VarHelpID = 3\r\n",
        "lines naming the declared name and another")]
    public void OnlyARunLeftUnderALineDeclaringOneNameNamingOneOtherIsOneARenameLeft(string code, string because)
    {
        // On a line declaring several, text alone cannot tell which declarator a stale run belonged to, and VB6
        // drops it anyway (design.md, "Renames (4.4)").
        RunsOf(code).Should().ContainSingle(because).Which.Kind.Should().Be(MemberRunKind.Orphan, because);
    }

    // -- The declaration reader ------------------------------------------------------------------------

    [Theory]
    [InlineData("Sub Main()", "Main:P")]
    [InlineData("Private Static Function Twice(ByVal n As Long) As Long", "Twice:P")]
    [InlineData("Friend Property Set Target(ByVal o As Object)", "Target:P")]
    [InlineData("public property let value(byval v as long)", "value:P")]
    [InlineData("Public Event Changed(ByVal Value As Long)", "Changed:P")]
    [InlineData("Private Declare Function GetTickCount Lib \"kernel32\" () As Long", "GetTickCount:P")]
    [InlineData("Declare PtrSafe Sub Sleep Lib \"kernel32\" (ByVal ms As Long)", "Sleep:P")]
    [InlineData("Dim a As Long, b(1 To 3, 4) As String, c", "a:V,b:V,c:V")]
    [InlineData("Global Count%, Name$", "Count:V,Name:V")]
    [InlineData("Private WithEvents Clock As Timer, WithEvents Alarm As Timer", "Clock:V,Alarm:V")]
    [InlineData("Public s As String * 10, t As New Collection", "s:V,t:V")]
    [InlineData("Static Count As Long", "Count:V")]
    [InlineData("Dim [Odd Name] As Long", "Odd Name:V")]
    [InlineData("Public Const C1 = 1, C2 = 2", "C1:C,C2:C")]
    [InlineData("Const Limit = (2 + 3) * 4, Half = Limit / 2", "Limit:C,Half:C")]
    [InlineData("Private Const Lf = 10, Sep = \":\", Tab = 9", "Lf:C,Sep:C,Tab:C")]        // a : in a string is the string's
    [InlineData("Private Const Lf = 10, Q = \"'\", Tab = 9", "Lf:C,Q:C,Tab:C")]           // and so is a '
    [InlineData("Public X As Long: Dim Y, Z", "X:V,Y:V,Z:V")]                              // every statement is read
    [InlineData("Public a As Long ' x, y", "a:V")]                                          // a comment ends the line
    [InlineData("Public a As Long ' x: Dim b", "a:V")]                                      // its : included
    [InlineData("Public a As Long: Rem x: Dim b", "a:V")]                                   // Rem where a statement starts
    [InlineData("Public Total$ ' isn't it", "Total:V")]
    public void TheDeclarationReaderFindsTheNamesALineDeclares(string line, string names)
    {
        ReadOnlyRegions.NamesDeclaredOn(line).Select(d => $"{d.Name}:{d.Kind.ToString()[0]}").Should().Equal(names.Split(','));
    }

    [Theory]
    [InlineData("Total = 0")]
    [InlineData("Call Go")]
    [InlineData("ReDim a(5)")]
    [InlineData("Public Enum Colors")]
    [InlineData("Private Type Point")]
    [InlineData("    Small = 1")]                       // an enum's member
    [InlineData("Option Explicit")]
    [InlineData("Implements IShape")]
    [InlineData("End Sub")]
    [InlineData("Property Total")]
    [InlineData("' Public Sub Go()")]
    [InlineData("Rem Public Sub Go(): Dim b")]          // a leading Rem takes the whole line, its : included
    [InlineData("")]
    public void ALineThatIsNotADeclarationDeclaresNothing(string line)
    {
        ReadOnlyRegions.NamesDeclaredOn(line).Should().BeEmpty();
    }

    [Fact]
    public void EachNameIsFoundWhereItIsWritten()
    {
        const string line = "Public  Function  Total$ (ByVal a As Long)";

        ReadOnlyRegions.NamesDeclaredOn(line).Should().ContainSingle()
            .Which.Should().Be(new DeclaredName("Total", line.IndexOf("Total", StringComparison.Ordinal), DeclarationKind.Procedure),
                "the type character is not part of the name, and the offset is the name's own");
    }

    // -- Continuations ---------------------------------------------------------------------------------

    [Fact]
    public void ANameEndingInAnUnderscoreDoesNotContinueTheLine()
    {
        // m_ is a name. Read as a continuation, it would join the declaration below onto it, and the run under that
        // declaration would describe nothing.
        const string code =
            "Private m_\r\n" +
            "Public Function Foo() As Long\r\n" +
            "Attribute Foo.VB_Description = \"x\"\r\n" +
            "End Function\r\n";

        var run = RunsOf(code).Should().ContainSingle().Subject;

        run.DescribedStart.Should().Be(At(code, "Public Function"));
        run.Kind.Should().Be(MemberRunKind.Procedure);
    }

    [Fact]
    public void ALineOfNothingButAnUnderscoreContinuesOnlyWhenIndented()
    {
        // docs/vb6-fidelity-oracle.md: in column 1 it is illegal, and so continues nothing; indented it is a
        // continuation. VB6 rejects the first file; the rule is what is pinned here.
        const string unindented =
            "Dim q As Long, _\r\n" +
            "_\r\n" +
            "Public Sub Foo()\r\n" +
            "Attribute Foo.VB_Description = \"x\"\r\n" +
            "End Sub\r\n";
        var indented = unindented.Replace("\r\n_\r\n", "\r\n  _\r\n", StringComparison.Ordinal);

        var run = RunsOf(unindented).Should().ContainSingle().Subject;
        run.DescribedStart.Should().Be(At(unindented, "Public Sub"));
        run.Kind.Should().Be(MemberRunKind.Procedure);

        run = RunsOf(indented).Should().ContainSingle().Subject;
        run.DescribedStart.Should().Be(At(indented, "Dim q"), "the declaration is the third line of one logical line");
        run.Kind.Should().Be(MemberRunKind.Orphan, "a line declaring q and a variable called Public, by this reading");
    }

    // -- Orphans ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Public Sub Go()\r\n\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n", "under a blank line (oracle probe p01)")]
    [InlineData("Public Sub Go()\r\n' describes Go\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n", "under a comment (p02)")]
    [InlineData("Public Sub Go()\r\n    Debug.Print 1\r\nAttribute Go.VB_Description = \"x\"\r\nEnd Sub\r\n", "under a body statement (p03)")]
    [InlineData("Public Sub Go()\r\nEnd Sub\r\nAttribute Go.VB_Description = \"x\"\r\n", "under the end of the member it names (p05e)")]
    [InlineData("Option Explicit\r\nAttribute VB_Description = \"x\"\r\n", "a line with no qualifier below the code (p06a)")]
    [InlineData("Public Sub Go()\r\nAttribute VB_Description = \"x\"\r\nEnd Sub\r\n", "a line with no qualifier under a declaration (p06c)")]
    [InlineData("Public Sub Go()\r\nAttribute Go.VB_Description = \"x\"\r\nAttribute A.VB_VarDescription = \"y\"\r\nEnd Sub\r\n", "a run mixing the two kinds (p09n)")]
    [InlineData("Public Total As Long\r\nAttribute Total.VB_Description = \"x\"\r\n", "a procedure attribute under a variable (p09i)")]
    [InlineData("Public Const Rate = 5\r\nAttribute Rate.VB_Description = \"x\"\r\n", "a procedure attribute under a constant (IDE probe V5)")]
    [InlineData("Public Sub Go()\r\nAttribute Go.VB_VarHelpID = -1\r\nEnd Sub\r\n", "a variable attribute under a procedure")]
    [InlineData("Public Event Changed()\r\nAttribute Changed.VB_VarDescription = \"x\"\r\n", "a variable attribute under an event")]
    [InlineData("Public Event Changed()\r\nAttribute Count.VB_VarDescription = \"x\"\r\n", "a variable attribute naming nothing under an event, which is no rename's")]
    [InlineData("Public A As Long: Public Sub Go()\r\nAttribute A.VB_VarDescription = \"x\"\r\nEnd Sub\r\n", "a variable attribute under a line that opens a procedure too")]
    [InlineData("Public A As Long\r\nAttribute A.VB_VarDescription = \"x\"\r\nAttribute A.VB_Description = \"y\"\r\n", "a variable's run with a procedure attribute in it")]
    [InlineData("Public Enum Sizes\r\nAttribute Sizes.VB_VarDescription = \"x\"\r\n    Small = 1\r\nEnd Enum\r\n", "under an Enum (V5)")]
    [InlineData("Public Enum Sizes\r\n    Small = 1\r\nAttribute Small.VB_VarDescription = \"x\"\r\nEnd Enum\r\n", "under an enum's member (V5)")]
    [InlineData("Private Type Point\r\nAttribute Point.VB_VarDescription = \"x\"\r\n    X As Long\r\nEnd Type\r\n", "under a Type")]
    [InlineData("Public Sub Go()\r\n    Dim t As Long\r\nAttribute t.VB_VarDescription = \"x\"\r\nEnd Sub\r\n", "under a procedure's own local")]
    [InlineData("Public Sub Go()\r\nEnd Sub\r\nPublic Late As Long\r\nAttribute Late.VB_VarDescription = \"x\"\r\n", "under a variable after the first procedure, which VB6 refuses")]
    [InlineData("Public Sub Go()\r\nAttribute = 5\r\nEnd Sub\r\n", "a line naming nothing")]
    public void ARunVb6DoesNotGiveToTheLineAboveDescribesNothingAndIsNoRegion(string code, string because)
    {
        var run = RunsOf(code).Should().ContainSingle(because).Subject;

        run.Kind.Should().Be(MemberRunKind.Orphan, because);
        run.DescribesMember.Should().BeFalse(because);
        run.Member.Should().BeNull(because);
        run.Key.Should().BeNull("an orphan is not counted among a member's runs");
        RegionsOf(code).Should().ContainSingle("only the header is read-only: " + because)
            .Which.Anchor.Should().BeNull();

        var text = ClassHeader + code;
        var line = text[..run.Region.Start].Count(c => c == '\n') + 1;
        ReadOnlyRegions.IsReadOnlyLine(text, RegionsOf(code), line).Should().BeFalse("its line takes a mark like any other");
    }

    [Theory]
    [InlineData("Attribute = 5")]
    [InlineData("Attribute(2) = 5")]
    [InlineData("ATTRIBUTE")]
    public void ALineWhoseFirstWordIsAttributeIsAnAttributeLineHoweverItGoesOn(string line)
    {
        // Attribute is a reserved word in code (oracle probe p07e), so such a line is no assignment or array. It
        // names nothing, so it describes nothing either.
        var run = RunsOf("Sub Main()\r\n" + line + "\r\nEnd Sub\r\n").Should().ContainSingle().Subject;

        run.Lines.Should().ContainSingle().Which.Should().Be(
            new AttributeLine(run.Region.Start, run.LastContentEnd, AttributeLineKind.Dotless, null, -1));
        run.Kind.Should().Be(MemberRunKind.Orphan);
    }

    [Theory]
    [InlineData("AttributeCount = 3")]
    [InlineData("Attribute_ = 3")]
    [InlineData("' Attribute X.VB_Description = \"x\"")]
    public void ALineWhoseFirstWordIsAnotherIsNotAnAttributeLine(string line)
    {
        RunsOf("Sub Main()\r\n" + line + "\r\nEnd Sub\r\n").Should().BeEmpty();
    }

    [Fact]
    public void ABlankLineInsideAMembersRunLeavesTheHalfAfterItAnOrphan()
    {
        // A blank line ends a run, so the second half hangs from the blank line. VB6 reaches nothing with that half
        // either, and drops it at its next save (IDE probe B, SplitRun).
        const string code =
            "Public Function Total() As Currency\r\n" +
            "Attribute Total.VB_Description = \"x\"\r\n" +
            "\r\n" +
            "Attribute Total.VB_UserMemId = 0\r\n" +
            "End Function\r\n";

        var runs = RunsOf(code);

        runs.Select(r => r.Kind).Should().Equal(MemberRunKind.Procedure, MemberRunKind.Orphan);
        RegionsOf(code).Should().HaveCount(2).And.Contain(runs[0].Region).And.NotContain(runs[1].Region);
    }

    [Fact]
    public void ARunThatDescribesAndAnOrphanSitSideBySide()
    {
        const string code =
            "Public Sub Go()\r\n" +
            "Attribute Go.VB_Description = \"Goes\"\r\n" +
            "    Debug.Print 1\r\n" +
            "Attribute Go.VB_UserMemId = 0\r\n" +
            "End Sub\r\n";

        var runs = RunsOf(code);

        runs.Select(r => r.DescribesMember).Should().Equal(true, false);
        RegionsOf(code).Skip(1).Should().Equal(runs[0].Region);
    }

    // -- Keys ------------------------------------------------------------------------------------------

    [Fact]
    public void TheKeysOutliveAnEditAboveAndAnOrphanBeforeThem()
    {
        // What 4.3's remembered fold state and 4.5b's comparison stand on: offsets move, keys do not. An orphan
        // naming the same member is free text, so it does not shift the ordinals either.
        var before = RunsOf("Private m As Long\r\n" + PropertyPair);
        var after = RunsOf(
            "' a new comment\r\n" +
            "Private m As Long\r\n" +
            "\r\n" +
            "Attribute Value.VB_Description = \"stray\"\r\n" +
            PropertyPair);

        after.Where(r => r.DescribesMember).Select(r => r.Key).Should().Equal(before.Select(r => r.Key));
        after.Where(r => r.DescribesMember).Select(r => r.Region.Start)
            .Should().NotEqual(before.Select(r => r.Region.Start), "the premise: the runs did move");
    }

    [Fact]
    public void AProceduresKeyFollowsItsDeclarationAcrossARenameByRetyping()
    {
        // IDE probes C2 and C8: VB6 keeps a procedure's description across its name retyped on the declaration.
        // The run is still the procedure's, and its key is the new name, as a fold remembered for it should be.
        const string code = "Public Sub Halt()\r\nAttribute Halt.VB_Description = \"Stops\"\r\nEnd Sub\r\n";

        RunsOf(code.Replace("Sub Halt", "Sub Halts", StringComparison.Ordinal)).Should().ContainSingle()
            .Which.Key.Should().Be(new MemberRunKey("HALTS", 0));
    }

    // -- The header ------------------------------------------------------------------------------------

    [Fact]
    public void TheHeaderIsNeverAMemberRunAndAnOrphanDoesNotMoveIt()
    {
        // An attribute line straight after the header's own run is the header's, whatever it names; and an
        // orphan below the code leaves the header where it was.
        var absorbed = ClassHeader + "Attribute Total.VB_Description = \"x\"\r\nOption Explicit\r\n";
        ReadOnlyRegions.MemberRuns(absorbed, ClassHeader.Length).Should().BeEmpty();
        ReadOnlyRegions.HeaderEnd(absorbed, ClassHeader.Length).Should().Be(absorbed.IndexOf("Option", StringComparison.Ordinal));

        var orphaned = ClassHeader + "Option Explicit\r\nAttribute VB_Description = \"x\"\r\n";
        ReadOnlyRegions.HeaderEnd(orphaned, ClassHeader.Length).Should().Be(ClassHeader.Length);
        ReadOnlyRegions.HeaderEnd(orphaned, 0).Should().Be(ClassHeader.Length, "found from the text alone too");
        ReadOnlyRegions.Of(orphaned, 0).Should().Equal(new TextRegion(0, ClassHeader.Length));
    }

    [Fact]
    public void ARunOnTheFirstLineOfTheCodeHangsFromNothing()
    {
        // The header takes every line after it that reads as one of its own, so a run can start the code only as a
        // line the header's reading does not take, which names nothing. Above it is the header, which is no line
        // of code, so the run has no anchor.
        var text = ClassHeader + "Attribute(1) = 2\r\nOption Explicit\r\n";

        var run = ReadOnlyRegions.MemberRuns(text, ClassHeader.Length).Should().ContainSingle().Subject;

        run.Region.Should().Be(new TextRegion(ClassHeader.Length, text.IndexOf("Option", StringComparison.Ordinal)));
        run.DescribedStart.Should().Be(ClassHeader.Length);
        run.Anchor.Should().Be(ClassHeader.Length);
        run.Declared.Should().BeEmpty();
        run.Kind.Should().Be(MemberRunKind.Orphan);
    }

    [Fact]
    public void AFormsRunsAreFoundBelowItsHeaderWhichStraddlesThePrefix()
    {
        const string designer =
            "VERSION 5.00\r\n" +
            "Begin VB.Form Form1 \r\n" +
            "   Caption         =   \"Form1\"\r\n" +
            "End\r\n";
        var text = designer +
                   "Attribute VB_Name = \"Form1\"\r\n" +
                   "Option Explicit\r\n" +
                   "Public Sub Go()\r\n" +
                   "Attribute Go.VB_Description = \"Goes\"\r\n" +
                   "End Sub\r\n";

        var run = ReadOnlyRegions.MemberRuns(text, designer.Length).Should().ContainSingle().Subject;

        run.Kind.Should().Be(MemberRunKind.Procedure);
        run.Region.Start.Should().BeGreaterThan(ReadOnlyRegions.HeaderEnd(text, designer.Length));
    }
}
