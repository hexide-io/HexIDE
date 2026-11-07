using AvaloniaEdit.Document;
using HexIDE.Controls;
using HexIDE.Lsp.Messages;

namespace HexIDE.Tests.Controls;

public class SemanticTokensTests
{
    [AvaloniaTheory]
    [InlineData("variable")]
    [InlineData("macro")]
    [InlineData("decorator")]
    public void RelativeTokensUseTheirServersLegendAndResetColumnsOnNewLines(string identifierType)
    {
        var document = new TextDocument("Private x\r\n  Long y");
        var tokens = SemanticTokensColorizer.Decode(document,
            new SemanticTokenResult([0, 0, 7, 1, 0, 0, 8, 1, 0, 1, 1, 2, 4, 2, 2], [identifierType, "keyword", "class"]));
        tokens.Should().Equal(new SemanticTokensColorizer.Token(0, 7, "Keyword"),
            new SemanticTokensColorizer.Token(8, 9, "Identifier"), new SemanticTokensColorizer.Token(13, 17, "Type"));
    }

    [AvaloniaTheory]
    [InlineData(new int[] { 0, 0, 99, 0, 0 })]
    [InlineData(new int[] { 0, 0, 1, 9, 0 })]
    [InlineData(new int[] { 0, 0, 1 })]
    [InlineData(new int[] { 0, 0, 2, 0, 0, 0, 1, 2, 0, 0 })]
    public void InvalidTokensLeaveOnlyLexicalColours(int[] data)
    {
        SemanticTokensColorizer.Decode(new TextDocument("abc"), new(data, ["keyword"]))
            .Should().BeEmpty();
    }
}
