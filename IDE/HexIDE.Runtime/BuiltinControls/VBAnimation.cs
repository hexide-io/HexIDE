using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Rendering;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.Media;

namespace HexIDE.Runtime.BuiltinControls;

// MSCOMCT2 Animation, groups 1–2 of the VB6 SP6 native/p-code measurements (#1096).
public sealed class VBAnimation : Control, ICSharpPropertyBag, ICustomHitTest
{
    private AnimationClip? clip;
    private WriteableBitmap? frameBitmap;
    private Size? windowSize;
    private readonly DispatcherTimer timer = new();
    private bool autoPlay, center;
    private int backStyle, dropMode;
    private long backColor = unchecked((int)0x8000000F);
    private int endFrame, firstFrame;
    private double repeats;
    private InputElement? pointerParent;
    internal int CurrentFrame { get; private set; }
    internal bool IsPlaying => timer.IsEnabled;
    internal TimeSpan FrameDuration => timer.Interval;
    internal AnimationClip? LoadedClip => clip;

    static VBAnimation()
    {
        // Group 2: MouseDown precedes focus; a double click has no second MouseUp (#1096).
        AttachedEvents.AttachMouse<VBAnimation>(AttachedEvents.DblClick.InPlaceOfMouseDown, suppressSecondMouseUp: true);
        AttachedEvents.AttachFocusEvents<VBAnimation>();
        PointerPressedEvent.AddClassHandler<VBAnimation>((control, e) =>
        {
            if (AttachedEvents.OwnsThePointerEvent(control, e.Source)
                && e.GetCurrentPoint(control).Properties.PointerUpdateKind == PointerUpdateKind.LeftButtonPressed)
                control.Focus(NavigationMethod.Pointer, e.KeyModifiers);
        }, handledEventsToo: true);
        AttachedEvents.AttachClick<VBAnimation>(AttachedEvents.ClickRelease.OverTheControl);
    }

    // The OCX resizes its window without updating the extender's Width and Height (#1096 group 2).
    protected override Size MeasureCore(Size availableSize) => windowSize ?? base.MeasureCore(availableSize);

    protected override void ArrangeCore(Rect finalRect)
    {
        base.ArrangeCore(finalRect);
        if (windowSize is { } size) Bounds = new Rect(finalRect.Position, size);
    }

    public override void Render(DrawingContext context)
    {
        base.Render(context);
        var area = new Rect(Bounds.Size);
        using var clipScope = context.PushClip(area);
        context.FillRectangle(VBColor.FromOle(backColor).ToBrush(), area);
        if (clip is null || frameBitmap is null) return;
        var destination = new Rect(center ? (Bounds.Width - clip.Width) / 2 : 0,
            center ? (Bounds.Height - clip.Height) / 2 : 0, clip.Width, clip.Height);
        context.DrawImage(frameBitmap, new Rect(0, 0, clip.Width, clip.Height), destination);
    }

    private void ShowFrame()
    {
        if (clip is null) return;
        frameBitmap ??= new WriteableBitmap(new PixelSize(clip.Width, clip.Height), new Vector(96, 96),
            PixelFormat.Bgra8888, AlphaFormat.Unpremul);
        var pixels = (byte[])clip.Frames[CurrentFrame].Clone();
        if (backStyle == 0)
        {
            var background = clip.Frames[0];
            for (int i = 0; i < pixels.Length; i += 4)
                if (pixels[i] == background[0] && pixels[i + 1] == background[1] && pixels[i + 2] == background[2])
                    pixels[i + 3] = 0;
        }
        using (var buffer = frameBitmap.Lock())
            for (int row = 0; row < clip.Height; row++)
                Marshal.Copy(pixels, row * clip.Width * 4, buffer.Address + row * buffer.RowBytes, clip.Width * 4);
        InvalidateVisual();
    }

    private void ClearFrame()
    {
        frameBitmap?.Dispose();
        frameBitmap = null;
        InvalidateVisual();
    }

    public bool HitTest(Point point) => new Rect(Bounds.Size).Contains(point);

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        pointerParent = this.GetVisualParent() as InputElement;
        pointerParent?.AddHandler(PointerPressedEvent, BeforePointerFocus, RoutingStrategies.Tunnel);
    }

    private void BeforePointerFocus(object? sender, PointerPressedEventArgs e)
    {
        // FocusManager runs at the source during tunnelling. Defer its focus change until after MouseDown.
        if (AttachedEvents.OwnsThePointerEvent(this, e.Source)) e.Handled = true;
    }

    public VBAnimation()
    {
        Focusable = true;
        Tag = "";
        timer.Tick += (_, _) => AdvanceFrame();
    }

    internal void AdvanceFrame()
    {
        if (clip is null || !IsPlaying) return;
        if (CurrentFrame < endFrame) CurrentFrame++;
        else if (repeats < 0 || repeats > 1)
        {
            if (repeats > 1) repeats--;
            CurrentFrame = firstFrame;
        }
        else timer.Stop();
        ShowFrame();
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        pointerParent?.RemoveHandler(PointerPressedEvent, BeforePointerFocus);
        pointerParent = null;
        timer.Stop();
        base.OnDetachedFromVisualTree(e);
    }

    internal Vb6Value Call(string method, IReadOnlyList<Vb6Value> args)
    {
        switch (method.ToLowerInvariant())
        {
            case "open":
                if (args.Count != 1 || args[0].Type != Vb6Value.ValueType.String) throw Unimplemented("Open arguments");
                Open((string)args[0].Value!);
                break;
            case "play": Play(args); break;
            case "stop":
                if (args.Count != 0) throw Unimplemented("Stop arguments");
                timer.Stop();
                break;
            case "close":
                if (args.Count != 0) throw Unimplemented("Close arguments");
                timer.Stop();
                if (clip is not null)
                {
                    Width = Height = 0;
                    windowSize = new Size(0, 0);
                    InvalidateMeasure();
                }
                ClearFrame();
                clip = null;
                break;
            default: throw Unimplemented(method);
        }
        return Vb6Value.Nothing;
    }

    private void Open(string filename)
    {
        timer.Stop();
        ClearFrame();
        var replacing = clip is not null;
        clip = null;
        if (filename.Length == 0) throw Error(52, "Bad file name or number");
        if (!File.Exists(filename)) throw new VBRunTimeException(VBStandardError.FileNotFound);
        try { clip = AnimationClip.Read(File.ReadAllBytes(filename)); }
        catch (AnimationClipException e)
        {
            throw Error(e.ErrorNumber, e.ErrorNumber == 35752 ? "Unable to open AVI file"
                : "Automation error / The object invoked has disconnected from its clients");
        }
        windowSize = center ? null : new Size(clip.Width, clip.Height);
        InvalidateMeasure();
        CurrentFrame = 0;
        timer.Interval = clip.FrameDuration;
        if (autoPlay)
        {
            if (replacing) Width = Height = 0;
            Play([]);
        }
    }

    private void Play(IReadOnlyList<Vb6Value> args)
    {
        if (clip is null) throw Error(35755, "Must open AVI file first");
        if (args.Count > 3) throw Unimplemented("Play arguments");
        var count = Number(args, 0, -1);
        var start = Number(args, 1, 0);
        var end = Number(args, 2, -1);
        if (start < 0) throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        if (start != Math.Truncate(start) || end != Math.Truncate(end)) throw Unimplemented("fractional frame conversion");
        if (count < 0 && count != -1 || end < -1) throw Unimplemented("Play range");
        timer.Stop();
        firstFrame = (int)Math.Min(start, clip.Frames.Count - 1);
        endFrame = end == -1 ? clip.Frames.Count - 1 : (int)Math.Min(end, clip.Frames.Count - 1);
        CurrentFrame = firstFrame;
        ShowFrame();
        repeats = count;
        if (firstFrame < endFrame) timer.Start();
    }

    private static double Number(IReadOnlyList<Vb6Value> args, int index, double fallback)
    {
        if (index >= args.Count || args[index].IsMissing) return fallback;
        var value = args[index];
        if (value.IsNull) throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (value.Type == Vb6Value.ValueType.String)
        {
            if (!double.TryParse((string)value.Value!, System.Globalization.NumberStyles.Float,
                    VbLocale.Current, out _)) throw new VBRunTimeException(VBStandardError.TypeMismatch);
            throw Unimplemented("numeric String conversion");
        }
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long && value.Type != Vb6Value.ValueType.Single && value.Type != Vb6Value.ValueType.Double)
            throw Unimplemented("numeric conversion");
        return Convert.ToDouble(value.Value, System.Globalization.CultureInfo.InvariantCulture);
    }

    internal void LoadProperties(List<(string Property, Vb6Value Value)> properties)
    {
        foreach (var (property, value) in properties)
            switch (property.ToLowerInvariant())
            {
                case "center": center = Boolean(value); break;
                case "backstyle": backStyle = (int)Number([value], 0, 0); break;
                case "backcolor": backColor = Convert.ToInt64(value.Value); break;
                case "autoplay" or "enabled" or "oledropmode": TrySetProperty(property, value); break;
                case "left": Canvas.SetLeft(this, Convert.ToDouble(value.Value) / 15); break;
                case "top": Canvas.SetTop(this, Convert.ToDouble(value.Value) / 15); break;
                case "width": Width = Convert.ToDouble(value.Value) / 15; break;
                case "height": Height = Convert.ToDouble(value.Value) / 15; break;
                case "tabindex": Avalonia.Input.KeyboardNavigation.SetTabIndex(this, Convert.ToInt32(value.Value)); break;
                case "visible": IsVisible = Boolean(value); break;
                case "_version" or "_extentx" or "_extenty" or "fullwidth" or "fullheight": break;
                default: throw Unimplemented("saved " + property);
            }
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = name.ToLowerInvariant() switch
        {
            "autoplay" => new Vb6Value(autoPlay), "center" => new Vb6Value(center),
            "backstyle" => new Vb6Value((long)backStyle), "enabled" => new Vb6Value(IsEnabled),
            "backcolor" => new Vb6Value(backColor), "oledropmode" => new Vb6Value((long)dropMode),
            "causesvalidation" => new Vb6Value(true), "helpcontextid" => new Vb6Value(0L),
            "hwnd" or "oledrag" or "aboutbox" => throw Unimplemented(name),
            _ => default,
        };
        return name.ToLowerInvariant() is "autoplay" or "center" or "backstyle" or "enabled" or "backcolor"
            or "oledropmode" or "causesvalidation" or "helpcontextid";
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "autoplay": autoPlay = Boolean(value); break;
            case "enabled": IsEnabled = Boolean(value); break;
            case "center" or "backstyle": throw Error(382, "Set not supported at runtime");
            case "hwnd": throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
            case "oledropmode":
                var mode = Number([value], 0, 0);
                if (mode is not (0 or 1)) throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
                dropMode = (int)mode;
                break;
            case "backcolor" or "causesvalidation" or "helpcontextid" or "oledrag" or "aboutbox": throw Unimplemented(name);
            default: return false;
        }
        return true;
    }

    private static bool Boolean(Vb6Value value)
    {
        if (value.Type == Vb6Value.ValueType.Boolean) return (bool)value.Value!;
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long
            && value.Type != Vb6Value.ValueType.Single && value.Type != Vb6Value.ValueType.Double)
            throw Unimplemented("Boolean conversion");
        return Number([value], 0, 0) != 0;
    }
    private static VBRunTimeException Error(int number, string description) => new(new VBStandardError(number, description));
    private static NotImplementedException Unimplemented(string member) => new($"Animation {member} is not implemented (#1096)");
}
