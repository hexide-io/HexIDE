// SPDX-License-Identifier: MIT
// #3213: run before and after a grammar edit, then diff the two output files.
using Antlr4.Runtime;
using HexIDE.Runtime.Interpreter;


var root = Directory.GetCurrentDirectory();
using var output = File.CreateText(args[0]);
foreach (var directory in new[] { "corpus", "TestFiles" })
foreach (var file in Directory.EnumerateFiles(Path.Combine(root, directory), "*", SearchOption.AllDirectories)
    .Where(f => new[] { ".bas", ".cls", ".frm", ".ctl" }.Contains(Path.GetExtension(f).ToLowerInvariant())).Order())
{
    var source = File.ReadAllText(file);
    var errors = new Errors();
    var lexer = new VB6Lexer(new AntlrInputStream(source));
    lexer.RemoveErrorListeners();
    lexer.AddErrorListener(errors);
    var parser = new VB6Parser(new CommonTokenStream(lexer));
    parser.RemoveErrorListeners();
    parser.AddErrorListener(errors);
    parser.startRule();
    output.WriteLine(Path.GetRelativePath(root, file));
    foreach (var error in errors.Messages) output.WriteLine("runtime " + error);
    errors.Messages.Clear();
    var lspLexer = new VisualBasic6Lexer(new AntlrInputStream(source));
    lspLexer.RemoveErrorListeners();
    lspLexer.AddErrorListener(errors);
    var lspParser = new VisualBasic6Parser(new CommonTokenStream(lspLexer));
    lspParser.RemoveErrorListeners();
    lspParser.AddErrorListener(errors);
    lspParser.startRule();
    foreach (var error in errors.Messages) output.WriteLine("lsp " + error);
}

sealed class Errors : IAntlrErrorListener<IToken>, IAntlrErrorListener<int>
{
    public List<string> Messages { get; } = [];
    public void SyntaxError(TextWriter output, IRecognizer recognizer, IToken symbol, int line, int column, string message, RecognitionException e)
        => Messages.Add($"{line}:{column} {message}");
    public void SyntaxError(TextWriter output, IRecognizer recognizer, int symbol, int line, int column, string message, RecognitionException e)
        => Messages.Add($"{line}:{column} {message}");
}
