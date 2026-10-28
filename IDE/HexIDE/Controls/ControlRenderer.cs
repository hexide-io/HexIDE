using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using Serilog;
using ComponentInstanceViewModel = HexIDE.VisualDesigner.ComponentInstanceViewModel;

namespace HexIDE.Controls;

public class ControlRenderer : Decorator
{
    public static readonly StyledProperty<ComponentInstanceViewModel?> ComponentProperty = AvaloniaProperty.Register<ControlRenderer, ComponentInstanceViewModel?>("Component");

    public ComponentInstanceViewModel? Component
    {
        get => GetValue(ComponentProperty);
        set => SetValue(ComponentProperty, value);
    }

    static ControlRenderer()
    {
        ComponentProperty.Changed.AddClassHandler<ControlRenderer>(((renderer, args) => renderer.UpdateContent()));
        BoundsProperty.Changed.AddClassHandler<ControlRenderer>((renderer, args) => renderer.UpdateContent());
    }

    private ComponentInstanceViewModel? boundViewModel;

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        Detach();
        if (Component != null)
        {
            boundViewModel = Component;
            Component.Instance.OnComponentPropertyChanged += ComponentOnOnComponentPropertyChanged;
        }
    }

    private void Detach()
    {
        if (boundViewModel != null)
        {
            boundViewModel.Instance.OnComponentPropertyChanged -= ComponentOnOnComponentPropertyChanged;
            boundViewModel = null;
        }
    }

    private void ComponentOnOnComponentPropertyChanged(ComponentInstance instance, PropertyClass property)
    {
        if (property == VBProperties.LeftProperty ||
            property == VBProperties.TopProperty ||
            property == VBProperties.WidthProperty ||
            property == VBProperties.HeightProperty)
            return;
        UpdateContent();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromVisualTree(e);
        Detach();
    }

    private void ShowFailure()
    {
        var text = $"{Component!.AddressedName}: {Component.PreviewFailure}\n{Component.Owner.Localization.GetString("Str.Designer.Unmodelled.ViewOnly")}";
        Child = new Border
        {
            BorderThickness = new Thickness(1),
            BorderBrush = Component.ForeColor,
            Background = Component.BackColor,
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, Foreground = Component.ForeColor }
        };
        ToolTip.SetTip(this, text);
        ToolTip.SetShowOnDisabled(this, true);
    }

    private void UpdateContent()
    {
        Child = null;
        ToolTip.SetTip(this, null);
        if (Component != null && Bounds.Width >= 1 && Bounds.Height >= 1)
        {
            if (Component.PreviewFailure is not null)
            {
                ShowFailure();
                return;
            }
            Control? visual = null;
            // A control can refuse while it is created, laid out or drawn, and none of them may take the designer down
            // (#2405): a StatusBar refuses only once it lays out its panels.
            try
            {
                visual = ((ComponentBaseClass)Component.Instance.BaseClass).InstantiateForDesigner(Component.Instance);
                RenderTargetBitmap target = new RenderTargetBitmap(new PixelSize((int)Bounds.Width, (int)Bounds.Height),
                    new Vector(96, 96));
                // A Line draws itself from its endpoints in its container's scale, which only the form knows (#163).
                if (visual is VBLine line)
                    line.Resolve(Component.LineTwipsPerUnit);
                VisualChildren.Add(visual);
                LogicalChildren.Add(visual);

                visual.Width = target.PixelSize.Width;
                visual.Height = target.PixelSize.Height;
                visual.Measure(new Size(target.PixelSize.Width, target.PixelSize.Height));
                visual.Arrange(new Rect(0, 0, target.PixelSize.Width, target.PixelSize.Height));

                this.UpdateLayout();

                MinHeight = visual.MinHeight;
                MinWidth = visual.MinWidth;
                MaxWidth = visual.MaxWidth;
                MaxHeight = visual.MaxHeight;

                TextOptions.SetTextRenderingMode(visual, TextRenderingMode.Alias);
                target.Render(visual);
                Release(visual);
                Child = new Image()
                {
                    Source = target
                };
            }
            catch (Exception ex)
            {
                if (visual != null)
                    Release(visual);
                Log.Error(ex, "Designer preview failed for {Control}", Component.AddressedName);
                Component.PreviewFailure = ex.Message;
                ShowFailure();
            }
        }
    }

    private void Release(Control visual)
    {
        LogicalChildren.Remove(visual);
        VisualChildren.Remove(visual);
    }
}