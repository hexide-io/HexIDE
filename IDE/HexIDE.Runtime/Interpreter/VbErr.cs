using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>A CSharp object that exposes VB6-style get/set properties by name (e.g. <c>Err.Number</c>). The
/// member read/write sites consult this after the <see cref="ICSharpProxy"/> method path.</summary>
public interface ICSharpPropertyBag
{
    bool TryGetProperty(string name, out Vb6Value value);
    bool TrySetProperty(string name, Vb6Value value);
}

/// <summary>The global <c>Err</c> object (one per program, VB6-correct). Methods (<c>Raise</c>/<c>Clear</c>)
/// arrive through <see cref="ICSharpProxy.Call"/>; the <c>Number</c>/<c>Description</c>/<c>Source</c> properties
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

    private string description = "";
    private bool descriptionSet;

    public void Clear()
    {
        Number = 0;
        description = "";
        descriptionSet = false;
        Source = "";
    }

    /// <summary>Populate from a trapped error, keeping its raising project or explicit Raise source (#1483).</summary>
    public void Capture(VBRunTimeException ex)
    {
        Source = ex.VbSource ?? projectName?.Invoke() ?? "";
        Number = ex.Error.ErrNo;
        Description = ex.Error.Description ?? "";
    }

    // Err.Raise Number, [Source], [Description], [HelpFile], [HelpContext] — sets the fields then throws. With no
    // Description it keeps the one already set: Raise 11 then Raise 13 is 13 with "Division by zero" (measured, #877).
    public void Raise(long number, string? source = null, string? description = null)
    {
        Number = number;
        if (source != null)
            Source = source;
        else if (Source.Length == 0)
            Source = projectName?.Invoke() ?? "";
        Description = description ?? (descriptionSet ? Description : StandardDescription(number));
        throw new VBRunTimeException(new VBStandardError((int)number, Description)) { VbSource = Source };
    }

    public void Call(string method, List<Vb6Value> args)
    {
        // Raise's number, source and description are values: an object stands for its default member (measured:
        // `Err.Raise w` raises 42, and a class with none raises 438). Without this an object raised number 0.
        for (var i = 0; i < args.Count; i++)
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
                    args.Count >= 3 ? VbDisplay.ToTextOrNull(args[2]) : null);
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
            case "source":      Source = VbDisplay.ToText(value); return true;
        }
        return false;
    }

    private static long ToLong(Vb6Value v) => Vb6Value.TryNumericToDouble(v, out var d) ? (long)d : 0;

    // The text Err.Raise takes when given no Description: the same table Error(n) answers from, measured (#1560). A
    // number HexIDE raises with words of its own, such as 340, is application-defined here, as it is in vb6.exe.
    private static string StandardDescription(long number) => VbErrorMessages.For(number);
}
