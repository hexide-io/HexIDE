using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

/// <summary>
/// A ComboBox. Its Text, at Style 0 (Dropdown Combo) and Style 1 (Simple Combo), is its edit box's, as measured on vb6.exe
/// (the oracle's "A ComboBox's Text, at each Style", #926): code's Text selects no item, and choosing an item puts its text
/// in the box. At Style 2 (Dropdown List) its Text is its selected item: code's Text selects the first item it matches,
/// without regard to case, or is 383 (#1428).
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
    private Border? listBorder;

    static VBComboBox()
    {
        // One GotFocus as the combo is entered, whether the combo or its text box takes the focus (measured, #1101).
        AttachedEvents.AttachFocusEvents<VBComboBox>();
    }

    public VBComboBox() : this(VBComboStyle.DropdownCombo)
    {
    }

    /// <param name="comboStyle">The Style the form gave it, which VB6 sets at design time only.</param>
    public VBComboBox(VBComboStyle comboStyle)
    {
        ComboStyle = comboStyle;
        IsEditable = !IsDropdownList;
        if (IsSimpleCombo)
        {
            Template = SimpleTemplate;
            // vb6.exe's rows are 13 pixels at its default font, a line of text and no more (measured, #1603).
            Styles.Add(new Style(x => x.OfType<ComboBoxItem>())
            {
                Setters = { new Setter(PaddingProperty, new Thickness(2, 0)), new Setter(MinHeightProperty, 0d) },
            });
            // On the way down, so the press is the list's before an item takes it.
            AddHandler(PointerPressedEvent, PressList, RoutingStrategies.Tunnel);
            AddHandler(PointerReleasedEvent, ReleaseList, RoutingStrategies.Tunnel);
        }
        // A Dropdown List shows its item in the combo's own padding, too deep for a combo's height: its text was cut off at
        // the bottom, as the edit box's was.
        Padding = new Thickness(2, 0);
        VerticalContentAlignment = VerticalAlignment.Center;
        if (IsEditable)
            TextSearch.SetTextBinding(this, NoItemMatches);
        // On the way down, so the edit box does not take them first.
        AddHandler(KeyDownEvent, TakeKey, RoutingStrategies.Tunnel);
    }

    /// <summary>Dropdown Combo, Simple Combo or Dropdown List. Design-time only, as in VB6.</summary>
    public VBComboStyle ComboStyle { get; }

    public bool IsDropdownList => ComboStyle == VBComboStyle.DropdownList;

    public bool IsSimpleCombo => ComboStyle == VBComboStyle.SimpleCombo;

    // The edit box's height at VB6's default font, MS Sans Serif 8, where vb6.exe drew it 21 pixels high (measured, #1603).
    private const double SimpleEditHeight = 21;

    /// <summary>
    /// A Simple Combo: its edit box, with its list under it, always shown, and no button (measured, #1603). The list
    /// scrolls when its items don't fit. Avalonia's ComboBox requires a popup, which is there and never opens.
    /// </summary>
    private static readonly FuncControlTemplate<VBComboBox> SimpleTemplate = new((combo, scope) =>
    {
        var edit = new TextBox
        {
            Name = "PART_EditableTextBox",
            Height = SimpleEditHeight,
            [DockPanel.DockProperty] = Dock.Top,
            [!!TextBox.TextProperty] = combo[!!TextProperty],
            [!TextBox.BackgroundProperty] = combo[!BackgroundProperty],
            [!TextBox.ForegroundProperty] = combo[!ForegroundProperty],
        }.RegisterInNameScope(scope);
        var items = new ItemsPresenter
        {
            Name = "PART_ItemsPresenter",
            [!ItemsPresenter.ItemsPanelProperty] = combo[!ItemsPanelProperty],
        }.RegisterInNameScope(scope);
        var popup = new Popup { Name = "PART_Popup", [DockPanel.DockProperty] = Dock.Top }.RegisterInNameScope(scope);
        var list = new Border
        {
            Name = "PART_List",
            BorderThickness = new Thickness(1),
            [!Border.BorderBrushProperty] = combo[!BorderBrushProperty],
            [!Border.BackgroundProperty] = combo[!BackgroundProperty],
            Child = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                Content = items,
            },
        }.RegisterInNameScope(scope);
        return new DockPanel { LastChildFill = true, Children = { edit, popup, list } };
    });

    // A press on a Simple Combo's list picks the item under it, or the last item when it is below them, the one already
    // selected too, as vb6.exe's list does on the press: the selection has moved when GotFocus runs, and its Click follows.
    // A double click's second press raises DblClick and picks nothing more. Measured, #1603.
    private void PressList(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed || e.Source is not Visual source || !IsInList(source)
            || ItemCount == 0)
            return;
        e.Handled = true;
        var item = ItemOf(source);
        if (item is not null && e.ClickCount == 2)
        {
            this.ExecuteSub(ComponentBaseClass.DblClickEvent);
            return;
        }
        var index = item is null ? ItemCount - 1 : IndexFromContainer(item);
        // vb6.exe's list moves its selection with the press and its edit box takes the item's text after, so a GotFocus
        // the press raises reads the new ListIndex and the Text from before (measured, #1603).
        if (editBox is { IsFocused: false } box)
        {
            textBeforePick = Text ?? "";
            try
            {
                SelectedIndex = index;
                box.Focus(NavigationMethod.Pointer);
            }
            finally
            {
                textBeforePick = null;
            }
        }
        else
            SelectedIndex = index;
        Picked();
    }

    // The Text a Simple Combo shows while a press on its list moves the focus to it.
    private string? textBeforePick;

    /// <summary>
    /// Its Text as code reads it: a Dropdown List's selected item, or the edit box's, which is the Text from before a press
    /// on a Simple Combo's list while that press is moving the focus to it.
    /// </summary>
    internal string VBText => textBeforePick ?? (IsDropdownList ? ListText : Text ?? "");

    // Its release is the press's: the list's own release would pick the item a second time.
    private void ReleaseList(object? sender, PointerReleasedEventArgs e)
    {
        if (e.Source is Visual source && IsInList(source))
            e.Handled = true;
    }

    private bool IsInList(Visual source) =>
        listBorder is { } list && (ReferenceEquals(source, list) || list.IsVisualAncestorOf(source));

    private ComboBoxItem? ItemOf(Visual source) =>
        source as ComboBoxItem ?? source.FindAncestorOfType<ComboBoxItem>();

    /// <summary>A Dropdown List's Text: its selected item's text, or "" with none selected (measured).</summary>
    internal string ListText => SelectedIndex >= 0 && SelectedIndex < ItemCount ? Items[SelectedIndex] as string ?? "" : "";

    // The edit box is one line, at a combo's height, which is too short for a TextBox's own padding: its text was cut off
    // and it showed the top of a vertical scroll bar. It scrolls only sideways, as a single-line TextBox does (#914).
    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        editBox = e.NameScope.Find<TextBox>("PART_EditableTextBox");
        listBorder = e.NameScope.Find<Border>("PART_List");
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
        {
            // A Simple Combo's list is always shown: it has nothing to drop, and raises no DropDown (measured, #1603).
            if (IsSimpleCombo)
                SetCurrentValue(IsDropDownOpenProperty, false);
            else
                this.ExecuteSub(ComboBoxComponentClass.DropDownEvent);
        }
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
        // A Simple Combo's pick leaves the focus in its edit box, as vb6.exe's does: what is typed next goes there.
        if (IsSimpleCombo && editBox is { IsFocused: false } box)
            box.Focus(NavigationMethod.Pointer);
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

    // VB6's own description for this 383, not the standard "Property Set can't be used with a read-only property".
    private static readonly VBStandardError TextIsReadOnly = new(383, "'Text' property is read-only");

    /// <summary>
    /// <c>Text = s</c> on a Dropdown List: it selects the first item whose whole text is <paramref name="text"/>, without
    /// regard to case, and raises Click every time, even when that item is selected already. With no such item it is 383,
    /// and nothing changes (measured).
    /// </summary>
    internal void SetListTextFromCode(string text)
    {
        var index = -1;
        for (var i = 0; i < ItemCount && index < 0; i++)
            if (string.Equals(Items[i] as string, text, StringComparison.OrdinalIgnoreCase))
                index = i;
        if (index < 0)
            throw new VBRunTimeException(TextIsReadOnly);
        SelectedIndex = index;
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
    }
}
