using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Avalonia.Controls;
using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.AvaloniaInterop;

// #990: docs/vb6-fidelity-oracle/the-coordinate-system-scalemode-scale-scalex-and-scaley.md.
// The mapping is shared with DesignPositions; a running user scale keeps its unit when the client resizes.
internal sealed class RuntimeScale
{
    private static readonly ConditionalWeakTable<Control, RuntimeScale> scales = new();
    private readonly Control owner;
    private readonly Canvas host;
    private VBScaleMode mode;
    private DesignPositions.Scale mapping;
    private bool missingMapping;

    // Issue #990's measured factor and maintainer decision, 2026-11-18. Compute in double, return Single.
    // Known accepted miss: ScaleX(1, vbInches, vbCentimeters) prints 2.540002, where VB6 prints 2.540003.
    // #4693: a centimetre PictureBox Line's X1 and Y1 read 0.49918103218078613 instead of VB6's
    // 0.499181061983109 (one Single step), also accepted by Desk card q-990-centimetres.
    internal const double TwipsPerCentimetre = 566.928588867188;

    private RuntimeScale(Control owner, Canvas host, int mode, double left, double top, double? width, double? height,
        double clientWidth, double clientHeight)
    {
        this.owner = owner;
        this.host = host;
        this.mode = (VBScaleMode)mode;
        missingMapping = mode == 0 && (width is null or 0 || height is null or 0)
            && !(owner is (Window or VBPictureBox) && width is null && height is null && left == 0 && top == 0);
        mapping = DesignPositions.Scale.Of(mode, left, top, width, height, clientWidth, clientHeight);
        VBProps.SetScaleMode(host, this.mode);
        scales.Add(owner, this);
        scales.Add(host, this);
    }

    public static void Initialize(Window window, Canvas host, FormDefinition form)
    {
        var root = VBLoader.FormComponentOf(form);
        _ = new RuntimeScale(window, host, form.Scale?.Mode ?? 1,
            VBLoader.RawDouble(root, "ScaleLeft") ?? 0, VBLoader.RawDouble(root, "ScaleTop") ?? 0,
            form.Scale?.Width, form.Scale?.Height,
            root.GetPropertyOrDefault(VBProperties.WidthProperty) * VBScaleModeExtensions.PixelToTwips,
            root.GetPropertyOrDefault(VBProperties.HeightProperty) * VBScaleModeExtensions.PixelToTwips);
    }

    public static void Initialize(VBPictureBox box, ComponentInstance component)
    {
        if (box.ChildHost is not { } host) return;
        _ = new RuntimeScale(box, host, VBLoader.RawNumber(component, "ScaleMode") ?? 1,
            VBLoader.RawDouble(component, "ScaleLeft") ?? 0, VBLoader.RawDouble(component, "ScaleTop") ?? 0,
            VBLoader.RawDouble(component, "ScaleWidth"), VBLoader.RawDouble(component, "ScaleHeight"),
            ClientTwips(box, false), ClientTwips(box, true));
    }

    public static void Initialize(VBUserControl control, Canvas host, FormDefinition definition)
    {
        var root = VBLoader.FormComponentOf(definition);
        _ = new RuntimeScale(control, host, definition.Scale?.Mode ?? 1,
            VBLoader.RawDouble(root, "ScaleLeft") ?? 0, VBLoader.RawDouble(root, "ScaleTop") ?? 0,
            definition.Scale?.Width, definition.Scale?.Height,
            control.Width * VBScaleModeExtensions.PixelToTwips, control.Height * VBScaleModeExtensions.PixelToTwips);
    }

    public static RuntimeScale? Of(Control control) => scales.TryGetValue(control, out var scale) ? scale : null;

    private static double ClientTwips(Control control, bool vertical) => (control switch
    {
        Window window => ControlPositions.ClientPixels(window, vertical),
        VBPictureBox box => vertical ? box.Height - box.ClientInset.Top - box.ClientInset.Bottom
            : box.Width - box.ClientInset.Left - box.ClientInset.Right,
        VBUserControl userControl => vertical ? userControl.Height : userControl.Width,
        _ => 0,
    }) * VBScaleModeExtensions.PixelToTwips;

    public double Origin(bool vertical)
    {
        if (mode != VBScaleMode.User) return 0;
        if (missingMapping && owner is VBUserControl)
            throw new NotImplementedException("A saved User scale without extents is not implemented (#4693)");
        return mapping.Origin(vertical);
    }

    public double TwipsPerUnit(bool vertical) => mode == VBScaleMode.User
        ? missingMapping ? throw new NotImplementedException("A saved User scale without extents is not implemented (#4693)")
            : mapping.TwipsPerUnit(vertical)
        : Unit(mode, vertical);

    private double Size(bool vertical) => ClientTwips(owner, vertical) / TwipsPerUnit(vertical);

    internal static double Unit(VBScaleMode mode, bool vertical) => mode switch
    {
        VBScaleMode.Centimiter => TwipsPerCentimetre,
        VBScaleMode.Millimiter => TwipsPerCentimetre / 10,
        _ => vertical ? mode.TwipsPerUnit().Vertical : mode.TwipsPerUnit().Horizontal,
    };

    public static bool TryGet(Control owner, string name, out Vb6Value value)
    {
        value = default;
        if (Of(owner) is not { } scale) return false;
        switch (name.ToUpperInvariant())
        {
            case "SCALEMODE": value = new Vb6Value((int)scale.mode); return true;
            case "SCALELEFT": value = new Vb6Value((float)scale.Origin(false)); return true;
            case "SCALETOP": value = new Vb6Value((float)scale.Origin(true)); return true;
            case "SCALEWIDTH": value = new Vb6Value((float)scale.Size(false)); return true;
            case "SCALEHEIGHT": value = new Vb6Value((float)scale.Size(true)); return true;
            default: return false;
        }
    }

    public static bool TrySet(Control owner, string name, Vb6Value value)
    {
        if (Of(owner) is not { } scale) return false;
        var member = name.ToUpperInvariant();
        if (member == "SCALEMODE")
        {
            var requested = Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);
            if (requested is < 0 or > 7)
                throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
            scale.SetMode((VBScaleMode)requested);
            return true;
        }
        if (member is not ("SCALELEFT" or "SCALETOP" or "SCALEWIDTH" or "SCALEHEIGHT")) return false;
        var number = Single(value);
        if (member is "SCALEWIDTH" or "SCALEHEIGHT" && number == 0)
            throw new VBRunTimeException(VBStandardError.InvalidPropertyValue);
        var left = member == "SCALELEFT" ? number : scale.Origin(false);
        var top = member == "SCALETOP" ? number : scale.Origin(true);
        var width = member == "SCALEWIDTH" ? number : scale.Size(false);
        var height = member == "SCALEHEIGHT" ? number : scale.Size(true);
        scale.SetUser(left, top, width, height);
        return true;
    }

    private void SetMode(VBScaleMode requested)
    {
        if (requested == VBScaleMode.User && mode != VBScaleMode.User)
            mapping = new DesignPositions.Scale("user units", TwipsPerUnit(false), TwipsPerUnit(true), Origin(false), Origin(true));
        if (requested != VBScaleMode.User || mode != VBScaleMode.User)
            missingMapping = false;
        mode = requested;
        VBProps.SetScaleMode(host, mode);
    }

    private void SetUser(double left, double top, double width, double height)
    {
        SetMode(VBScaleMode.User);
        missingMapping = false;
        mapping = DesignPositions.Scale.Of(0, left, top, width, height, ClientTwips(owner, false), ClientTwips(owner, true));
    }

    public static void Statement(Control owner, IReadOnlyList<Vb6Value> points)
    {
        var scale = Of(owner) ?? throw new NotImplementedException("Scale on this object is not implemented (#4693)");
        if (points.Count == 0)
        {
            scale.SetMode(VBScaleMode.Twip);
            return;
        }
        var left = Single(points[0]);
        var top = Single(points[1]);
        var width = (float)(Single(points[2]) - left);
        var height = (float)(Single(points[3]) - top);
        if (width == 0 || height == 0)
            throw new VBRunTimeException(VBStandardError.DivisionByZero);
        scale.SetUser(left, top, width, height);
    }

    public static bool IsConversion(string name) => name.Equals("ScaleX", StringComparison.OrdinalIgnoreCase)
        || name.Equals("ScaleY", StringComparison.OrdinalIgnoreCase);

    public static Vb6Value ConvertUnits(Control owner, string name, IReadOnlyList<Vb6Value> args)
    {
        var scale = Of(owner) ?? throw new NotImplementedException($"{name} on this object is not implemented (#4693)");
        if (args.Count == 0)
            throw new VBCompileErrorException("Argument not optional");
        if (args.Count == 4 || args.Count == 5 && name.Equals("ScaleX", StringComparison.OrdinalIgnoreCase))
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");
        if (args.Count > 3)
            throw new NotImplementedException($"{name} with this argument count is not implemented (#4693)");
        // #1474: ScaleX on a PictureBox refuses an optional gap, including a positional blank.
        if (owner is VBPictureBox && name.Equals("ScaleX", StringComparison.OrdinalIgnoreCase)
            && args.Count > 2 && args[1].IsMissing)
            throw new VBRunTimeException(VBStandardError.NamedArgumentNotFound);
        var vertical = name.Equals("ScaleY", StringComparison.OrdinalIgnoreCase);
        var from = args.Count > 1 && !args[1].IsMissing ? Mode(args[1]) : 8;
        var to = args.Count > 2 && !args[2].IsMissing ? Mode(args[2]) : (int)scale.mode;
        double Factor(int unit) => unit switch
        {
            0 when scale.mode == VBScaleMode.User => scale.TwipsPerUnit(vertical),
            0 => throw new VBRunTimeException(VBStandardError.InvalidProcedureCall),
            // The oracle's measured HiMetric factor is Single 0.566929161548615 (#990);
            // using the double ratio instead misses #4693's ScaleX(1440) = 816.378 row.
            8 => (float)(1440d / 2540),
            _ => Unit((VBScaleMode)unit, vertical),
        };
        return new Vb6Value((float)(Single(args[0]) * Factor(from) / Factor(to)));
    }

    private static int Mode(Vb6Value value)
    {
        var mode = Convert.ToInt32(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Integer, null).Value);
        if (mode is < 0 or > 8)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        return mode;
    }

    private static float Single(Vb6Value value) =>
        Convert.ToSingle(VbNumeric.CoerceOnStore(value, Vb6Value.ValueType.Single, null).Value);
}
