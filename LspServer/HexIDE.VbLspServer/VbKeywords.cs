// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors
// This file is part of HexIDE.VbLspServer, which uses the
// Rubberduck VBA ANTLR4 grammar (GPLv3). See LICENSE for details.

namespace HexIDE.VbLspServer;

/// <summary>
/// Static list of VB6 keywords and common built-ins returned as completion items.
/// </summary>
internal static class VbKeywords
{
    private static LspCompletionItem Kw(string label, string? insertText = null)
        => new(label, LspCompletionItemKind.Keyword, InsertText: insertText ?? label);

    private static LspCompletionItem Builtin(string label)
        => new(label, LspCompletionItemKind.Function);

    public static readonly LspCompletionItem[] All =
    [
        // Declarations
        Kw("Dim"),
        Kw("ReDim"),
        Kw("Preserve"),
        Kw("Static"),
        Kw("Public"),
        Kw("Private"),
        Kw("Friend"),
        Kw("Global"),
        Kw("Const"),
        Kw("Enum"),
        Kw("Type"),
        Kw("Implements"),
        Kw("As"),
        Kw("New"),
        Kw("ByVal"),
        Kw("ByRef"),
        Kw("Optional"),
        Kw("ParamArray"),

        // Procedures
        Kw("Sub"),
        Kw("Function"),
        Kw("End Sub"),
        Kw("End Function"),
        Kw("Property Get"),
        Kw("Property Let"),
        Kw("Property Set"),
        Kw("End Property"),
        Kw("Exit Sub"),
        Kw("Exit Function"),
        Kw("Exit Property"),

        // Control flow
        Kw("If"),
        Kw("Then"),
        Kw("Else"),
        Kw("ElseIf"),
        Kw("End If"),
        Kw("Select Case"),
        Kw("Case"),
        Kw("Case Else"),
        Kw("End Select"),
        Kw("For"),
        Kw("To"),
        Kw("Step"),
        Kw("Next"),
        Kw("For Each"),
        Kw("In"),
        Kw("Do"),
        Kw("Loop"),
        Kw("While"),
        Kw("Wend"),
        Kw("With"),
        Kw("End With"),
        Kw("GoTo"),
        Kw("GoSub"),
        Kw("Return"),
        Kw("Resume"),
        Kw("On Error"),
        Kw("On Error GoTo"),
        Kw("On Error Resume Next"),

        // Assignment / object
        Kw("Set"),
        Kw("Let"),
        Kw("Call"),
        Kw("Nothing"),

        // Literals / constants
        Kw("True"),
        Kw("False"),
        Kw("Empty"),
        Kw("Null"),

        // Data types
        Kw("Boolean"),
        Kw("Byte"),
        Kw("Integer"),
        Kw("Long"),
        Kw("Single"),
        Kw("Double"),
        Kw("Currency"),
        Kw("Date"),
        Kw("String"),
        Kw("Object"),
        Kw("Variant"),

        // Operators / misc
        Kw("Not"),
        Kw("And"),
        Kw("Or"),
        Kw("Xor"),
        Kw("Mod"),
        Kw("Like"),
        Kw("Is"),
        Kw("IsNull"),
        Kw("IsEmpty"),
        Kw("IsObject"),
        Kw("Option Explicit"),
        Kw("Option Base"),

        // Common built-ins (Function kind)
        Builtin("MsgBox"),
        Builtin("InputBox"),
        Builtin("Print"),
        Builtin("Debug"),
        Builtin("Err"),
        Builtin("Len"),
        Builtin("Left"),
        Builtin("Right"),
        Builtin("Mid"),
        Builtin("Trim"),
        Builtin("LTrim"),
        Builtin("RTrim"),
        Builtin("UCase"),
        Builtin("LCase"),
        Builtin("InStr"),
        Builtin("InStrRev"),
        Builtin("Replace"),
        Builtin("Split"),
        Builtin("Join"),
        Builtin("CStr"),
        Builtin("CInt"),
        Builtin("CLng"),
        Builtin("CDbl"),
        Builtin("CBool"),
        Builtin("CDate"),
        Builtin("Int"),
        Builtin("Fix"),
        Builtin("Abs"),
        Builtin("Sqr"),
        Builtin("Rnd"),
        Builtin("Now"),
        Builtin("Date"),
        Builtin("Time"),
        Builtin("Timer"),
        Builtin("Array"),
        Builtin("UBound"),
        Builtin("LBound"),
        Builtin("TypeName"),
        Builtin("VarType"),
        Builtin("Chr"),
        Builtin("Asc"),
        Builtin("Format"),
    ];

    /// <summary>
    /// Single-word reserved keywords that cannot be used as identifiers.
    /// Built-in functions (MsgBox, Len, Mid, etc.) are excluded — users can
    /// legitimately shadow them with local variables.
    /// </summary>
    public static readonly HashSet<string> ReservedWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Dim", "ReDim", "Preserve", "Static", "Public", "Private", "Friend",
        "Global", "Const", "Enum", "Type", "Implements", "As", "New",
        "ByVal", "ByRef", "Optional", "ParamArray",
        "Sub", "Function", "Property", "Get", "Let", "Set",
        "End", "Exit",
        "If", "Then", "Else", "ElseIf",
        "Select", "Case",
        "For", "To", "Step", "Next", "Each", "In",
        "Do", "Loop", "While", "Until", "Wend",
        "With", "GoTo", "GoSub", "Return", "Resume", "On", "Error",
        "Call", "Nothing",
        "True", "False", "Empty", "Null",
        "Boolean", "Byte", "Integer", "Long", "Single", "Double",
        "Currency", "Date", "String", "Object", "Variant",
        "Not", "And", "Or", "Xor", "Mod", "Like", "Is",
        "Option", "Explicit", "Base",
    };
}
