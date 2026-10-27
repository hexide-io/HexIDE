using System.IO;
using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

public partial class SyntaxChecker : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} in line {line}:{charPositionInLine}\nOffending symbol: {offendingSymbol.Text}") { Line = line };
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} in line {line}:{charPositionInLine}") { Line = line };
    }

    public void Run(string code)
    {
        var inputStream = new AntlrInputStream(new StringReader(code));
        var lexer = new VB6Lexer(inputStream);
        lexer.RemoveErrorListeners();
        lexer.AddErrorListener(this);
        var parser = new VB6Parser(NextCounterList.Tokens(lexer));
        parser.RemoveErrorListeners();
        parser.AddErrorListener(this);
        parser.AddParseListener(new ParseDepthGuard());   // reject pathological nesting as a compile error, not a crash

        var tree = parser.startRule();
    }
}