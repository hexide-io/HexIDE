using System.Collections.Generic;
using Antlr4.Runtime;
using HexIDE.Language;

namespace HexIDE.Runtime.Interpreter;

/// <summary>Finds a module's parentheses with the interpreter's lexer, so a string or a comment hides one as it does from a program.</summary>
public sealed class InterpreterBrackets : IBracketReader
{
    public IReadOnlyList<BracketMark> Brackets(string source)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(source));
        lexer.RemoveErrorListeners();
        var marks = new List<BracketMark>();
        var open = new Stack<int>();
        for (var token = lexer.NextToken(); token.Type != TokenConstants.EOF; token = lexer.NextToken())
        {
            if (token.Type == VB6Lexer.LPAREN)
            {
                open.Push(marks.Count);
                marks.Add(new BracketMark(token.StartIndex, open.Count - 1));
            }
            else if (token.Type == VB6Lexer.RPAREN)
            {
                var depth = open.Count == 0 ? -1 : marks[open.Pop()].Depth;
                marks.Add(new BracketMark(token.StartIndex, depth));
            }
        }
        foreach (var index in open)
            marks[index] = marks[index] with { Depth = -1 };
        return marks;
    }
}
