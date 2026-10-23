namespace HexIDE.Runtime.BuiltinTypes;

/// <summary>
/// A ComboBox's Style. VB6's designer writes 1 and 2 with these names in a saved form, and drops 0 (measured, #926).
/// </summary>
public enum VBComboStyle
{
    [Vb6Name("Dropdown Combo")] DropdownCombo = 0,
    [Vb6Name("Simple Combo")] SimpleCombo = 1,
    [Vb6Name("Dropdown List")] DropdownList = 2
}
