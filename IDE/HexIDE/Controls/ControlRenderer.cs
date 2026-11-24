using System;
using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Addins;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.Components;
using HexIDE.VisualDesigner;
using Serilog;
using ComponentInstanceViewModel = HexIDE.VisualDesigner.ComponentInstanceViewModel;

namespace HexIDE.Controls;

public class ControlRenderer : Decorator, IDesignerDrawingSurface
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

    // What draws the controls: what the application's drawer makes for this renderer (#5591), unless a test set its own. The
    // drawer is asked afresh for each picture, so a drawer taken out draws nothing more.
    internal IDesignerPictures? Pictures
    {
        get => pictures ?? AddinDesignerPictureService.For(Application.Current)?.Invoke(this);
        set => pictures = value;
    }

    // The control being drawn. Who may draw here is the grant's to decide (#5591): the drawer runs in this process, and its
    // control sits in the designer's tree, where it could reach the rest. So these checks keep the loan tidy, and aren't the
    // boundary: one control at a time, only a control that isn't somewhere else, only that control taken out, and whatever is
    // still held once a draw returns taken out then.
    private Control? drawing;

    void IDesignerDrawingSurface.Attach(object visual)
    {
        if (visual is not Control control)
            throw new ArgumentException($"The designer draws only controls, not {visual?.GetType().FullName ?? "null"}.", nameof(visual));
        if (drawing is not null)
            throw new ArgumentException("The designer is drawing another control here.", nameof(visual));
        if (control.Parent is not null || control.GetVisualParent() is not null)
            throw new ArgumentException("The control is somewhere already.", nameof(visual));
        drawing = control;
        VisualChildren.Add(control);
        LogicalChildren.Add(control);
    }

    void IDesignerDrawingSurface.Detach(object visual)
    {
        if (drawing is null || !ReferenceEquals(visual, drawing))
            return;
        EndLoan();
    }

    private void EndLoan()
    {
        if (drawing is not { } held)
            return;
        LogicalChildren.Remove(held);
        VisualChildren.Remove(held);
        drawing = null;
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

    private bool updatePending;

    private void UpdateContent()
    {
        if (updatePending)
            return;
        updatePending = true;
        // Bounds changes arrive inside layout. Drawing there prevents the nested layout update from
        // delivering a virtualizing list's viewport, leaving only its first, clipped row (#5121).
        Dispatcher.UIThread.Post(() =>
        {
            updatePending = false;
            DrawContent();
        }, DispatcherPriority.Loaded);
    }

    private void DrawContent()
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
            if (Pictures is not { } drawer)
            {
                // Only when the provider's registration was refused at startup, which is logged then too.
                Log.Error("Designer preview: nothing is registered to draw {Control}", Component.AddressedName);
                return;
            }
            try
            {
                var picture = drawer.Draw(Component.Instance,
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
            finally
            {
                // The loan ends with the draw, so a drawer that left its control here can't block the next one.
                EndLoan();
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