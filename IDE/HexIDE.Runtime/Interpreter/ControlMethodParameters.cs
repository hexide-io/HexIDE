using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;

namespace HexIDE.Runtime.Interpreter;

// Only names measured on SP6 native and p-code in #1474 (2026-11-18, measuring PR #4717).
// The form type library names other methods too; that alone does not establish that VB6 accepts those names.
internal sealed class ControlMethodParameters(object target) : INamedParameterSource
{
    internal static bool IsTarget(object? target) => target is VBListBox or VBComboBox or VBTextBox or VBPictureBox
        or Window or VbObject { ClassDef.Kind: InterpreterModuleKind.Form };

    public bool TryGetParameters(string member, out IReadOnlyList<string> names, out int required)
    {
        var form = target is VbObject { ClassDef.Kind: InterpreterModuleKind.Form } or Window;
        names = member.ToUpperInvariant() switch
        {
            "ADDITEM" when target is VBListBox or VBComboBox => ["Item", "Index"],
            "REMOVEITEM" when target is VBListBox or VBComboBox => ["Index"],
            "MOVE" when form || target is VBTextBox => ["Left", "Top", "Width", "Height"],
            "ZORDER" => ["Position"],
            "TEXTWIDTH" when form => ["Str"],
            "SCALEX" when target is VBPictureBox => ["Width", "FromScale", "ToScale"],
            _ => [],
        };
        required = member.Equals("ZOrder", StringComparison.OrdinalIgnoreCase) ? 0 : 1;
        return names.Count != 0;
    }
}
