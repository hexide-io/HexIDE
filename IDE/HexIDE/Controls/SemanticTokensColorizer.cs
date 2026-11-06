using System;
using System.Collections.Generic;
using Avalonia.Media;
using AvaloniaEdit.Document;
using AvaloniaEdit.Rendering;
using HexIDE.Lsp.Messages;
using HexIDE.Themes;

namespace HexIDE.Controls;

internal sealed class SemanticTokensColorizer : DocumentColorizingTransformer
{
    internal record Token(int Start, int End, string Color);
    internal IReadOnlyList<Token> Tokens { get; set; } = [];

    internal static IReadOnlyList<Token> Decode(TextDocument document, SemanticTokenResult? result)
    {
        if (result is null || result.Data.Length % 5 != 0) return [];
        var tokens = new List<Token>();
        long line = 0, column = 0;
        var end = 0;
        for (var i = 0; i < result.Data.Length; i += 5)
        {
            var data = result.Data;
            if (data[i] < 0 || data[i + 1] < 0 || data[i + 2] <= 0
                || data[i + 3] < 0 || data[i + 3] >= result.TokenTypes.Length || data[i + 4] < 0) return [];
            column = data[i] == 0 ? column + data[i + 1] : data[i + 1];
            line += data[i];
            if (line >= document.LineCount) return [];
            var source = document.GetLineByNumber((int)line + 1);
            if (column + data[i + 2] > source.Length) return [];
            var start = source.Offset + (int)column;
            if (start < end) return [];
            end = start + data[i + 2];
            var color = result.TokenTypes[data[i + 3]] switch
            {
                "keyword" or "modifier" => "Keyword",
                "comment" => "Comment",
                "string" or "regexp" => "String",
                "number" => "Number",
                "operator" => "Operator",
                "type" or "class" or "enum" or "interface" or "struct" or "typeParameter" => "Type",
                "variable" or "parameter" or "property" or "enumMember" or "event" or "function" or "method" or "namespace"
                    or "macro" or "decorator" => "Identifier",
                _ => null
            };
            if (color is not null) tokens.Add(new Token(start, end, color));
        }
        return tokens;
    }

    protected override void ColorizeLine(DocumentLine line)
    {
        foreach (var token in Tokens)
        {
            if (token.Start >= line.EndOffset) break;
            if (token.End <= line.Offset) continue;
            var color = SyntaxHighlightingTheme.Definition.GetNamedColor(token.Color).Foreground?.GetColor(null!);
            if (color is { } value)
                ChangeLinePart(Math.Max(line.Offset, token.Start), Math.Min(line.EndOffset, token.End),
                    element => element.TextRunProperties.SetForegroundBrush(new SolidColorBrush(value)));
        }
    }
}
