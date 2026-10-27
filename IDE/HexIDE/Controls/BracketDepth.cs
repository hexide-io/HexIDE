using System.Collections.Generic;
using Antlr4.Runtime;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Controls;

internal static class BracketDepth
{
    // A negative depth marks an unmatched parenthesis.
    internal readonly record struct Mark(int Offset, int Depth);

    internal static IReadOnlyList<Mark> Scan(string text)
    {
        var lexer = new VB6Lexer(new AntlrInputStream(text));
        lexer.RemoveErrorListeners();
        var marks = new List<Mark>();
        var open = new Stack<int>();
        for (var token = lexer.NextToken(); token.Type != TokenConstants.EOF; token = lexer.NextToken())
        {
            if (token.Type == VB6Lexer.LPAREN)
            {
                open.Push(marks.Count);
                marks.Add(new Mark(token.StartIndex, open.Count - 1));
            }
            else if (token.Type == VB6Lexer.RPAREN)
            {
                var depth = open.Count == 0 ? -1 : marks[open.Pop()].Depth;
                marks.Add(new Mark(token.StartIndex, depth));
            }
        }
        foreach (var index in open)
            marks[index] = marks[index] with { Depth = -1 };
        return marks;
    }
}
