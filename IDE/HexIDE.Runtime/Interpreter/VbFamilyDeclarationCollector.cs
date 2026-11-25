using System.Collections.Generic;
using System.Linq;
using Antlr4.Runtime;
using HexIDE.Language;
using HexIDE.Lsp.Messages;
using LspRange = HexIDE.Lsp.Messages.Range;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Collects declaration headers only; no names are bound (#4401). The Basic provider registers it for the Object Browser and
/// the Watch dialog (#5403), which ask it when no language server answers.
/// </summary>
internal sealed class VbFamilyDeclarationCollector : IDeclarationReader
{
    public IReadOnlyList<DocumentSymbol> Declarations(string source) => Collect(source);

    internal static DocumentSymbol[] Collect(string text)
    {
        // Use the existing lexer for comments, strings and continuations without parsing bodies or
        // choosing a conditional-compilation branch. Both branches' declarations remain visible.
        var lexer = new VB6Lexer(new AntlrInputStream(text));
        lexer.RemoveErrorListeners();
        var symbols = new List<DocumentSymbol>();
        var statement = new List<IToken>();
        var closingToken = 0;
        var pending = -1;
        for (var token = lexer.NextToken(); ; token = lexer.NextToken())
        {
            if (token.Type is TokenConstants.EOF or VB6Lexer.NEWLINE or VB6Lexer.COLON)
            {
                CollectHeader();
                statement.Clear();
                if (token.Type == TokenConstants.EOF) break;
            }
            else if (token.Channel == TokenConstants.DefaultChannel && token.Type != VB6Lexer.WS)
                statement.Add(token);
        }
        return symbols.ToArray();

        void CollectHeader()
        {
            if (statement.Count == 0) return;
            var first = statement[0];
            if (pending >= 0 && first.Type == closingToken)
            {
                var symbol = symbols[pending];
                symbols[pending] = symbol with { Range = new LspRange(symbol.Range.Start,
                    new Position(first.Line - 1, first.Column + first.Text.Length)) };
                pending = -1;
                return;
            }
            if (pending >= 0) return;
            var index = 0;
            while (index < statement.Count && statement[index].Type is VB6Lexer.PUBLIC or VB6Lexer.PRIVATE
                       or VB6Lexer.FRIEND or VB6Lexer.GLOBAL or VB6Lexer.STATIC)
                index++;
            if (index + 1 >= statement.Count) return;
            var header = statement[index];
            var (kind, suffix, end) = header.Type switch
            {
                VB6Lexer.SUB => (SymbolKind.Method, "", VB6Lexer.END_SUB),
                VB6Lexer.FUNCTION => (SymbolKind.Function, "", VB6Lexer.END_FUNCTION),
                VB6Lexer.PROPERTY_GET => (SymbolKind.Property, " (Get)", VB6Lexer.END_PROPERTY),
                VB6Lexer.PROPERTY_LET => (SymbolKind.Property, " (Let)", VB6Lexer.END_PROPERTY),
                VB6Lexer.PROPERTY_SET => (SymbolKind.Property, " (Set)", VB6Lexer.END_PROPERTY),
                VB6Lexer.ENUM => (SymbolKind.Enum, "", VB6Lexer.END_ENUM),
                VB6Lexer.TYPE => (SymbolKind.Struct, "", VB6Lexer.END_TYPE),
                _ => (default(SymbolKind), "", 0)
            };
            if (end == 0) return;
            var name = statement[index + 1];
            var nameEnd = index + 1;
            if (name.Type == VB6Lexer.L_SQUARE_BRACKET)
            {
                nameEnd = statement.FindIndex(index + 2, t => t.Type == VB6Lexer.R_SQUARE_BRACKET);
                if (nameEnd <= index + 2) return;
            }
            var declaredName = string.Concat(statement.Skip(index + 1).Take(nameEnd - index).Select(t => t.Text));
            var selection = new LspRange(new Position(name.Line - 1, name.Column),
                new Position(statement[nameEnd].Line - 1, statement[nameEnd].Column + statement[nameEnd].Text.Length));
            var last = statement[^1];
            symbols.Add(new DocumentSymbol(declaredName + suffix, kind,
                new LspRange(new Position(first.Line - 1, first.Column),
                    new Position(last.Line - 1, last.Column + last.Text.Length)), selection));
            pending = symbols.Count - 1;
            closingToken = end;
        }
    }
}
