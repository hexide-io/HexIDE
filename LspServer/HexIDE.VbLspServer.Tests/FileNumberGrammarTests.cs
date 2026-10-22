using Antlr4.Runtime;
using HexIDE.VbLspServer;

namespace HexIDE.VbLspServer.Tests;

// #1146: compile results are recorded in the oracle's "File-number hashes and date literals".
// Mirrored on both parsers so editor diagnostics and execution agree.
public class FileNumberGrammarTests
{
    [Theory]
    [InlineData("Line Input #1, s: Print #2, s", 2, 0)]
    [InlineData("Print #1, \"a\";: Close #1", 2, 0)]
    [InlineData("Close #1, #2, #3", 3, 0)]
    [InlineData("Print #1, #1/2/2003#: Close #1", 2, 1)]
    [InlineData("Print #1, 2, #1/2/2003#", 1, 1)]
    [InlineData("Print #f, 2#: Close #f", 2, 0)]
    [InlineData("Open \"x\" For Output As #1: Print #1, \"a\": Close #1", 3, 0)]
    [InlineData("Debug.Print Input$(1, #1): Close #1", 2, 0)]
    [InlineData("Print #1, _\r\n2#: Close #1", 2, 0)]
    [InlineData("If True Then Print #1, \"a\": Print #2, \"b\"", 2, 0)]
    [InlineData("Print #1, \"a\": ' #not-a-date#", 1, 0)]
    public void File_statements_keep_their_file_numbers(string body, int files, int dates)
    {
        var source = "Sub Main()\n" + body + "\nEnd Sub\n";
        VbDiagnosticsProvider.GetDiagnosticsAndTree(source).Diagnostics.Should().BeEmpty();
        var tokens = new CommonTokenStream(new VisualBasic6Lexer(new AntlrInputStream(source)));
        tokens.Fill();
        tokens.GetTokens().Count(t => t.Type == VisualBasic6Lexer.FILENUMBER).Should().Be(files);
        tokens.GetTokens().Count(t => t.Type == VisualBasic6Lexer.DATELITERAL).Should().Be(dates);
    }

    [Theory]
    [InlineData("#1/2/2003#")]
    [InlineData("#10:30:00 AM#")]
    [InlineData("#January 2, 2003#")]
    [InlineData("#Tue, Jan 2, 2001#")]
    [InlineData("#10:30AM#")]
    [InlineData("#1/2/2003 10:30 PM#")]
    [InlineData("#10 PM#")]
    [InlineData("#1, 2#")]
    [InlineData("#May, 2#")]
    [InlineData("#2/29#")]
    [InlineData("#2 Sept 2003#")]
    [InlineData("#Tues, Jan 2, 2001#")]
    [InlineData("#Thurs, Jan 4, 2001#")]
    [InlineData("#Jan. 2, 2003#")]
    [InlineData("#2 Jan. 2003#")]
    [InlineData("#2 Jan.#")]
    [InlineData("#10:30 A#")]
    [InlineData("#10:30 P#")]
    [InlineData("#10.30#")]
    [InlineData("#1-2-2003#")]
    [InlineData("# 1/2/2003 #")]
    [InlineData("#Thur, Jan 4, 2001#")]
    [InlineData("#Weds, Jan 3, 2001#")]
    [InlineData("#2 Janu 2003#")]
    [InlineData("#2 Februar 2003#")]
    [InlineData("#2 Septem 2003#")]
    [InlineData("#2 Octob 2003#")]
    [InlineData("#2 Novem 2003#")]
    [InlineData("#2 Decem 2003#")]
    public void Genuine_dates_remain_whole_tokens(string literal)
    {
        var tokens = new CommonTokenStream(new VisualBasic6Lexer(new AntlrInputStream("Debug.Print " + literal)));
        tokens.Fill();
        tokens.GetTokens().Where(t => t.Type == VisualBasic6Lexer.DATELITERAL).Select(t => t.Text)
            .Should().Equal(literal);
    }

    [Fact]
    public void A_date_shaped_pair_stays_a_date_even_after_Print()
    {
        // VB6 rejects this as the form's Print method in a standard module. Reinterpreting it as
        // file 1 followed by a Double would silently change the program.
        var tokens = new CommonTokenStream(new VisualBasic6Lexer(new AntlrInputStream("Print #1, 2#")));
        tokens.Fill();
        tokens.GetTokens().Where(t => t.Type == VisualBasic6Lexer.DATELITERAL).Select(t => t.Text)
            .Should().Equal("#1, 2#");
        tokens.GetTokens().Should().NotContain(t => t.Type == VisualBasic6Lexer.FILENUMBER);
    }
}
