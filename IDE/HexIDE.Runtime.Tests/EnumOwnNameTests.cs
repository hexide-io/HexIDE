using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// #1637: vb6.exe SP6 /make, en-GB, 2026-10-24; native and p-code agree.
public class EnumOwnNameTests : BaseVBTestFixture
{
    private const string Declaration = "Enum E\n E\n F = 5\nEnd Enum\n";
    private Task Project(string code, (string Name, string Code)[]? modules = null,
        (string Name, string Code)[]? classes = null) =>
        new BasicInterpreter(new MockStdLib(debug), context, rootEnv, code, "Module1", modules ?? [], classes ?? []).Execute();

    private async Task Refused(Func<Task> run, string message)
    {
        (await run.Should().ThrowAsync<VBCompileErrorException>()).Which.Message.Should().Be("Compile error:\n\n" + message);
    }

    [Fact]
    public async Task Issue_body()
    {
        await Project("Enum E\nE\nEnd Enum\nSub Main()\nDebug.Print E.E\nEnd Sub\nMain\n");
        AssertDebugLog([new Vb6Value(0L)]);
    }

    [Fact]
    public async Task Qualified_members()
    {
        await Project(Declaration + "Debug.Print TypeName(E.E) & \":\" & E.E\nDebug.Print TypeName(E.F) & \":\" & E.F\n");
        AssertDebugLog([new Vb6Value("Long:0"), new Vb6Value("Long:5")]);
    }

    [Fact]
    public async Task Default_Long()
    {
        await Project(Declaration + "Dim x As E\nDebug.Print TypeName(x) & \":\" & x\n");
        AssertDebugLog([new Vb6Value("Long:0")]);
    }

    [Fact]
    public async Task Stores()
    {
        await Project(Declaration + "Dim x As E\nx = E.E\nDebug.Print TypeName(x) & \":\" & x\nx = E.F + 1\nDebug.Print TypeName(x) & \":\" & x\n");
        AssertDebugLog([new Vb6Value("Long:0"), new Vb6Value("Long:6")]);
    }

    [Fact]
    public async Task ByVal_parameter()
    {
        await Project(Declaration + "Debug.Print Twice(E.F)\nFunction Twice(ByVal v As E) As Long\n Twice = v * 2\nEnd Function\n");
        AssertDebugLog([new Vb6Value(10L)]);
    }

    [Fact]
    public Task Bare_expression() => Refused(() => Project(Declaration + "Debug.Print E\n"), "Expected variable or procedure, not enum type");

    [Fact]
    public Task Bare_store() => Refused(() => Project(Declaration + "Dim x As E\nx = E\n"), "Expected variable or procedure, not enum type");

    [Fact]
    public async Task Private_class_members_and_store()
    {
        await Project("Dim obj As New Class1\nobj.Probe\n", classes: [("Class1", "Private " + Declaration +
            "Public Sub Probe()\nDim x As E\nDebug.Print E.E\nDebug.Print E.F\nx = E.F\nDebug.Print x\nEnd Sub\n")]);
        AssertDebugLog([new Vb6Value(0L), new Vb6Value(5L), new Vb6Value(5L)]);
    }

    [Fact]
    public Task Private_class_bare() => Refused(() => Project("Dim obj As New Class1\nobj.Probe\n",
        classes: [("Class1", "Private " + Declaration + "Public Sub Probe()\nDebug.Print E\nEnd Sub\n")]),
        "Expected variable or procedure, not enum type");

    [Fact]
    public async Task Across_modules()
    {
        await Project("Debug.Print TypeName(E.E) & \":\" & E.E\nDebug.Print TypeName(Module2.E.E) & \":\" & Module2.E.E\nDim x As E\nx = E.F\nDebug.Print TypeName(x) & \":\" & x\n",
            [("Module2", "Public " + Declaration)]);
        AssertDebugLog([new Vb6Value("Long:0"), new Vb6Value("Long:0"), new Vb6Value("Long:5")]);
    }

    [Theory]
    [InlineData("E")]
    [InlineData("Module2.E")]
    public Task Across_modules_bare(string expression) => Refused(() => Project("Debug.Print " + expression + "\n",
        [("Module2", "Public " + Declaration)]), "Expected variable or procedure, not enum type");

    [Fact]
    public async Task Different_enum_name_control()
    {
        await Project("Enum G\nE = 7\nEnd Enum\nDebug.Print TypeName(E) & \":\" & E\nDebug.Print TypeName(G.E) & \":\" & G.E\n");
        AssertDebugLog([new Vb6Value("Long:7"), new Vb6Value("Long:7")]);
    }

    [Fact]
    public Task Qualified_store()
    {
        // VB6 refuses this even in an uncalled Sub. The CST walk sees executed statements only:
        // call the Sub to pin the boundary's recorded second-order consequence.
        return Refused(() => Project(Declaration + "Bad\nSub Bad()\nE.E = 1\nEnd Sub\n"),
            "Assignment to constant not permitted");
    }

    [Fact]
    public Task Ambiguous_across_modules() => Refused(() => Project("Debug.Print E\n",
        [("Module2", "Public Enum E\nE = 3\nEnd Enum\n"), ("Module3", "Public Enum G\nE = 4\nEnd Enum\n")]),
        "Ambiguous name detected: E");
}
