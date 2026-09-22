using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;

namespace HexIDE.Controls;

public class PropertyBox : TemplatedControl
{
    private Panel? panel;
    public static readonly StyledProperty<IDataTemplate?> GenericTemplateProperty = AvaloniaProperty.Register<PropertyBox, IDataTemplate?>(nameof(GenericTemplate));
    public static readonly StyledProperty<IDataTemplate?> ColorTemplateProperty = AvaloniaProperty.Register<PropertyBox, IDataTemplate?>(nameof(ColorTemplate));
    public static readonly StyledProperty<IDataTemplate?> EnumTemplateProperty = AvaloniaProperty.Register<PropertyBox, IDataTemplate?>(nameof(EnumTemplate));
    public static readonly StyledProperty<IDataTemplate?> FontTemplateProperty = AvaloniaProperty.Register<PropertyBox, IDataTemplate?>(nameof(FontTemplate));
    public static readonly StyledProperty<IDataTemplate?> StringListTemplateProperty = AvaloniaProperty.Register<PropertyBox, IDataTemplate?>(nameof(StringListTemplate));

    public static readonly StyledProperty<PropertyClass?> PropertyClassProperty = AvaloniaProperty.Register<PropertyBox, PropertyClass?>("Property");
    public static readonly StyledProperty<object?> ObjectProperty = AvaloniaProperty.Register<PropertyBox, object?>("Object", defaultBindingMode: BindingMode.TwoWay);

    public object? Object
    {
        get => GetValue(ObjectProperty);
        set => SetValue(ObjectProperty, value);
    }

    public PropertyClass? PropertyClass
    {
        get => GetValue(PropertyClassProperty);
        set => SetValue(PropertyClassProperty, value);
    }

    public IDataTemplate? GenericTemplate
    {
        get => GetValue(GenericTemplateProperty);
        set => SetValue(GenericTemplateProperty, value);
    }

    public IDataTemplate? ColorTemplate
    {
        get => GetValue(ColorTemplateProperty);
        set => SetValue(ColorTemplateProperty, value);
    }

    public IDataTemplate? EnumTemplate
    {
        get => GetValue(EnumTemplateProperty);
        set => SetValue(EnumTemplateProperty, value);
    }

    public IDataTemplate? FontTemplate
    {
        get => GetValue(FontTemplateProperty);
        set => SetValue(FontTemplateProperty, value);
    }

    public IDataTemplate? StringListTemplate
    {
        get => GetValue(StringListTemplateProperty);
        set => SetValue(StringListTemplateProperty, value);
    }

    static PropertyBox()
    {
        PropertyClassProperty.Changed.AddClassHandler<PropertyBox>((box, e) =>
        {
            box.UpdatePropertyVisibility();
        });
        KeyDownEvent.AddClassHandler<PropertyBox>(OnKeyDownTunnel, RoutingStrategies.Tunnel);
    }

    /// <summary>
    /// Enter commits the text typed into a property row, as it does in VB6's Properties window.
    /// </summary>
    /// <remarks>
    /// The row's text box commits on focus loss, and nothing handled Enter, so a person pressing it saw the text
    /// stay and nothing change until they clicked elsewhere; <c>press_key</c> Enter on the row did nothing at
    /// all (#625). Tunnelled so the text box cannot take the key first. The text is selected afterwards, so the
    /// next keystroke replaces the committed value rather than appending to it.
    /// </remarks>
    private static void OnKeyDownTunnel(PropertyBox box, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || e.KeyModifiers != KeyModifiers.None) return;
        if ((e.Source as Visual)?.FindAncestorOfType<TextBox>(includeSelf: true) is not { } textBox) return;

        BindingOperations.GetBindingExpressionBase(textBox, TextBox.TextProperty)?.UpdateSource();
        textBox.SelectAll();
        e.Handled = true;
    }

    protected override void OnApplyTemplate(TemplateAppliedEventArgs e)
    {
        base.OnApplyTemplate(e);
        panel = e.NameScope.Get<Panel>("PART_Panel");
        UpdatePropertyVisibility();
    }

    private void UpdatePropertyVisibility()
    {
        if (panel == null)
            return;

        panel.Children.Clear();

        if (PropertyClass == null)
            return;

        if (PropertyClass.PropertyType == typeof(VBColor))
        {
            panel.Children.Add(ColorTemplate?.Build(null)!);
        }
        else if (PropertyClass.PropertyType == typeof(VBFont))
        {
            panel.Children.Add(FontTemplate?.Build(null)!);
        }
        else if (PropertyClass.PropertyType.IsEnum || PropertyClass.PropertyType == typeof(bool))
        {
            panel.Children.Add(EnumTemplate?.Build(null)!);
        }
        else if (PropertyClass.PropertyType == typeof(List<string>))
        {
            panel.Children.Add(StringListTemplate?.Build(null)!);
        }
        else
        {
            panel.Children.Add(GenericTemplate?.Build(null)!);
        }
    }
}