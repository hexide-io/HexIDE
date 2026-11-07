namespace HexIDE.Runtime.Interpreter;

/// <summary>Keeps a parser's syntax-error message to the line the error is on (#3210).</summary>
/// <remarks>
/// ANTLR words a failed prediction as "no viable alternative at input '…'", quoting every token from where the
/// decision began, with line breaks written as the two characters <c>\n</c> and <c>\r</c>. When that decision begins
/// at the top of the module, the quote is the module's whole text up to the error: 6,638 characters for one corpus
/// case. The line and column already say where the error is, so only the quote's last line is kept.
/// </remarks>
internal static class SyntaxErrorText
{
    private const string NoViable = "no viable alternative at input '";

    internal static string Of(string message)
    {
        if (!message.StartsWith(NoViable, StringComparison.Ordinal) || !message.EndsWith('\''))
            return message;
        var input = message[NoViable.Length..^1];
        while (input.EndsWith(@"\n", StringComparison.Ordinal) || input.EndsWith(@"\r", StringComparison.Ordinal))
            input = input[..^2];
        var lastBreak = Math.Max(input.LastIndexOf(@"\n", StringComparison.Ordinal),
            input.LastIndexOf(@"\r", StringComparison.Ordinal));
        if (lastBreak < 0)
            return message;
        return NoViable + input[(lastBreak + 2)..].TrimStart() + "'";
    }
}
