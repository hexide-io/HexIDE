namespace HexIDE.Runtime.BuiltinTypes;

// A form's six styles, distinct from a control's two borders (#1596, oracle PR #5216).
internal enum VBFormBorderStyle
{
    [Vb6Name("None")] None = 0,
    [Vb6Name("Fixed Single")] FixedSingle = 1,
    [Vb6Name("Sizable")] Sizable = 2,
    [Vb6Name("Fixed Dialog")] FixedDialog = 3,
    [Vb6Name("Fixed ToolWindow")] FixedToolWindow = 4,
    [Vb6Name("Sizable ToolWindow")] SizableToolWindow = 5
}
