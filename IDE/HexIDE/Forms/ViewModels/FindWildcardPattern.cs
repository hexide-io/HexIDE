using System;
using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.Forms.ViewModels;

/// <summary>VB6 IDE wildcard syntax measured in #5312, not the runtime Like operator.</summary>
internal static class FindWildcardPattern
{
    internal static string Translate(string pattern)
    {
        var result = new StringBuilder();
        for (var i = 0; i < pattern.Length; i++)
        {
            switch (pattern[i])
            {
                case '?':
                    result.Append(@"[^\r\n]");
                    break;
                case '#':
                    result.Append("[0-9]");
                    break;
                case '*':
                    result.Append(i == pattern.Length - 1 ? @"[^\r\n]*" : @"[^\r\n]*?");
                    break;
                case '[':
                    var end = pattern.IndexOf(']', i + 1);
                    if (end < 0)
                        throw new ArgumentException("Str.FindReplace.Msg.UnmatchedBrackets");
                    var start = i + 1;
                    var negated = start < end && pattern[start] == '!';
                    if (negated) start++;
                    if (start < end || negated)
                    {
                        result.Append(negated ? @"[^\r\n" : "[");
                        for (var j = start; j < end; j++)
                        {
                            result.Append(EscapeClassCharacter(pattern[j]));
                            if (j + 2 < end && pattern[j + 1] == '-')
                            {
                                if (pattern[j] > pattern[j + 2])
                                    throw new ArgumentException("Str.FindReplace.Msg.InvalidPattern");
                                result.Append('-').Append(EscapeClassCharacter(pattern[j + 2]));
                                j += 2;
                            }
                        }
                        result.Append(']');
                    }
                    i = end;
                    break;
                default:
                    result.Append(Regex.Escape(pattern[i].ToString()));
                    break;
            }
        }
        return result.ToString();
    }

    private static string EscapeClassCharacter(char value) => value switch
    {
        '-' or '[' or ']' or '^' or '\\' => "\\" + value,
        _ => Regex.Escape(value.ToString())
    };
}
