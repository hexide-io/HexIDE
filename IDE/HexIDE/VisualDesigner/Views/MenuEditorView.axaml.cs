using Avalonia;
using Avalonia.Controls;

namespace HexIDE.VisualDesigner;

public partial class MenuEditorView : UserControl
{
    public static readonly StyledProperty<string> BlankItemNameProperty =
        AvaloniaProperty.Register<MenuEditorView, string>(nameof(BlankItemName), "");
    public static readonly StyledProperty<string> ItemLevelFormatProperty =
        AvaloniaProperty.Register<MenuEditorView, string>(nameof(ItemLevelFormat), "");

    public string BlankItemName
    {
        get => GetValue(BlankItemNameProperty);
        set => SetValue(BlankItemNameProperty, value);
    }
    public string ItemLevelFormat
    {
        get => GetValue(ItemLevelFormatProperty);
        set => SetValue(ItemLevelFormatProperty, value);
    }

    public MenuEditorView()
    {
        InitializeComponent();
        InsertButton.Click += (_, _) => CaptionField.Focus();
    }
}
