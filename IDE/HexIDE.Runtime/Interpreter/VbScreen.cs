using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What the <c>Screen</c> object reads from the host (#2431): the primary screen's size in device-independent pixels, and the
/// names of the fonts the platform has, in the platform's order.
/// </summary>
public interface IVbDisplay
{
    Avalonia.Size Size { get; }
    IReadOnlyList<string> Fonts { get; }
}

/// <summary>
/// The global <c>Screen</c> object (#2431). Measured with vb6.exe, native and p-code alike (the oracle's "The Screen object"):
/// <list type="bullet">
/// <item><c>Width</c> and <c>Height</c> are Singles, the screen in twips, and <c>TwipsPerPixelX</c> and <c>TwipsPerPixelY</c>
/// Singles of 15. HexIDE's pixel is a device-independent one, 15 twips whatever the display's scaling, so the size is the
/// screen's in those pixels, in whole pixels, times 15.</item>
/// <item><c>MousePointer</c> is an Integer, 0 until set, converted as a control's is: 0 to 15 and 99, rounded to even, and
/// anything else 380. Setting it does not set a form's, and it stays set after the last form unloads.</item>
/// <item><c>ActiveForm</c> is the form last activated, and Nothing, with no error, when no form is. <c>ActiveControl</c> is
/// that form's control with the focus, and Nothing when there is none or no form is active.</item>
/// <item><c>FontCount</c> is an Integer and <c>Fonts(i)</c> a String, "" where <c>i</c> is past either end, the index
/// converted as an Integer: rounded to even, 40000 is 6, "abc" 13 and Null 94. The order is the platform's, not
/// Windows'.</item>
/// <item>Writing a member VB6 does not let a program write is 382, naming it ("'Width' property cannot be set at run time"),
/// and the two TwipsPerPixel members are 383 ("... property is read-only"), as through an Object. Early-bound, VB6 refuses
/// the write at compile time instead ("Can't assign to read-only property"); here it is refused as the statement runs.</item>
/// </list>
/// </summary>
public sealed class VbScreen(BasicInterpreter interpreter, IBasicStandardLibrary host) : ICSharpAsyncProxy, ICSharpPropertyBag
{
    private VBCursorType mousePointer;

    /// <summary>The pointer a program set, which every form shows while it is not 0.</summary>
    public VBCursorType MousePointer => mousePointer;

    // The members a program may read and not write, with the casing VB6's message gives them.
    private static readonly Dictionary<string, string> CannotBeSet = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Width"] = "Width", ["Height"] = "Height", ["FontCount"] = "FontCount",
        ["ActiveForm"] = "ActiveForm", ["ActiveControl"] = "ActiveControl",
    };

    private static readonly Dictionary<string, string> ReadOnly = new(StringComparer.OrdinalIgnoreCase)
    {
        ["TwipsPerPixelX"] = "TwipsPerPixelX", ["TwipsPerPixelY"] = "TwipsPerPixelY",
    };

    void ICSharpProxy.Call(string method, List<Vb6Value> args) =>
        throw new InvalidOperationException("Screen calls must be awaited");

    public Task<Vb6Value?> CallAsync(string method, List<Vb6Value> args) => Task.FromResult<Vb6Value?>(Read(method, args));

    private Vb6Value Read(string member, List<Vb6Value> args)
    {
        var name = member.ToLowerInvariant();
        if (name == "fonts")
            return Font(args);
        if (name == "mouseicon")
            throw new NotImplementedException("Screen.MouseIcon is not implemented (#2431)");
        if (args.Count != 0)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        return name switch
        {
            "width" => Twips(host.Display.Size.Width),
            "height" => Twips(host.Display.Size.Height),
            "twipsperpixelx" or "twipsperpixely" => new Vb6Value((float)VBScaleModeExtensions.PixelToTwips),
            "mousepointer" => new Vb6Value((short)mousePointer),
            "fontcount" => new Vb6Value((short)Math.Min(host.Display.Fonts.Count, short.MaxValue)),
            "activeform" => interpreter.Forms?.ActiveForm is { } form ? Vb6Value.NewObject(form) : Vb6Value.Nothing,
            "activecontrol" => interpreter.Forms?.ActiveControl is { } control ? new Vb6Value(control) : Vb6Value.Nothing,
            _ => throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod),
        };
    }

    private static Vb6Value Twips(double pixels) => new((float)(Math.Floor(pixels) * VBScaleModeExtensions.PixelToTwips));

    private Vb6Value Font(List<Vb6Value> args)
    {
        if (args.Count != 1)
            throw new VBRunTimeException(VBStandardError.WrongNumberOfArgumentsOrInvalidPropertyAssignment);
        var index = Convert.ToInt32(VbNumeric.CoerceOnStore(args[0], Vb6Value.ValueType.Integer, null).Value);
        var fonts = host.Display.Fonts;
        return new Vb6Value(index >= 0 && index < fonts.Count ? fonts[index] : "");
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        value = Read(name, []);
        return true;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (name.Equals("MousePointer", StringComparison.OrdinalIgnoreCase))
        {
            mousePointer = (VBCursorType)AvaloniaInteroperability.EnumValue(typeof(VBCursorType), value);
            interpreter.Forms?.ScreenPointerChanged(mousePointer);
            return true;
        }
        if (CannotBeSet.TryGetValue(name, out var cannot))
            throw new VBRunTimeException(new VBStandardError(VBStandardError.PropertySetCantBeExecutedAtRunTime.ErrNo,
                $"'{cannot}' property cannot be set at run time"));
        if (ReadOnly.TryGetValue(name, out var readOnly))
            throw new VBRunTimeException(new VBStandardError(VBStandardError.PropertySetCantBeUsedWithAReadOnlyProperty.ErrNo,
                $"'{readOnly}' property is read-only"));
        if (name.Equals("MouseIcon", StringComparison.OrdinalIgnoreCase) || name.Equals("Fonts", StringComparison.OrdinalIgnoreCase))
            throw new NotImplementedException($"Writing Screen.{name} is not implemented (#2431)");
        throw new VBRunTimeException(VBStandardError.ObjectDoesntSupportThisPropertyOrMethod);
    }
}
