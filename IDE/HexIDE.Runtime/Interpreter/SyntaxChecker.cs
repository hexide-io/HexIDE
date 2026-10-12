using System.IO;
using Antlr4.Runtime;

namespace HexIDE.Runtime.Interpreter;

public partial class SyntaxChecker : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} in {Where}line {line}:{charPositionInLine}\nOffending symbol: {offendingSymbol.Text}") { Line = line };
    }

    public void SyntaxError(TextWriter output, IRecognizer recognizer, int offendingSymbol, int line, int charPositionInLine,
        string msg, RecognitionException e)
    {
        throw new VBCompileErrorException($"{msg} in {Where}line {line}:{charPositionInLine}") { Line = line };
    }

    private string? moduleName;

    // The module a syntax error is in, named in its message so the user fixes the right file.
    private string Where => moduleName is null ? "" : moduleName + ", ";

    public void Run(string code, string? moduleName = null)
    {
        this.moduleName = moduleName;
        var inputStream = new AntlrInputStream(new StringReader(code));
        var lexer = new VB6Lexer(inputStream);
        var commonTokenStream = new CommonTokenStream(lexer);
        var parser = new VB6Parser(commonTokenStream);

        lexer.RemoveErrorListeners();
        parser.RemoveErrorListeners();

        lexer.AddErrorListener(this);
        parser.AddErrorListener(this);
        parser.AddParseListener(new ParseDepthGuard());   // reject pathological nesting as a compile error, not a crash

        var tree = parser.startRule();
    }
}