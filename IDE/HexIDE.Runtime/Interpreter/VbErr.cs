using System.Collections.Generic;
using System.Globalization;

namespace HexIDE.Runtime.Interpreter;

/// <summary>A CSharp object that exposes VB6-style get/set properties by name (e.g. <c>Err.Number</c>). The
/// member read/write sites consult this after the <see cref="ICSharpProxy"/> method path.</summary>
public interface ICSharpPropertyBag
{
    bool TryGetProperty(string name, out Vb6Value value);
    bool TrySetProperty(string name, Vb6Value value);
}

/// <summary>A property write that can wait for a host dialog before the next VB statement runs.</summary>
public interface ICSharpAsyncPropertyBag
{
    ValueTask<bool> TrySetPropertyAsync(string name, Vb6Value value);
}

/// <summary>The global <c>Err</c> object (one per program, VB6-correct). Methods (<c>Raise</c>/<c>Clear</c>)
/// arrive through <see cref="ICSharpProxy.Call"/>; the error properties
/// through <see cref="ICSharpPropertyBag"/>. The block-level error trap re-populates it via <see cref="Capture"/>.
/// </summary>
public class VbErr(Func<string>? projectName = null) : ICSharpProxy, ICSharpPropertyBag
{
    public long Number { get; set; }

    /// <summary>
    /// The error's text. Once anything has set it, a Raise that gives none keeps it, whatever it is, the empty string
    /// included; only <see cref="Clear"/> makes a Raise take the number's standard text again (measured, #877).
    /// </summary>
    public string Description
    {
        get => description;
        set
        {
            description = value;
            descriptionSet = true;
        }
    }

    public string Source { get; set; } = "";
    public string HelpFile { get; set; } = "";
    public long HelpContext { get; set; }

    private string description = "";
    private bool descriptionSet;

    public void Clear()
    {
        Number = 0;
        description = "";
        descriptionSet = false;
        Source = "";
        HelpFile = "";
        HelpContext = 0;
    }

    /// <summary>Populate from a trapped error, keeping its raising project or explicit Raise source (#1483).</summary>
    public void Capture(VBRunTimeException ex)
    {
        Source = ex.VbSource ?? projectName?.Invoke() ?? "";
        Number = ex.Error.ErrNo;
        HelpFile = ex.VbHelpFile ?? "";
        HelpContext = ex.VbHelpContext ?? DefaultHelpContext(Number);
        Description = ex.Error.Description ?? "";
    }

    // Err.Raise Number, [Source], [Description], [HelpFile], [HelpContext] — sets the fields then throws. With no
    // Description it keeps the one already set: Raise 11 then Raise 13 is 13 with "Division by zero" (measured, #877).
    public void Raise(long number, string? source = null, string? description = null,
        string? helpFile = null, long? helpContext = null)
    {
        Number = number;
        if (source != null)
            Source = source;
        else if (Source.Length == 0)
            Source = projectName?.Invoke() ?? "";
        Description = description ?? (descriptionSet ? Description : StandardDescription(number));
        HelpFile = helpFile ?? "";
        HelpContext = helpContext ?? DefaultHelpContext(number);
        throw new VBRunTimeException(new VBStandardError((int)number, Description))
            { VbSource = Source, VbHelpFile = HelpFile, VbHelpContext = HelpContext };
    }

    public void Call(string method, List<Vb6Value> args)
    {
        // Raise's number, source and description are values: an object stands for its default member (measured:
        // `Err.Raise w` raises 42, and a class with none raises 438). Without this an object raised number 0.
        for (var i = 0; i < Math.Min(3, args.Count); i++)
            args[i] = ObjectAsValue.Scalar(args[i], null);
        switch (method.ToLowerInvariant())
        {
            case "clear":
                Clear();
                break;
            case "raise":
                if (args.Count < 1)
                    throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
                Raise(ToLong(args[0]),
                    args.Count >= 2 ? VbDisplay.ToTextOrNull(args[1]) : null,
                    args.Count >= 3 ? VbDisplay.ToTextOrNull(args[2]) : null,
                    args.Count >= 4 && !args[3].IsMissing ? HelpFileValue(args[3]) : null,
                    args.Count >= 5 && !args[4].IsMissing ? HelpContextValue(args[4]) : null);
                break;
            default:
                throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "Err." + method);
        }
    }

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "number":      value = new Vb6Value(Number); return true;     // VB6 Err.Number is a Long
            case "description": value = new Vb6Value(Description); return true;
            case "helpfile":    value = new Vb6Value(HelpFile); return true;
            case "helpcontext": value = new Vb6Value(HelpContext); return true;
            case "lastdllerror": value = new Vb6Value(0L); return true;
            case "source":      value = new Vb6Value(Source); return true;
        }
        value = default;
        return false;
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "number":      Number = ToLong(value); return true;
            case "description": Description = VbDisplay.ToText(value); return true;
            case "helpfile":    HelpFile = HelpFileValue(value); return true;
            case "helpcontext": HelpContext = HelpContextValue(value); return true;
            case "lastdllerror": throw new VBCompileErrorException("Can't assign to read-only property");
            case "source":      Source = VbDisplay.ToText(value); return true;
        }
        return false;
    }

    private static string HelpFileValue(Vb6Value value) => value.Type == Vb6Value.ValueType.String
        ? (string)value.Value!
        : throw new NotImplementedException("Err.HelpFile assignment conversion is not measured (#2527)");

    private static long HelpContextValue(Vb6Value value)
    {
        if (value.Type == Vb6Value.ValueType.String
            && int.TryParse((string)value.Value!, NumberStyles.Integer, VbLocale.Current, out var parsed))
            return parsed;
        if ((value.Type == Vb6Value.ValueType.Integer || value.Type == Vb6Value.ValueType.Long
             || value.Type == Vb6Value.ValueType.Byte || value.Type == Vb6Value.ValueType.Single
             || value.Type == Vb6Value.ValueType.Double || value.Type == Vb6Value.ValueType.Currency)
            && Vb6Value.TryNumericToDouble(value, out var number)
            && number >= int.MinValue && number <= int.MaxValue && number == Math.Truncate(number))
            return (long)number;
        throw new NotImplementedException("Err.HelpContext assignment conversion is not measured (#2527)");
    }

    // #2527: built-in errors use their help topic; application-defined errors use topic 95.
    private static long DefaultHelpContext(long number) =>
        1000000 + (VbErrorMessages.For(number) == VbErrorMessages.ApplicationDefined ? 95 : number);

    private static long ToLong(Vb6Value v) => Vb6Value.TryNumericToDouble(v, out var d) ? (long)d : 0;

    // The text Err.Raise takes when given no Description: the same table Error(n) answers from, measured (#1560). A
    // number HexIDE raises with words of its own, such as 340, is application-defined here, as it is in vb6.exe.
    private static string StandardDescription(long number) => VbErrorMessages.For(number);
}
