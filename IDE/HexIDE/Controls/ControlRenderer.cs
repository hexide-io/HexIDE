using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using Serilog;
using ComponentInstanceViewModel = HexIDE.VisualDesigner.ComponentInstanceViewModel;

namespace HexIDE.Controls;

public class ControlRenderer : Decorator, IDesignerDrawingHost
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

    private IDesignerPictures? pictures;

    // What draws the controls. In this process for now, through this renderer, whose place in the tree gives a control its
    // styles and resources.
    internal IDesignerPictures Pictures
    {
        get => pictures ??= new InProcessDesignerPictures(this);
        set => pictures = value;
    }

    void IDesignerDrawingHost.Attach(Control visual)
    {
        VisualChildren.Add(visual);
        LogicalChildren.Add(visual);
    }

    void IDesignerDrawingHost.Detach(Control visual)
    {
        LogicalChildren.Remove(visual);
        VisualChildren.Remove(visual);
    }

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
            // A control can refuse while it is created, laid out or drawn, and none of them may take the designer down
            // (#2405): a StatusBar refuses only once it lays out its panels.
            try
            {
                var picture = Pictures.Draw(Component.Instance,
                    new DesignerDrawRequest((int)Bounds.Width, (int)Bounds.Height, Component.LineTwipsPerUnit));

                MinHeight = picture.MinHeight;
                MinWidth = picture.MinWidth;
                MaxWidth = picture.MaxWidth;
                MaxHeight = picture.MaxHeight;

                Child = new Image()
                {
                    Source = BitmapOf(picture)
                };
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Designer preview failed for {Control}", Component.AddressedName);
                Component.PreviewFailure = ex.Message;
                ShowFailure();
            }
        }
    }

    private static Bitmap BitmapOf(DesignerPicture picture)
    {
        var pin = GCHandle.Alloc(picture.Pixels, GCHandleType.Pinned);
        try
        {
            return new Bitmap(PixelFormat.Bgra8888, AlphaFormat.Premul, pin.AddrOfPinnedObject(),
                new PixelSize(picture.PixelWidth, picture.PixelHeight), new Vector(96, 96), picture.PixelWidth * 4);
        }
        finally
        {
            pin.Free();
        }
    }
}