using Antlr4.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.Tests;

// DFA caches are shared by parser instances; profile without concurrent corpus parses.
[Collection(typeof(ParserPredictionCollection))]
public class BlockParsingCostTests(ITestOutputHelper output) : BaseVBTestFixture
{
    [Theory]
    [InlineData(100)]
    [InlineData(400)]
    public void NestedBlocks_PredictionCostIsBounded(int depth)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, "Sub Main()\nEnd Sub\n");
        var parser = new VB6Parser(NextCounterList.Tokens(new VB6Lexer(new AntlrInputStream(SizedParsingTests.Source(true, depth)))));
        parser.Interpreter.ClearDFA();
        parser.Profile = true;
        vb.ParseWithFallback(parser, p => p.startRule());
        parser.NumberOfSyntaxErrors.Should().Be(0);
        var decisions = parser.ParseInfo.getDecisionInfo();
        for (var i = 0; i < decisions.Length; i++)
        {
            var d = decisions[i];
            if (d.invocations == 0) continue;
            output.WriteLine($"{i} {parser.RuleNames[parser.Atn.decisionToState[i].ruleIndex]}: calls={d.invocations}, time={d.timeInPrediction}, SLL={d.SLL_TotalLook}/{d.SLL_MaxLook}, LL={d.LL_TotalLook}/{d.LL_MaxLook}, fallback={d.LL_Fallback}, DFA={parser.ParseInfo.getDFASize(i)}");
        }
        decisions.Max(d => d.SLL_MaxLook).Should().BeLessThan(16);
        decisions.Sum(d => d.SLL_TotalLook).Should().BeLessThan(200L * depth);
        parser.ParseInfo.getDFASize().Should().BeLessThan(1000);
    }

    [Theory]
    [InlineData(100)]
    [InlineData(400)]
    public void PrintExpression_PredictionCostIsBounded(int terms)
    {
        var vb = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, "Sub Main()\nEnd Sub\n");
        var source = "Sub Main()\nDebug.Print " + string.Join(" + ", Enumerable.Repeat("1", terms)) + "\nEnd Sub\n";
        var parser = new VB6Parser(NextCounterList.Tokens(new VB6Lexer(new AntlrInputStream(source))));
        parser.Interpreter.ClearDFA();
        parser.Profile = true;
        vb.ParseWithFallback(parser, p => p.startRule());
        parser.NumberOfSyntaxErrors.Should().Be(0);
        var decisions = parser.ParseInfo.getDecisionInfo();
        decisions.Sum(d => d.SLL_TotalLook).Should().BeLessThan(200L * terms);
        decisions.Sum(d => d.LL_TotalLook).Should().BeLessThan(20L * terms);
    }
}
