using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Net.Sockets;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.VisualDesigner;

public partial class MenuEditorViewModel : ObservableObject, IDialog
{
    [Notify] private MenuViewModel? selectedMenu;

    public ObservableCollection<MenuViewModel> FlatMenu { get; } = new();

    public List<MenuViewModel> Deleted { get; } = new();

    private readonly IWindowManager? windowManager;
    private readonly ILocalizationService? localization;
    private readonly IEnumerable<string> controlNames = [];

    private MenuViewModel blankMenu = null!;
    private readonly List<(MenuViewModel Menu, int Indent)> originalItems = new();
    internal bool HasLayoutChanges => !ItemsToApply.Select(menu => (menu, menu.Indent)).SequenceEqual(originalItems);
    internal bool HasChanges => HasLayoutChanges || ItemsToApply.Any(menu => menu.HasChanges);
    internal IEnumerable<MenuViewModel> ItemsToApply => FlatMenu.Where(menu => menu != blankMenu);

    public sealed record Choice(string Label, string Value);
    public List<Choice> Shortcuts { get; } = CreateShortcuts();

    private static List<Choice> CreateShortcuts(ILocalizationService? localization = null)
    {
        string Text(string key, string fallback)
        {
            var value = localization?.GetString("Str.Dialog.MenuEditor.Shortcut." + key);
            return string.IsNullOrEmpty(value) ? fallback : value;
        }

        var ctrl = Text("CtrlFormat", "Ctrl+{0}");
        var shift = Text("ShiftFormat", "Shift+{0}");
        var shiftCtrl = Text("ShiftCtrlFormat", "Shift+Ctrl+{0}");
        var alt = Text("AltFormat", "Alt+{0}");
        var function = Text("FunctionFormat", "F{0}");
        var insert = Text("InsertKey", "Ins");
        var delete = Text("DeleteKey", "Del");
        var backspace = Text("BackspaceKey", "Bksp");
        var choices = new List<Choice> { new(Text("None", "(None)"), "") };
        for (var key = 'A'; key <= 'Z'; key++)
            choices.Add(new(string.Format(ctrl, key), "^" + key));
        foreach (var (format, prefix) in new[] { ("{0}", ""), (ctrl, "^"), (shift, "+"), (shiftCtrl, "+^") })
            for (var key = 1; key <= 12; key++)
                if (key != 10)
                    choices.Add(new(string.Format(format, string.Format(function, key)), prefix + "{F" + key + "}"));
        choices.AddRange([new(string.Format(ctrl, insert), "^{INSERT}"), new(string.Format(shift, insert), "+{INSERT}"),
            new(delete, "{DEL}"), new(string.Format(shift, delete), "+{DEL}"), new(string.Format(alt, backspace), "%{BKSP}")]);
        return choices;
    }

    public MenuEditorViewModel()
    {
        AddBlankRow();
        SelectedMenu = blankMenu;
    }

    public MenuEditorViewModel(FormEditViewModel form, ILocalizationService? localization = null)
    {
        Shortcuts = CreateShortcuts(localization);
        Title = localization?.GetString("Str.Dialog.MenuEditor.Title") ?? Title;

        void AddRecur(ComponentInstance menu, int level)
        {
            var vm = new MenuViewModel(menu);
            vm.Indent = level;
            FlatMenu.Add(vm);
            originalItems.Add((vm, level));
            if (menu.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) is { } subItems)
            {
                foreach (var subItem in subItems)
                    AddRecur(subItem, level + 1);
            }
        }
        foreach (var menu in form.TopLevelMenu)
            AddRecur(menu.Instance, 0);
        AddBlankRow();
        SelectedMenu = blankMenu;
    }

    internal MenuEditorViewModel(FormEditViewModel form, IWindowManager windowManager,
        IEnumerable<string> controlNames, ILocalizationService localization) : this(form, localization)
    {
        this.windowManager = windowManager;
        this.localization = localization;
        this.controlNames = controlNames;
    }

    private void AddBlankRow()
    {
        blankMenu = new MenuViewModel();
        blankMenu.PropertyChanged += OnBlankCaptionChanged;
        FlatMenu.Add(blankMenu);
    }

    private void OnBlankCaptionChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName != nameof(MenuViewModel.Caption) || string.IsNullOrEmpty(blankMenu.Caption))
            return;

        // The caption promotes this row; the next blank starts at level zero until Next reaches it (#4141).
        blankMenu.PropertyChanged -= OnBlankCaptionChanged;
        AddBlankRow();
    }

    public void Accept()
    {
        if (localization is not null && ComponentNaming.MenuRefusalFor(ItemsToApply, controlNames, localization) is { } refusal)
        {
            SelectedMenu = refusal.Row;
            // VB6 titles these boxes with its product name, as the default caption does here.
            _ = windowManager!.MessageBox(refusal.Message, null, MessageBoxButtons.Ok, MessageBoxIcon.Error);
            return;
        }
        CloseRequested?.Invoke(true);
    }

    public void Cancel()
    {
        CloseRequested?.Invoke(false);
    }

    public void Next()
    {
        if (selectedMenu == null)
            return;

        var indexOf = FlatMenu.IndexOf(selectedMenu) + 1;
        if (indexOf >= FlatMenu.Count)
            indexOf = 0;

        var next = FlatMenu[indexOf];
        if (next == blankMenu)
            next.Indent = selectedMenu.Indent;
        SelectedMenu = next;
    }

    public void Insert()
    {
        var indexOf = selectedMenu == null ? Math.Max(0, FlatMenu.Count - 1) : FlatMenu.IndexOf(selectedMenu) + 1;
        indexOf = Math.Min(indexOf, FlatMenu.Count - 1);
        var newMenu = new MenuViewModel();
        FlatMenu.Insert(indexOf, newMenu);
        newMenu.Indent = selectedMenu?.Indent ?? 0;
        SelectedMenu = newMenu;
    }

    public void Delete()
    {
        if (selectedMenu == null || selectedMenu == blankMenu)
            return;

        var indexOf = FlatMenu.IndexOf(selectedMenu);
        FlatMenu.RemoveAt(indexOf);
        indexOf = Math.Min(indexOf, FlatMenu.Count - 1);
        Deleted.Add(selectedMenu);
        if (indexOf >= 0)
            SelectedMenu = FlatMenu[indexOf];
        else
            SelectedMenu = null;
    }

    public void MoveLeft()
    {
        if (selectedMenu == null)
            return;

        selectedMenu.Indent = Math.Max(0, selectedMenu.Indent - 1);
    }

    public void MoveRight()
    {
        if (selectedMenu == null)
            return;

        selectedMenu.Indent = selectedMenu.Indent + 1;
    }

    public void MoveUp()
    {
        if (selectedMenu == null || selectedMenu == blankMenu)
            return;

        var indexOf = FlatMenu.IndexOf(selectedMenu);
        if (indexOf > 0)
        {
            FlatMenu.Move(indexOf, indexOf - 1);
        }
    }

    public void MoveDown()
    {
        if (selectedMenu == null)
            return;

        var indexOf = FlatMenu.IndexOf(selectedMenu);
        if (indexOf < FlatMenu.Count - 2)
        {
            FlatMenu.Move(indexOf, indexOf + 1);
        }
    }

    public string Title { get; } = "Menu Editor";
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;
}

public partial class MenuViewModel : ObservableObject
{
    public ComponentInstance? Menu { get; }
    [Notify] private string caption;
    [Notify] private string name;
    [Notify] private bool isChecked;
    [Notify] private bool isEnable = true;
    [Notify] private bool isVisible = true;
    [Notify] private bool isWindowsList;
    [Notify] private int indent;
    [Notify] private int? index;
    [Notify] private string shortcut = "";
    [Notify] private int helpContextID;
    [Notify] private int negotiatePosition;

    public MenuViewModel()
    {
        caption = "";
        name = "";
    }

    public MenuViewModel(ComponentInstance menu)
    {
        Menu = menu;
        // Index is the preserved .frm line every control shares (#4284), not a menu property of its own.
        index = PropertyText.ArrayIndexOf(menu);
        shortcut = menu.GetPropertyOrDefault(MenuComponentClass.ShortcutProperty) ?? "";
        helpContextID = menu.GetPropertyOrDefault(VBProperties.HelpContextIDProperty);
        negotiatePosition = menu.GetPropertyOrDefault(MenuComponentClass.NegotiatePositionProperty);
        caption = menu.GetPropertyOrDefault(VBProperties.CaptionProperty) ?? "";
        name = menu.GetPropertyOrDefault(VBProperties.NameProperty) ?? "";
        isChecked = menu.GetPropertyOrDefault(VBProperties.CheckedProperty);
        isEnable = menu.GetPropertyOrDefault(VBProperties.EnabledProperty);
        isVisible = menu.GetPropertyOrDefault(VBProperties.VisibleProperty);
        isWindowsList = menu.GetPropertyOrDefault(VBProperties.WindowListProperty);
    }

    internal bool HasChanges => Menu is null
        || index != PropertyText.ArrayIndexOf(Menu)
        || shortcut != Menu.GetPropertyOrDefault(MenuComponentClass.ShortcutProperty)
        || helpContextID != Menu.GetPropertyOrDefault(VBProperties.HelpContextIDProperty)
        || negotiatePosition != Menu.GetPropertyOrDefault(MenuComponentClass.NegotiatePositionProperty)
        || caption != (Menu.GetPropertyOrDefault(VBProperties.CaptionProperty) ?? "")
        || name != (Menu.GetPropertyOrDefault(VBProperties.NameProperty) ?? "")
        || isEnable != Menu.GetPropertyOrDefault(VBProperties.EnabledProperty)
        || isVisible != Menu.GetPropertyOrDefault(VBProperties.VisibleProperty)
        || isChecked != Menu.GetPropertyOrDefault(VBProperties.CheckedProperty)
        || isWindowsList != Menu.GetPropertyOrDefault(VBProperties.WindowListProperty);

    public void Apply(ComponentInstance menu)
    {
        void SetIfChanged<T>(PropertyClass<T> property, T value)
        {
            if (!EqualityComparer<T>.Default.Equals(menu.GetPropertyOrDefault(property), value))
                menu.SetProperty(property, value);
        }

        if (index != PropertyText.ArrayIndexOf(menu))
            menu.SetUntypedProperty(VBProperties.DesignIndexProperty, index?.ToString(CultureInfo.InvariantCulture) ?? "");
        SetIfChanged(MenuComponentClass.ShortcutProperty, shortcut);
        SetIfChanged(VBProperties.HelpContextIDProperty, helpContextID);
        SetIfChanged(MenuComponentClass.NegotiatePositionProperty, negotiatePosition);
        SetIfChanged(VBProperties.CaptionProperty, caption);
        SetIfChanged(VBProperties.NameProperty, name);
        SetIfChanged(VBProperties.EnabledProperty, isEnable);
        SetIfChanged(VBProperties.VisibleProperty, isVisible);
        SetIfChanged(VBProperties.CheckedProperty, isChecked);
        SetIfChanged(VBProperties.WindowListProperty, isWindowsList);
    }
}
