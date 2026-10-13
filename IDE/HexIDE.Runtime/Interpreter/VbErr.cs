using System.Collections.Generic;
using System.Reflection;

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
public class VbErr : ICSharpProxy, ICSharpPropertyBag
{
    public long Number { get; set; }
    public string Description { get; set; } = "";
    public string Source { get; set; } = "";

    public void Clear()
    {
        Number = 0;
        Description = "";
        Source = "";
    }

    /// <summary>Populate from a trapped runtime error (called by the On Error Resume Next block trap and, later,
    /// the GoTo-handler driver). Source is left as-is — VB6 sets it to the project name, which we don't model.</summary>
    public void Capture(VBRunTimeException ex)
    {
        Number = ex.Error.ErrNo;
        Description = ex.Error.Description ?? "";
    }

    // Err.Raise Number, [Source], [Description], [HelpFile], [HelpContext] — sets the fields then throws.
    public void Raise(long number, string? source = null, string? description = null)
    {
        Number = number;
        if (source != null)
            Source = source;
        Description = description ?? StandardDescription(number);
        throw new VBRunTimeException(new VBStandardError((int)number, Description));
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

    // Known VB6 error number -> its standard description (so `Err.Raise 6` / `Error 6` set "Overflow"); an
    // unknown number falls back to VB6's generic text.
    private static readonly Dictionary<int, string> StandardDescriptions = BuildStandardDescriptions();

    private static string StandardDescription(long number) =>
        StandardDescriptions.TryGetValue((int)number, out var d) ? d : "Application-defined or object-defined error";

    private static Dictionary<int, string> BuildStandardDescriptions()
    {
        var map = new Dictionary<int, string>();
        foreach (var f in typeof(VBStandardError).GetFields(BindingFlags.Public | BindingFlags.Static))
            if (f.GetValue(null) is VBStandardError e)
                map[e.ErrNo] = e.Description;
        return map;
    }
}
