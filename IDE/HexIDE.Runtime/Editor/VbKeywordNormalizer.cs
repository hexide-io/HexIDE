// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.Runtime.

namespace HexIDE.Runtime.Editor;

/// <summary>
/// Normalizes VB6 keyword casing to PascalCase on a per-line basis.
/// Skips content inside string literals and comments.
/// </summary>
/// <remarks>
/// Clean-room keyword list derived from the VB6 language specification.
/// This class is in the MIT-licensed Runtime project.
/// </remarks>
public static class VbKeywordNormalizer
{
    private static readonly Dictionary<string, string> Keywords = BuildKeywordMap();

    private static Dictionary<string, string> BuildKeywordMap()
    {
        string[] keywords =
        [
            // Declaration
            "Dim", "ReDim", "Preserve", "Static", "Public", "Private", "Friend",
            "Global", "Const", "Enum", "Type", "Implements", "As", "New",
            "ByVal", "ByRef", "Optional", "ParamArray", "Declare", "Lib", "Alias",
            "WithEvents", "Event", "RaiseEvent",

            // Procedures
            "Sub", "Function", "Property", "Get", "Let", "Set",
            "Exit", "End",

            // Control flow
            "If", "Then", "Else", "ElseIf",
            "Select", "Case",
            "For", "To", "Step", "Next", "Each", "In",
            "Do", "Loop", "While", "Until", "Wend",
            "With", "GoTo", "GoSub", "Return", "Resume", "On", "Error",

            // Operators / literals
            "Not", "And", "Or", "Xor", "Mod", "Like", "Is",
            "True", "False", "Empty", "Null", "Nothing", "Me",

            // Types
            "Boolean", "Byte", "Integer", "Long", "Single", "Double",
            "Currency", "Date", "String", "Object", "Variant", "Any",

            // Option
            "Option", "Explicit", "Base", "Compare",

            // Statements
            "Call", "Print", "Debug",
            "Open", "Close", "Input", "Output", "Append", "Binary", "Random",
            "Write", "Read", "Seek", "Lock", "Unlock", "Put",
            "Name", "Kill", "MkDir", "RmDir", "ChDir", "ChDrive",
            "Erase", "LSet", "RSet",
            "Stop", "Assert",

            // Error handling
            "Err",

            // Common built-in functions
            "MsgBox", "InputBox",
            "Len", "Left", "Right", "Mid", "Trim", "LTrim", "RTrim",
            "UCase", "LCase", "InStr", "InStrRev", "Replace", "Split", "Join",
            "CStr", "CInt", "CLng", "CDbl", "CBool", "CDate", "CByte",
            "CSng", "CCur", "CVar", "CVErr",
            "Int", "Fix", "Abs", "Sqr", "Rnd", "Sgn", "Log", "Exp",
            "Now", "Time", "Timer", "Array", "UBound", "LBound",
            "TypeName", "VarType", "Chr", "Asc", "Format",
            "IsNull", "IsEmpty", "IsObject", "IsNumeric", "IsMissing",
            "IsArray", "IsDate", "IsError",
            "Hex", "Oct", "Val", "Str", "Space", "String",
            "RGB", "QBColor",
            "DoEvents", "Shell", "Environ", "Command",
            "FreeFile", "EOF", "LOF", "Loc", "Seek",
            "Dir", "CurDir", "FileLen", "FileDateTime", "GetAttr", "SetAttr",
        ];

        var map = new Dictionary<string, string>(keywords.Length, StringComparer.OrdinalIgnoreCase);
        foreach (var kw in keywords)
            map[kw] = kw;

        // Multi-word compound keywords — handled specially below
        return map;
    }

    /// <summary>
    /// Normalizes keyword casing in a single physical line of VB6 source code.
    /// Returns the corrected line, or <c>null</c> if no changes were needed.
    /// </summary>
    public static string? NormalizeLine(string line)
    {
        if (string.IsNullOrEmpty(line))
            return null;

        var chars = line.ToCharArray();
        var changed = false;
        var i = 0;

        while (i < chars.Length)
        {
            var ch = chars[i];

            // Skip string literals
            if (ch == '"')
            {
                i++;
                while (i < chars.Length && chars[i] != '"')
                    i++;
                if (i < chars.Length) i++; // skip closing quote
                continue;
            }

            // Comment — rest of line is untouched
            if (ch == '\'')
                break;

            // REM comment (must be at start of line or after whitespace)
            if ((ch == 'R' || ch == 'r') && i + 2 < chars.Length)
            {
                if ((i == 0 || !IsIdentChar(chars[i - 1])) &&
                    (chars[i + 1] == 'e' || chars[i + 1] == 'E') &&
                    (chars[i + 2] == 'm' || chars[i + 2] == 'M') &&
                    (i + 3 >= chars.Length || !IsIdentChar(chars[i + 3])))
                {
                    // "Rem" keyword — normalize it but then stop (rest is comment)
                    if (TryNormalizeWord(chars, i, 3, out changed))
                        changed = true;
                    break;
                }
            }

            // Identifier/keyword
            if (IsIdentStartChar(ch))
            {
                var start = i;
                i++;
                while (i < chars.Length && IsIdentChar(chars[i]))
                    i++;
                var len = i - start;

                if (TryNormalizeWord(chars, start, len, out var wordChanged))
                    changed = changed || wordChanged;
                continue;
            }

            // Preprocessor directive — #If, #End, #Else, #ElseIf, #Const
            if (ch == '#')
            {
                i++;
                // skip whitespace after #
                while (i < chars.Length && chars[i] == ' ')
                    i++;
                if (i < chars.Length && IsIdentStartChar(chars[i]))
                {
                    var start = i;
                    i++;
                    while (i < chars.Length && IsIdentChar(chars[i]))
                        i++;
                    var len = i - start;
                    if (TryNormalizeWord(chars, start, len, out var wordChanged))
                        changed = changed || wordChanged;
                }
                continue;
            }

            i++;
        }

        return changed ? new string(chars) : null;
    }

    private static bool TryNormalizeWord(char[] chars, int start, int length, out bool changed)
    {
        changed = false;
        var word = new string(chars, start, length);
        if (!Keywords.TryGetValue(word, out var canonical))
            return false;

        // Check if already correct
        if (word == canonical)
            return false;

        // Replace in-place
        for (var j = 0; j < length; j++)
            chars[start + j] = canonical[j];

        changed = true;
        return true;
    }

    private static bool IsIdentStartChar(char ch) =>
        char.IsLetter(ch) || ch == '_';

    private static bool IsIdentChar(char ch) =>
        char.IsLetterOrDigit(ch) || ch == '_';
}
