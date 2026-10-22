using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A ComboBox. Its Text, at Style 0 (Dropdown Combo) and Style 1 (Simple Combo), is its edit box's, as measured on vb6.exe
/// (the oracle's "A ComboBox's Text, at each Style", #926): code's Text selects no item, and choosing an item puts its text
/// in the box. Style 2 (Dropdown List) is #1428's, so its Text is refused at run time.
/// </summary>
/// <remarks>
/// Its events, measured with real input (the oracle's "A ComboBox's Click, Change and DropDown", #1429):
/// <list type="bullet">
/// <item>Click is a choice: every pick of an item by the mouse, the one already selected too, every arrow key that moves
/// the selection, with the list open or closed, a Dropdown List's letter key, and code's ListIndex when it moves.</item>
/// <item>Change is the edit box's, from typing and from code's Text, never from choosing an item. Typing selects
/// nothing, even an item's whole text.</item>
/// <item>DropDown comes as the list opens.</item>
/// </list>
/// So Click is raised where the input is taken, not from Avalonia's selection, which also moves for a list edit that
/// raises nothing.
/// </remarks>
public class VBComboBox : ComboBox
{
    protected override Type StyleKeyOverride => typeof(ComboBox);

    // Avalonia's editable combo selects the first item its Text matches, without regard to case, whether code or typing
    // put the Text there. VB6's selects none. So every item reads as this, which no Text can be, and none matches; but not
    // while Avalonia puts the selected item's own text in the box.
    private static readonly string NoItemText = $"no item {Guid.NewGuid():N}";
    private static readonly Binding NoItemMatches = new(".") { Converter = new FuncValueConverter<object?, string>(_ => NoItemText) };

    // Set while the box takes the selected item's text, or code's: neither raises Change from here.
    private bool textFromSelection;
    private bool textFromCode;
    private TextBox? editBox;

    public VBComboBox() : this(0)
    {
    }

    /// <param name="comboStyle">The Style the form gave it, which is read from the file, not modelled (#1428).</param>
    public VBComboBox(int comboStyle)
    {
        ComboStyle = comboStyle;
        IsEditable = !IsDropdownList;
        // A Dropdown List shows its item in the combo's own padding, too deep for a combo's height: its text was cut off at
        // the bottom, as the edit box's was.
        Padding = new Thickness(2, 0);
        VerticalContentAlignment = VerticalAlignment.Center;
        if (IsEditable)
            TextSearch.SetTextBinding(this, NoItemMatches);
        // On the way down, so the edit box does not take them first.
        AddHandler(KeyDownEvent, TakeKey, RoutingStrategies.Tunnel);
    }

    /// <summary>0 Dropdown Combo, 1 Simple Combo, 2 Dropdown List. Design-time only, as in VB6.</summary>
    public int ComboStyle { get; }

    public bool IsDropdownList => ComboStyle is not (0 or 1);

    // The edit box is one line, at a combo's height, which is too short for a TextBox's own padding: its text was cut off
    // and it showed the top of a vertical scroll bar. It scrolls only sideways, as a single-line TextBox does (#914).
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        editBox = e.NameScope.Find<TextBox>("PART_EditableTextBox");
        if (editBox is not { } box)
            return;
        box.MinHeight = 0;
        box.Padding = new Thickness(2, 0);
        box.VerticalContentAlignment = VerticalAlignment.Center;
        ScrollViewer.SetVerticalScrollBarVisibility(box, ScrollBarVisibility.Disabled);
        ScrollViewer.SetHorizontalScrollBarVisibility(box, ScrollBarVisibility.Hidden);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        if (change.Property == SelectedItemProperty && IsEditable)
        {
            if (textFromSelection)
            {
                base.OnPropertyChanged(change);
                return;
            }
            // The selected item's own text goes in the box, so the guard is off while Avalonia reads it.
            var chosen = SelectedIndex;
            textFromSelection = true;
            TextSearch.SetTextBinding(this, null);
            try
            {
                base.OnPropertyChanged(change);
                // Avalonia matched that text against every item without regard to case, and selected the first match:
                // for an item whose text another shares, the wrong one. The box holds the chosen item's text already, so
                // choosing it again changes no text, and nothing is matched.
                if (SelectedIndex != chosen)
                    SelectedIndex = chosen;
            }
            finally
            {
                TextSearch.SetTextBinding(this, NoItemMatches);
                textFromSelection = false;
            }
            return;
        }
        base.OnPropertyChanged(change);
        if (change.Property == TextProperty && IsEditable && !textFromSelection && !textFromCode)
            this.ExecuteSub(TextBoxComponentClass.ChangeEvent);
        else if (change.Property == IsDropDownOpenProperty && change.GetNewValue<bool>())
            this.ExecuteSub(ComboBoxComponentClass.DropDownEvent);
    }

    // An item picked with the mouse is Click, even the one already selected (measured).
    public override bool UpdateSelectionFromEvent(Control container, RoutedEventArgs eventArgs)
    {
        if (!base.UpdateSelectionFromEvent(container, eventArgs))
            return false;
        if (eventArgs is PointerEventArgs)
            Picked();
        return true;
    }

    // A Dropdown List takes a letter as a pick of the first item starting with it (measured).
    protected override void OnTextInput(TextInputEventArgs e)
    {
        var before = SelectedIndex;
        base.OnTextInput(e);
        if (IsDropdownList && SelectedIndex != before)
            Picked();
    }

    // Up and Down move the selection at once, list open or closed, and raise Click when they move it; Enter in the open
    // list only closes it (measured). Avalonia moves only the focus in an open list, and from no selection Up selects
    // nothing; VB6's selects the first item.
    private void TakeKey(object? sender, KeyEventArgs e)
    {
        if (e.KeyModifiers.HasFlag(KeyModifiers.Alt) || ItemCount == 0)
            return;
        if (e.Key == Key.Enter && IsDropDownOpen)
        {
            SetCurrentValue(IsDropDownOpenProperty, false);
            e.Handled = true;
            return;
        }
        if (e.Key is not (Key.Up or Key.Down))
            return;
        e.Handled = true;
        var index = SelectedIndex < 0 ? 0 : Math.Clamp(SelectedIndex + (e.Key == Key.Down ? 1 : -1), 0, ItemCount - 1);
        if (index == SelectedIndex)
            return;
        SelectedIndex = index;
        if (IsDropDownOpen)
            ContainerFromIndex(index)?.Focus(NavigationMethod.Directional);
        Picked();
    }

    // A choice puts the item's text in the edit box selected, so a key typed next replaces it (measured with the mouse).
    private void Picked()
    {
        if (IsEditable)
            editBox?.SelectAll();
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    /// <summary><c>ListIndex = n</c> from code: Click when it moves the selection, and none when it doesn't (measured).</summary>
    internal void SetListIndexFromCode(int index)
    {
        if (index == SelectedIndex)
            return;
        SelectedIndex = index;
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }

    /// <summary>
    /// <c>Text = s</c> on a Dropdown Combo or a Simple Combo: nothing when <paramref name="text"/> is its Text already,
    /// compared exactly. Otherwise the edit box takes it, the selection clears, even when it is an item's text, and Change
    /// is raised, once (measured).
    /// </summary>
    public void SetTextFromCode(string text)
    {
        if (string.Equals(Text ?? "", text, StringComparison.Ordinal))
            return;
        textFromCode = true;
        try
        {
            SelectedIndex = -1;
            Text = text;
        }
        finally
        {
            textFromCode = false;
        }
        this.ExecuteSub(TextBoxComponentClass.ChangeEvent);
    }
}
