// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// Hand-curated table of VB6/VBA built-in names (VB6 language facts, solely ours).

namespace HexIDE.VbLspServer;

/// <summary>
/// Known VB6/VBA built-in names that can legitimately appear as assignment targets without
/// being declared by the user. Prevents false-positive "undeclared variable" warnings.
/// </summary>
internal static class VbBuiltins
{
    private static readonly HashSet<string> _names = new(StringComparer.OrdinalIgnoreCase)
    {
        // ── Error object ──────────────────────────────────────────────────────
        "Err",

        // ── Built-in VBA / VB6 type names (appear in New, As, and TypeOf) ────
        "Object", "Variant", "String", "Integer", "Long", "Single", "Double",
        "Boolean", "Byte", "Currency", "Date", "LongLong", "LongPtr",
        // Common built-in classes
        "Collection", "Dictionary", "FileSystemObject",
        "Scripting", "ADODB", "MSXML2", "WScript",
        // VB6 control types referenced via New or CreateObject
        "Form", "Control", "Picture", "Font", "StdPicture", "StdFont",

        // ── Form / UserControl intrinsic properties accessed without Me. ──────
        // These appear as bare assignment targets in form module code.
        "Caption", "BackColor", "ForeColor", "Font",
        "Width", "Height", "Left", "Top",
        "Enabled", "Visible", "Tag", "Index",
        "WindowState", "BorderStyle", "Picture",
        "MousePointer", "ClipControls", "AutoRedraw",
        "DrawStyle", "DrawWidth", "FillColor", "FillStyle",
        "ScaleMode", "ScaleLeft", "ScaleTop", "ScaleWidth", "ScaleHeight",
        "CurrentX", "CurrentY",
        "KeyPreview",

        // ── TextBox / ComboBox / ListBox properties ───────────────────────────
        "Text", "Value", "ListIndex", "SelStart", "SelLength", "SelText",
        "MultiLine", "ScrollBars", "MaxLength", "Alignment",

        // ── VB6 global objects ────────────────────────────────────────────────
        "App", "Screen", "Printer", "Clipboard", "Debug",

        // ── Common implicit form control names (Form1, etc. refer to themselves)
        "Me",
    };

    public static bool IsKnownName(string name) => _names.Contains(name);
}
