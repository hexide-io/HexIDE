// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Rewrites raw ANTLR4 syntax-error messages into friendlier VB6-style text (grammar-agnostic).

using System.Text.RegularExpressions;

namespace HexIDE.VbLspServer;

/// <summary>
/// Rewrites raw ANTLR4 syntax-error messages into friendlier VB6-style text.
/// </summary>
internal static partial class VbErrorMessages
{
    public static string Prettify(string antlrMessage, string? tokenText)
    {
        // "missing X at Y" — token expected but not found
        var m = MissingAt().Match(antlrMessage);
        if (m.Success)
        {
            var expected = FriendlyToken(m.Groups["expected"].Value);
            return $"Expected {expected}";
        }

        // "mismatched input X expecting {Y, Z, ...}"
        m = MismatchedInput().Match(antlrMessage);
        if (m.Success)
            return $"Syntax error: unexpected '{EscapeToken(tokenText)}'";

        // "extraneous input X expecting Y"
        m = ExtraneousInput().Match(antlrMessage);
        if (m.Success)
            return $"Syntax error: unexpected '{EscapeToken(tokenText)}'";

        // "no viable alternative at input X"
        m = NoViableAlt().Match(antlrMessage);
        if (m.Success)
            return $"Syntax error near '{EscapeToken(tokenText)}'";

        // "token recognition error at: X" (lexer)
        m = TokenRecognition().Match(antlrMessage);
        if (m.Success)
            return $"Invalid character: '{m.Groups["ch"].Value}'";

        // Fall back to original
        return antlrMessage;
    }

    private static string FriendlyToken(string antlrToken)
    {
        // Strip angle brackets, single quotes, keyword literals
        var t = antlrToken.Trim('\'', '"', '<', '>');
        return t switch
        {
            "EOS"         => "end of statement",
            "NEWLINE"     => "end of line",
            "END_OF_FILE" => "end of file",
            "EOF"         => "end of file",
            "LPAREN"      => "'('",
            "RPAREN"      => "')'",
            "COMMA"       => "','",
            "DOT"         => "'.'",
            "EQ"          => "'='",
            "COLON"       => "':'",
            "UNDERSCORE"  => "'_'",
            _             => $"'{t}'"
        };
    }

    private static string EscapeToken(string? token)
    {
        if (string.IsNullOrEmpty(token) || token == "<EOF>") return "end of file";
        return token.Trim();
    }

    [GeneratedRegex(@"missing (?<expected>\S+) at")]
    private static partial Regex MissingAt();

    [GeneratedRegex(@"mismatched input")]
    private static partial Regex MismatchedInput();

    [GeneratedRegex(@"extraneous input")]
    private static partial Regex ExtraneousInput();

    [GeneratedRegex(@"no viable alternative at input")]
    private static partial Regex NoViableAlt();

    [GeneratedRegex(@"token recognition error at: '(?<ch>[^']+)'")]
    private static partial Regex TokenRecognition();
}
