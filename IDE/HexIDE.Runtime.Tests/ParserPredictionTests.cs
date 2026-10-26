using System.Diagnostics;
using Antlr4.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// Wall-clock assertions must not compete with the corpus lanes and their parsers.
[CollectionDefinition(DisableParallelization = true)]
public sealed class ParserPredictionCollection;

[Collection(typeof(ParserPredictionCollection))]
public class ParserPredictionTests : BaseVBTestFixture
{
    [Fact]
    public void LongConcatenations_ModuleStartupBeatsLlParsing()
    {
        // #2038's measured workload: forty lines with twenty concatenation terms each.
        var line = "Debug.Print \"a\"" + string.Concat(Enumerable.Range(0, 10)
            .Select(i => $" & Hour(d) & \":{i}\""));
        var code = "Dim d As Date\n" + string.Join("\n", Enumerable.Repeat(line, 40)) + "\n";
        void ParseLl()
        {
            var parser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(code))));
            parser.startRule();
            parser.NumberOfSyntaxErrors.Should().Be(0);
        }

        BasicInterpreter BuildModule() => new(new MockStdLib(debug, written, dialogs), context,
            new ExecutionEnvironment(), code);

        // Warm both paths; compare on this host rather than pinning a machine-dependent time limit.
        ParseLl();
        BuildModule();
        var watch = Stopwatch.StartNew();
        ParseLl();
        var llTime = watch.Elapsed;
        watch.Restart();
        var vb = BuildModule();
        watch.Elapsed.Should().BeLessThan(llTime * 0.9);
        vb.PrePass.topLevelBlocks.Should().NotBeEmpty();
    }

    [Fact]
    public async Task CallAndArrayAmbiguity_StillRuns()
    {
        await Run("Dim a(2) As Integer\na(1) = 7\nDebug.Print a(1)\nDebug.Print Hour(#12:00:00#)\n");
        debug.Count.Should().Be(2);
    }
    [Theory]
    [InlineData("Foo(1)")]
    [InlineData("obj.Items(5)")]
    [InlineData("Foo(a:=1, b:=2)")]
    public void ImmediateEntries_PreserveLlTrees(string expression)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug, written, dialogs), context, rootEnv, "");
        var expressionParser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(expression))));
        vb.ParseValueStmt(expression).ToStringTree(expressionParser).Should().Be(expressionParser.valueStmt().ToStringTree(expressionParser));
        var statement = "x = " + expression;
        var statementParser = new VB6Parser(new CommonTokenStream(new VB6Lexer(new AntlrInputStream(statement))));
        vb.TryParseStatement(statement)!.ToStringTree(statementParser).Should().Be(statementParser.blockStmt().ToStringTree(statementParser));
    }

    [Fact]
    public void ImmediateEntries_PreserveErrorPolicies()
    {
        var vb = new BasicInterpreter(new MockStdLib(debug, written, dialogs), context, rootEnv, "");
        var parse = () => vb.ParseValueStmt("1 +");
        parse.Should().Throw<VBCompileErrorException>();
        vb.TryParseStatement("x = 1 +").Should().BeNull();
        var trailing = () => vb.ParseValueStmt("1 2");
        trailing.Should().Throw<VBCompileErrorException>();
        vb.TryParseStatement("x = 1 Then").Should().BeNull();
    }
}
