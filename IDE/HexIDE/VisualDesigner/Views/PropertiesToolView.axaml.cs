using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.VisualTree;
using HexIDE.Events;

namespace HexIDE.VisualDesigner.Views;

public partial class PropertiesToolView : UserControl
{
    private IDisposable? sub;

    public PropertiesToolView()
    {
        InitializeComponent();
    }

    private void PropertyGotFocus(object? sender, FocusChangedEventArgs e)
    {
        if (e.Source is Control c)
        {
            if (c.FindAncestorOfType<ListBoxItem>() is { } listBoxItem &&
                c.FindAncestorOfType<ListBox>() is { } listBox)
            {
                var dataContext = listBoxItem.DataContext;
                listBox.SelectedItem = dataContext;
            }
        }
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        sub?.Dispose();
        sub = null;
        if (DataContext is PropertiesToolViewModel vm)
        {
            sub = vm.EventBus.Subscribe<TextInputForPropertyEvent>(OnTextInputForProperty);
        }
    }

    private void OnTextInputForProperty(TextInputForPropertyEvent e)
    {
        var listBox = AlphabeticPropertiesTab.IsSelected ? AlphabeticProperties : CategorizedProperties;

        var container = listBox.ContainerFromIndex(listBox.SelectedIndex);
        if (container == null)
            return;

        Control? control = null;

        if (container.FindDescendantOfType<ComboBox>() is { } comboBox)
        {
            control = comboBox;
        }
        else if (container.FindDescendantOfType<TextBox>() is { } textBox)
        {
            control = textBox;
            textBox.SelectAll();
        }

        if (control != null)
        {
            control.Focus();
            control.RaiseEvent(new TextInputEventArgs()
            {
                RoutedEvent = TextBox.TextInputEvent,
                Source = this,
                Text = e.Text
            });
        }

    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        sub?.Dispose();
        sub = null;
    }
}