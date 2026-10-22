using System;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A ComboBox. Its Text, at Style 0 (Dropdown Combo) and Style 1 (Simple Combo), is its edit box's, as measured on vb6.exe
/// (the oracle's "A ComboBox's Text, at each Style", #926): code's Text selects no item, and choosing an item puts its text
/// in the box. Style 2 (Dropdown List) is #1428's, so its Text is refused at run time.
/// </summary>
public class VBComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);

    // Avalonia's editable combo selects the first item its new Text matches, without regard to case, where VB6's selects
    // none. While code puts its Text in, every item reads as this, which no Text a program sets can be, so none matches.
    private static readonly string NoItemText = $"no item {Guid.NewGuid():N}";
    private static readonly Binding NoItemMatches = new(".") { Converter = new FuncValueConverter<object?, string>(_ => NoItemText) };

    public VBComboBox() : this(0)
    {
    }

    /// <param name="comboStyle">The Style the form gave it, which is read from the file, not modelled (#1428).</param>
    public VBComboBox(int comboStyle)
    {
        ComboStyle = comboStyle;
        IsEditable = !IsDropdownList;
    }

    /// <summary>0 Dropdown Combo, 1 Simple Combo, 2 Dropdown List. Design-time only, as in VB6.</summary>
    public int ComboStyle { get; }

    public bool IsDropdownList => ComboStyle is not (0 or 1);

    /// <summary>
    /// <c>Text = s</c> on a Dropdown Combo or a Simple Combo: nothing when <paramref name="text"/> is its Text already,
    /// compared exactly. Otherwise the edit box takes it and the selection clears, even when it is an item's text
    /// (measured).
    /// </summary>
    public void SetTextFromCode(string text)
    {
        if (string.Equals(Text ?? "", text, StringComparison.Ordinal))
            return;
        TextSearch.SetTextBinding(this, NoItemMatches);
        try
        {
            SelectedIndex = -1;
            Text = text;
        }
        finally
        {
            TextSearch.SetTextBinding(this, null);
        }
    }
}
