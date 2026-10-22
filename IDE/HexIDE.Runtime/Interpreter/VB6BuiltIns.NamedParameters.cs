using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

// The intrinsics' parameter names, for named arguments (#1465). Measured with vb6.exe: for each intrinsic, a project naming
// every parameter (it compiles, or VB6 says "Named argument not found" for a wrong name, or "Syntax error" where the
// intrinsic takes no names at all), and for each, one naming only the required ones and one leaving out each of those in
// turn ("Argument not optional"). The editor's signature help keeps its own table, in the language server; the two are
// checked to agree by NamedParameterParityTests, but this one is what runs.
public partial class VB6BuiltIns
{
    /// <summary>An intrinsic's parameters, in order, and how many of them are required.</summary>
    /// <param name="OmittedIsSyntaxError">String, given one of its two by name, is "Syntax error" (measured) where every
    /// other intrinsic leaving out a required one is "Argument not optional".</param>
    internal sealed record IntrinsicParameters(string[] Names, int Required, bool OmittedIsSyntaxError = false);

    private static readonly Dictionary<string, IntrinsicParameters> NamedParameterTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asc"] = new(["String"], 1),
        ["AscW"] = new(["String"], 1),
        ["Atn"] = new(["Number"], 1),
        ["Chr"] = new(["CharCode"], 1),
        ["ChrW"] = new(["CharCode"], 1),
        ["Cos"] = new(["Number"], 1),
        ["CreateObject"] = new(["Class", "ServerName"], 1),
        ["DateAdd"] = new(["Interval", "Number", "Date"], 3),
        ["DateDiff"] = new(["Interval", "Date1", "Date2", "FirstDayOfWeek", "FirstWeekOfYear"], 3),
        ["DatePart"] = new(["Interval", "Date", "FirstDayOfWeek", "FirstWeekOfYear"], 2),
        ["DateSerial"] = new(["Year", "Month", "Day"], 3),
        ["Day"] = new(["Date"], 1),
        ["Environ"] = new(["Expression"], 1),
        ["Exp"] = new(["Number"], 1),
        ["Format"] = new(["Expression", "Format", "FirstDayOfWeek", "FirstWeekOfYear"], 1),
        ["GetObject"] = new(["Pathname", "Class"], 0),
        ["Hour"] = new(["Time"], 1),
        ["IIf"] = new(["Expression", "TruePart", "FalsePart"], 3),
        ["InputBox"] = new(["Prompt", "Title", "Default", "XPos", "YPos", "HelpFile", "Context"], 1),
        ["InStrRev"] = new(["StringCheck", "StringMatch", "Start", "Compare"], 2),
        ["IsArray"] = new(["VarName"], 1),
        ["IsDate"] = new(["Expression"], 1),
        ["IsEmpty"] = new(["Expression"], 1),
        ["IsError"] = new(["Expression"], 1),
        ["IsMissing"] = new(["ArgName"], 1),
        ["IsNull"] = new(["Expression"], 1),
        ["IsNumeric"] = new(["Expression"], 1),
        ["IsObject"] = new(["Expression"], 1),
        ["LCase"] = new(["String"], 1),
        ["Left"] = new(["String", "Length"], 2),
        ["Log"] = new(["Number"], 1),
        ["LTrim"] = new(["String"], 1),
        ["Mid"] = new(["String", "Start", "Length"], 2),
        ["Minute"] = new(["Time"], 1),
        ["Month"] = new(["Date"], 1),
        ["MsgBox"] = new(["Prompt", "Buttons", "Title", "HelpFile", "Context"], 1),
        ["QBColor"] = new(["Color"], 1),
        ["Replace"] = new(["Expression", "Find", "Replace", "Start", "Count", "Compare"], 3),
        ["RGB"] = new(["Red", "Green", "Blue"], 3),
        ["Right"] = new(["String", "Length"], 2),
        ["Rnd"] = new(["Number"], 0),
        ["RTrim"] = new(["String"], 1),
        ["Second"] = new(["Time"], 1),
        ["Shell"] = new(["Pathname", "WindowStyle"], 1),
        ["Sin"] = new(["Number"], 1),
        ["Space"] = new(["Number"], 1),
        ["Sqr"] = new(["Number"], 1),
        ["String"] = new(["Number", "Character"], 2, OmittedIsSyntaxError: true),
        ["StrReverse"] = new(["Expression"], 1),
        ["Tan"] = new(["Number"], 1),
        ["TimeSerial"] = new(["Hour", "Minute", "Second"], 3),
        ["Trim"] = new(["String"], 1),
        ["TypeName"] = new(["VarName"], 1),
        ["UCase"] = new(["String"], 1),
        ["VarType"] = new(["VarName"], 1),
        ["Weekday"] = new(["Date", "FirstDayOfWeek"], 1),
        ["Year"] = new(["Date"], 1),
    };

    // Measured to take no names at all: a named argument to one is VB6's "Syntax error".
    private static readonly HashSet<string> TakesNoNamedArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "CBool", "CByte", "CCur", "CDate", "CDbl", "CInt", "CLng", "CSng", "CStr", "CVar", "CVErr", "Fix", "InStr", "Int",
        "LBound", "Len", "Sgn", "StrComp", "UBound",
    };

    // A ParamArray from the first parameter: `Array(ArgList:=1)` and `Choose(Index:=1, Choice:="a")` are "Argument in
    // ParamArray may not be named" (measured).
    private static readonly HashSet<string> NamedIsAParamArray = new(StringComparer.OrdinalIgnoreCase) { "Array", "Choose" };

    /// <summary>The intrinsics that take named arguments, and their parameters, for NamedParameterParityTests.</summary>
    internal static IReadOnlyDictionary<string, IntrinsicParameters> IntrinsicsTakingNamedArguments => NamedParameterTable;

    /// <summary>
    /// An intrinsic's arguments as positions, named ones placed by name and evaluated in parameter order (#1465). With none
    /// named, the values as they came, and so too for a name that is no intrinsic at all, which nothing runs: its caller
    /// answers it as VB6 does, "Sub or Function not defined".
    /// </summary>
    internal static async Task<List<Vb6Value>> ArrangeIntrinsicArguments(string name, IReadOnlyList<CallArg> args)
    {
        if (NamedArguments.Any(args))
        {
            if (TakesNoNamedArguments.Contains(name))
                throw new VBCompileErrorException("Syntax error");
            if (NamedIsAParamArray.Contains(name))
                throw new VBCompileErrorException("Argument in ParamArray may not be named");
            if (NamedParameterTable.TryGetValue(name, out var parameters))
                args = await NamedArguments.Arrange(args, parameters.Names, i => i < parameters.Required,
                    notOptional: parameters.OmittedIsSyntaxError ? "Syntax error" : "Argument not optional");
            else if (Builtins.ContainsKey(name) || VbIntrinsicNames.IsIntrinsic(name))
                throw new NotImplementedException($"Named arguments to {name} are not implemented yet (#1475)");
        }
        var values = new List<Vb6Value>(args.Count);
        foreach (var arg in args)
            values.Add(arg.Value);
        return values;
    }
}
