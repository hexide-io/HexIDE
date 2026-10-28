using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.Interpreter;

namespace HexIDE.Runtime.BuiltinControls;

// Measured in #1088 and #2373; captions use the host font and visuals use the host theme.
public sealed class VBToolbar : UserControl, ICSharpPropertyBag
{
    protected override Type StyleKeyOverride => typeof(UserControl);

    private readonly Canvas strip = new();

    public VBToolbar()
    {
        Content = strip;
        Background = Brushes.Transparent;
        AddHandler(PointerPressedEvent, Press, RoutingStrategies.Tunnel);
        AddHandler(PointerReleasedEvent, Release, RoutingStrategies.Tunnel);
        Styles.Add(new Style(x => x.Is<Button>().Class("flat").Not(y => y.Class(":pointerover")).Not(y => y.Class(":checked")).Not(y => y.Class(":pressed")))
        {
            Setters = { new Setter(TemplatedControl.BorderThicknessProperty, new Thickness(0)),
                new Setter(TemplatedControl.BackgroundProperty, Brushes.Transparent) }
        });
    }

    private readonly Dictionary<Control, (ToolbarItem Item, bool Arrow)> targets = [];
    private bool suppressRelease;

    private (ToolbarItem? Item, bool Arrow) Target(object? source)
    {
        for (var visual = source as Visual; visual is not null && visual != this; visual = visual.GetVisualParent())
            if (visual is Control control && targets.TryGetValue(control, out var target)) return target;
        return (null, false);
    }

    private void ItemEvent(EventClass kind, ToolbarItem item, Func<bool>? when = null)
    {
        var handlers = ControlEvents.Handlers(this, this.FindLogicalAncestorOfType<IModuleExecutionRoot>(), kind.Name)
            .Where(h => h.Root.Handles(h.Name)).ToList();
        bool? ready = null;
        bool Once() => ready ??= when?.Invoke() ?? true;
        if (handlers.Count == 0) { Once(); return; }
        foreach (var handler in handlers)
            handler.Root.ExecuteSubIf(handler.Name, [new Vb6Value(item)], Once);
    }

    private void Activate(ToolbarItem? item)
    {
        if (item is null) return;
        // Apply the value at the event's turn, after the preceding Click handler.
        ItemEvent(ToolbarComponentClass.ButtonClickEvent, item, () =>
        {
            item.TryGetProperty("Enabled", out var enabled);
            item.TryGetProperty("Style", out var kind);
            item.TryGetProperty("Value", out var value);
            var style = Convert.ToInt64(kind.Value);
            if (!(bool)enabled.Value! || style is 3 or 4 || style == 2 && Convert.ToInt64(value.Value) == 1) return false;
            if (style is 1 or 2) item.TrySetProperty("Value", new Vb6Value(style == 2 || Convert.ToInt64(value.Value) == 0 ? 1L : 0L));
            return true;
        });
    }

    private void Press(object? sender, PointerPressedEventArgs e)
    {
        var (item, arrow) = Target(e.Source);
        var left = e.GetCurrentPoint(this).Properties.IsLeftButtonPressed;
        suppressRelease = left && (arrow || e.ClickCount % 2 == 0);
        e.Handled = suppressRelease;
        if (left && e.ClickCount % 2 == 0) { Activate(item); return; }
        AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseDownEvent);
        if (!left || !arrow || item is null) return;
        ItemEvent(ToolbarComponentClass.ButtonDropDownEvent, item);
        item.TryGetProperty("ButtonMenus", out var menus);
        var flyout = new MenuFlyout();
        foreach (var menu in ((ToolbarItems)menus.Value!).Items)
        {
            if (!menu.IsVisible) continue;
            menu.TryGetProperty("Text", out var text);
            menu.TryGetProperty("Enabled", out var enabled);
            var entry = new MenuItem { Header = text.Value, IsEnabled = (bool)enabled.Value! };
            entry.Click += (_, _) => ItemEvent(ToolbarComponentClass.ButtonMenuClickEvent, menu);
            flyout.Items.Add(entry);
        }
        var anchor = targets.First(t => t.Value.Item == item && !t.Value.Arrow).Key;
        FlyoutBase.SetAttachedFlyout(anchor, flyout);
        flyout.ShowAt(anchor);
    }

    private void Release(object? sender, PointerReleasedEventArgs e)
    {
        e.Handled = suppressRelease;
        if (suppressRelease) { suppressRelease = false; return; }
        AttachedEvents.RaiseMouse(this, e, ComponentBaseClass.MouseUpEvent);
        this.ExecuteSub(ComponentBaseClass.ClickEvent);
        if (e.InitialPressMouseButton == MouseButton.Left) Activate(Target(e.Source).Item);
        RefreshButtons();
    }

    internal void InvalidateImages()
    {
        foreach (var image in strip.GetVisualDescendants().OfType<ToolbarImage>()) image.InvalidateVisual();
    }

    internal void RefreshButtons()
    {
        UpdateButtonSize();
        strip.Children.Clear();
        targets.Clear();
        foreach (var item in Buttons.Items)
        {
            if (!item.IsVisible) continue;
            item.TryGetProperty("Style", out var kind);
            item.TryGetProperty("Caption", out var caption);
            item.TryGetProperty("Value", out var value);
            item.TryGetProperty("Enabled", out var enabled);
            item.TryGetProperty("ToolTipText", out var tip);
            var displayCaption = DisplayCaption((string)caption.Value!);
            object captionContent = displayCaption.Contains('&')
                ? new AccessText { Text = displayCaption.Replace("_", "__").Replace("&", "_"), ShowAccessKey = true }
                : displayCaption;
            ToolbarImage? disabledImage = null;
            Control visual;
            switch (Convert.ToInt64(kind.Value))
            {
                case 3:
                    visual = new Border { Child = new Border { Width = 1, Background = Brushes.Gray, HorizontalAlignment = HorizontalAlignment.Center } };
                    break;
                case 4:
                    visual = new Border { Width = Math.Max(0, item.LayoutWidth / 15) };
                    break;
                case 1: case 2:
                    visual = new ToggleButton { Content = captionContent, IsChecked = Convert.ToInt64(value.Value) == 1 };
                    break;
                case 5:
                    var face = new StackPanel { Orientation = Orientation.Horizontal };
                    face.Children.Add(captionContent as Control ?? new TextBlock { Text = displayCaption });
                    var arrow = new TextBlock { Text = "▾" };
                    face.Children.Add(arrow);
                    targets[arrow] = (item, true);
                    visual = Convert.ToInt64(value.Value) == 1
                        ? new ToggleButton { Content = face, IsChecked = true }
                        : new Button { Content = face };
                    break;
                default:
                    visual = Convert.ToInt64(value.Value) == 1
                        ? new ToggleButton { Content = captionContent, IsChecked = true }
                        : new Button { Content = captionContent };
                    break;
            }
            if (ImageList is not null && visual is Button imageButton)
            {
                var captionVisual = imageButton.Content as Control ?? new TextBlock { Text = displayCaption };
                // Released before the face takes it: replacing the content afterwards would leave the caption in the
                // face with no logical parent, which Avalonia refuses once the bar is in a shown form.
                imageButton.Content = null;
                var face = new Canvas();
                face.SetValue(TextElement.FontFamilyProperty, FontFamily);
                face.SetValue(TextElement.FontSizeProperty, FontSize);
                var image = new ToolbarImage(this, item, imageButton);
                face.Children.Add(image);
                face.Children.Add(captionVisual);
                image.Width = imageButton.Width = Math.Max(0, item.LayoutWidth / 15);
                image.Height = ButtonHeight / 15;
                face.Width = image.Width;
                face.Height = image.Height;
                captionVisual.Measure(Size.Infinity);
                Canvas.SetLeft(captionVisual, TextBelow ? (face.Width - captionVisual.DesiredSize.Width) / 2 : face.Width - captionVisual.DesiredSize.Width - 4);
                Canvas.SetTop(captionVisual, TextBelow ? ImageList.ImageHeight + 7 : (face.Height - captionVisual.DesiredSize.Height) / 2);
                if (!(bool)enabled.Value! && image.SelectedPicture() is not null)
                {
                    // Keep the caption under the disabled theme; its image must not inherit Button opacity.
                    face.Children.Remove(image);
                    image.IsHitTestVisible = false;
                    disabledImage = image;
                }
                imageButton.Content = face;
                imageButton.PointerEntered += (_, _) => image.InvalidateVisual();
                imageButton.PointerExited += (_, _) => image.InvalidateVisual();
            }
            visual.Width = Math.Max(0, item.LayoutWidth / 15);
            visual.Height = ButtonHeight / 15;
            Canvas.SetLeft(visual, Buttons.Left(item) / 15);
            Canvas.SetTop(visual, Buttons.Top(item) / 15);
            if (visual is Button button)
            {
                button.Padding = new Thickness(0);
                button.MinWidth = 0;
                button.MinHeight = 0;
            }
            visual.IsEnabled = (bool)enabled.Value!;
            if (style == 1) visual.Classes.Add("flat");
            if (showTips && (string)tip.Value! != "") ToolTip.SetTip(visual, tip.Value);
            targets[visual] = (item, false);
            strip.Children.Add(visual);
            if (disabledImage is not null)
            {
                Canvas.SetLeft(disabledImage, Buttons.Left(item) / 15);
                Canvas.SetTop(disabledImage, Buttons.Top(item) / 15);
                strip.Children.Add(disabledImage);
            }
        }
        if (HasHadButtons)
        {
            strip.Height = LayoutHeight / 15;
            Height = strip.Height;
        }
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == WidthProperty || change.Property == FontFamilyProperty || change.Property == FontSizeProperty)
            RefreshButtons();
    }

    private long appearance = 1, borderStyle, style, textAlignment, helpContextId, mousePointer, oleDropMode;
    private bool allowCustomize = true, showTips = true, wrappable = true;
    private string helpFile = "";
    internal VBImageList? ImageList;
    internal VBImageList? HotImageList, DisabledImageList;
    internal string? SavedImageListName;
    private ToolbarItems? buttons;

    internal Vb6Value ValidateImage(Vb6Value image)
    {
        if (image.Type == Vb6Value.ValueType.Integer || image.Type == Vb6Value.ValueType.Long)
        {
            var index = ToolbarItems.Number(image);
            if (index == 0) return new Vb6Value(0);
            if (index < 0) throw ToolbarItems.Unmeasured();
            if (ImageList is null) throw ToolbarItems.Error(35613);
            if (index > ImageList.Images.Count) throw ToolbarItems.Error(35600);
            return new Vb6Value((int)index);
        }
        if (image.Type != Vb6Value.ValueType.String || ((string)image.Value!).Length == 0)
            throw ToolbarItems.Unmeasured();
        if (ImageList is null) throw ToolbarItems.Error(35613);
        _ = ImageList.Images.Item(image);
        return image;
    }

    private void BindImages(Vb6Value value)
    {
        if (value.Type == Vb6Value.ValueType.String) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (value.Value is not null && value.Value is not VBImageList)
        {
            if (value.Value is VBPictureBox) throw ToolbarItems.Error(35610);
            throw ToolbarItems.Unmeasured();
        }
        if (value.Value is null && value.Type != Vb6Value.ValueType.Object) throw ToolbarItems.Unmeasured();
        var images = value.Value as VBImageList;
        if (images is not null && images.Images.Count == 0) throw ToolbarItems.Error(35613);
        ValidateImageGeometry(images, TextBelow);
        var resetImages = ImageList is not null || images is null;
        ImageList = images;
        if (resetImages)
            foreach (var item in Buttons.Items) item.Image = new Vb6Value(0);
        if (images is not null) images.BoundToolbars.Add(new WeakReference<VBToolbar>(this));
    }
    public ToolbarItems Buttons => buttons ??= new(this);
    internal bool HasHadButtons;
    internal int LoadedAlign = 1;

    internal bool Flat => style == 1;
    internal bool TextBelow => textAlignment == 0;
    private float buttonWidth = 345.2604f, buttonHeight = 569.7643f;
    internal float ButtonWidth => buttonWidth;
    internal float ButtonHeight => buttonHeight;
    internal bool Wrappable => wrappable;
    internal float DropdownWidth => style == 1 ? 225 : 195.0237f;
    internal float ButtonTop => style == 1 ? 0 : 30;
    internal float LayoutHeight => Buttons.Items.Count == 0 ? lastHeight : Buttons.RowHeight + (style == 1 ? 0 : 60);
    private float lastHeight = 660;

    internal static string DisplayCaption(string caption) => caption.Replace("\t", "").Replace("\r", "").Replace("\n", "");

    // #2373, issuecomment-6445245253: one-line system-font extent, counting '&'.
    internal static double CaptionWidth(string caption, double extent, bool textBelow) =>
        Math.Max(textBelow ? 23 : 43, Math.Ceiling(extent) +
            caption.Count(c => c is '\t' or '\n') * 8 + caption.Count(c => c == '\r') + (textBelow ? 7 : 27));

    private double CaptionExtent(string caption)
    {
        var typeface = new Typeface(FontFamily, FontStyle, FontWeight, FontStretch);
        if (OperatingSystem.IsWindows())
            return ToolbarCaptionMetrics.Measure(DisplayCaption(caption), typeface, FontSize);
        return PortableCaptionExtent(DisplayCaption(caption), typeface, FontSize);
    }

    internal static double PortableCaptionExtent(string caption, Typeface typeface, double size)
    {
        var glyphs = typeface.GlyphTypeface;
        var scale = size / glyphs.Metrics.DesignEmHeight;
        double extent = 0;
        // #2334: whole-pixel advances per glyph, without kerning or shaping; '&' counts.
        foreach (var rune in DisplayCaption(caption).EnumerateRunes())
        {
            glyphs.TryGetHorizontalGlyphAdvance(glyphs.CharacterToGlyphMap.GetGlyph(rune.Value), out var advance);
            extent += Math.Round(advance * scale);
        }
        return extent;
    }

    // #2334 measured square 16/32 px images below, and 16 px images to the right.
    private void ValidateImageGeometry(VBImageList? images, bool below)
    {
        if (images is null || Buttons.Items.Count == 0) return;
        var captioned = Buttons.Items.Any(i => i.IsVisible && i.HasCaptionSpace && i.Caption.Length != 0);
        if (images.ImageWidth != images.ImageHeight || images.ImageWidth is not (16 or 32) ||
            images.ImageWidth == 32 && (!below || !captioned))
            throw ToolbarItems.Unmeasured();
    }

    private void UpdateButtonSize()
    {
        var items = Buttons.Items.Where(i => i.IsVisible && i.HasCaptionSpace).ToArray();
        if (Buttons.Items.Count == 0) return; // Clear retains the last dimensions.
        double widest = 0;
        foreach (var item in items)
        {
            widest = Math.Max(widest, CaptionWidth(item.Caption, CaptionExtent(item.Caption), TextBelow));
        }
        var imageWidth = ImageList?.ImageWidth ?? 16;
        var imageHeight = ImageList?.ImageHeight ?? 16;
        ValidateImageGeometry(ImageList, TextBelow);
        buttonWidth = (float)(Math.Max(widest == 0 ? (TextBelow ? 23 : 43) : widest, imageWidth + 7) * 15);
        buttonHeight = (imageHeight + (TextBelow && items.Any(i => i.Caption.Length != 0) ? 22 : 6)) * 15;
        lastHeight = Buttons.RowHeight + (style == 1 ? 0 : 60);
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "buttons" => new(Buttons),
            "imagelist" => ImageList is null ? Vb6Value.Nothing : new((Control)ImageList),
            "hotimagelist" => HotImageList is null ? Vb6Value.Nothing : new((Control)HotImageList),
            "disabledimagelist" => DisabledImageList is null ? Vb6Value.Nothing : new((Control)DisabledImageList),
            "appearance" => new(appearance),
            "borderstyle" => new(borderStyle),
            "style" => new(style),
            "textalignment" => new(textAlignment),
            "allowcustomize" => new(allowCustomize),
            "showtips" => new(showTips),
            "wrappable" => new(wrappable),
            "enabled" => new(IsEnabled),
            "buttonwidth" => new(ButtonWidth),
            "buttonheight" => new(ButtonHeight),
            "height" => new(LayoutHeight),
            "align" => new(LoadedAlign),
            "helpcontextid" => new(helpContextId),
            "helpfile" => new(helpFile),
            "mousepointer" => new(mousePointer),
            "oledropmode" => new(oleDropMode),
            _ => default,
        };
        if (value != default)
            return true;
        RefusePending(name);
        if (name.Equals("Font", StringComparison.OrdinalIgnoreCase) || name.Equals("TabStop", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        return false;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        var changed = SetProperty(name, value);
        if (changed) RefreshButtons();
        return changed;
    }

    private bool SetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "buttons": throw ToolbarItems.Error(383);
            case "imagelist": BindImages(value); return true;
            case "hotimagelist": case "disabledimagelist":
                if (value.Value is not VBImageList alternate || alternate.Images.Count == 0 ||
                    alternate.ImageWidth != 16 || alternate.ImageHeight != 16 ||
                    ImageList is not { ImageWidth: 16, ImageHeight: 16 })
                    throw ToolbarItems.Unmeasured();
                if (name.Equals("hotimagelist", StringComparison.OrdinalIgnoreCase)) HotImageList = alternate;
                else DisabledImageList = alternate;
                alternate.BoundToolbars.Add(new WeakReference<VBToolbar>(this));
                return true;
            case "style": style = Choice(value, 1); return true;
            case "textalignment":
                var alignment = Choice(value, 1);
                ValidateImageGeometry(ImageList, alignment == 0);
                textAlignment = alignment;
                if (!HasHadButtons) buttonHeight = TextBelow ? 569.7643f : 329.9528f;
                return true;
            case "appearance": appearance = MeasuredChoice(value, 1); return true;
            case "borderstyle": borderStyle = MeasuredChoice(value, 0, 1); return true;
            case "helpcontextid": helpContextId = Long(value); return true;
            case "helpfile":
                if (value.Type != Vb6Value.ValueType.String)
                    throw UnmeasuredConversion();
                helpFile = (string)value.Value!;
                return true;
            case "mousepointer": mousePointer = MeasuredChoice(value, 0); return true;
            case "oledropmode": oleDropMode = MeasuredChoice(value, 0); return true;
            case "allowcustomize": allowCustomize = Boolean(value); return true;
            case "showtips": showTips = Boolean(value); return true;
            case "wrappable": wrappable = Boolean(value); return true;
            case "enabled": IsEnabled = Boolean(value); return true;
            // VB6 accepts these assignments without changing the size, even negative ButtonWidth (measured).
            case "buttonwidth": case "buttonheight": case "height":
                _ = Long(value);
                return true;
            case "align": throw new NotImplementedException("Setting the Toolbar's Align is not implemented (#1088)");
        }
        RefusePending(name);
        if (name.Equals("Font", StringComparison.OrdinalIgnoreCase) || name.Equals("TabStop", StringComparison.OrdinalIgnoreCase))
            throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
        return false;
    }

    private static long Long(Vb6Value value) =>
        value.Type == Vb6Value.ValueType.Integer || value.Type == Vb6Value.ValueType.Long
            ? Convert.ToInt64(value.Value) : throw UnmeasuredConversion();

    private static bool Boolean(Vb6Value value) =>
        value.Type == Vb6Value.ValueType.Boolean ? (bool)value.Value! : throw UnmeasuredConversion();

    private static NotImplementedException UnmeasuredConversion() =>
        new("Toolbar property conversions or enum values beyond the measurements are not implemented (#1088)");

    private static long MeasuredChoice(Vb6Value value, params long[] measured)
    {
        var number = Long(value);
        return measured.Contains(number) ? number : throw UnmeasuredConversion();
    }

    private static long Choice(Vb6Value value, int maximum)
    {
        var number = Long(value);
        return number >= 0 && number <= maximum ? number : throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
    }

    internal static void RefusePending(string name)
    {
        if (ToolbarComponentClass.Instance.Member(name) is { Implemented: false })
            throw new NotImplementedException($"The Toolbar's {name} is not implemented (#1088)");
    }
}
