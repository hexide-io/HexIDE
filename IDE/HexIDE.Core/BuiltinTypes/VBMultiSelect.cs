namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// A ListBox's MultiSelect. VB6 writes 1 and 2 with these names in a saved form, and drops 0 (measured, #1413).
/// </summary>
public enum VBMultiSelect
{
    [Vb6Name("None")] None = 0,
    [Vb6Name("Simple")] Simple = 1,
    [Vb6Name("Extended")] Extended = 2
}
