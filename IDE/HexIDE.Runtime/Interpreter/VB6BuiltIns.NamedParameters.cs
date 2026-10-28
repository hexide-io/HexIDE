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

    // #1561 measured positional calls only, and so did #1510 for the byte-string functions. Refuse named calls
    // explicitly until an oracle probe supplies them. #2516, #2523 and #2622 likewise measure only positional calls.
    private static readonly HashSet<string> UnmeasuredNamedArguments = new(StringComparer.OrdinalIgnoreCase)
    { "AscB", "CallByName", "ChrB", "CurDir", "Dir", "FileAttr", "FileDateTime", "FileLen", "FormatDateTime", "FV", "GetAttr", "IMEStatus", "InStrB", "IPmt", "LeftB", "MidB", "NPer", "ObjPtr", "Partition", "Pmt", "PPmt", "PV", "Rate", "RightB" };

    private static readonly Dictionary<string, IntrinsicParameters> NamedParameterTable = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Asc"] = new(["String"], 1),
        ["AscW"] = new(["String"], 1),
        ["Atn"] = new(["Number"], 1),
        // CDec and CVDate take their name where every other C-conversion is "Syntax error" (measured, #1475).
        ["CDec"] = new(["Expression"], 1),
        ["Chr"] = new(["CharCode"], 1),
        ["ChrW"] = new(["CharCode"], 1),
        ["Cos"] = new(["Number"], 1),
        ["CreateObject"] = new(["Class", "ServerName"], 1),
        ["CVDate"] = new(["Expression"], 1),
        ["DateAdd"] = new(["Interval", "Number", "Date"], 3),
        ["DateDiff"] = new(["Interval", "Date1", "Date2", "FirstDayOfWeek", "FirstWeekOfYear"], 3),
        ["DatePart"] = new(["Interval", "Date", "FirstDayOfWeek", "FirstWeekOfYear"], 2),
        ["DateSerial"] = new(["Year", "Month", "Day"], 3),
        ["DateValue"] = new(["Date"], 1),
        // The settings four (measured, #1562): DeleteSetting's section is optional, so `DeleteSetting "App"` deletes the
        // program's every section; SaveSetting's value is Setting, not Value, and GetSetting's default Default.
        ["DeleteSetting"] = new(["AppName", "Section", "Key"], 1),
        ["Day"] = new(["Date"], 1),
        ["Environ"] = new(["Expression"], 1),
        ["EOF"] = new(["FileNumber"], 1),
        // LOF, Loc and Seek take FileNumber by name; another name is "Named argument not found" (measured, #1645).
        ["LOF"] = new(["FileNumber"], 1),
        ["Loc"] = new(["FileNumber"], 1),
        ["Seek"] = new(["FileNumber"], 1),
        // Error's one parameter is optional: Error with no number is the text of the error Err holds (measured, #1560).
        ["Error"] = new(["ErrorNumber"], 0),
        ["Exp"] = new(["Number"], 1),
        ["Filter"] = new(["SourceArray", "Match", "Include", "Compare"], 2),
        ["Format"] = new(["Expression", "Format", "FirstDayOfWeek", "FirstWeekOfYear"], 1),
        ["FreeFile"] = new(["RangeNumber"], 0),
        ["GetAllSettings"] = new(["AppName", "Section"], 2),
        ["GetSetting"] = new(["AppName", "Section", "Key", "Default"], 3),
        ["GetObject"] = new(["Pathname", "Class"], 0),
        // Hex, Oct and Str take `Number` though their type library lists no parameters (measured, #1475).
        ["Hex"] = new(["Number"], 1),
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
        ["Join"] = new(["SourceArray", "Delimiter"], 1),
        ["LCase"] = new(["String"], 1),
        ["Left"] = new(["String", "Length"], 2),
        ["LoadPicture"] = new(["FileName", "Size", "ColorDepth", "X", "Y"], 0),
        ["Log"] = new(["Number"], 1),
        ["LTrim"] = new(["String"], 1),
        ["Mid"] = new(["String", "Start", "Length"], 2),
        ["Minute"] = new(["Time"], 1),
        ["Month"] = new(["Date"], 1),
        ["MonthName"] = new(["Month", "Abbreviate"], 1),
        ["MsgBox"] = new(["Prompt", "Buttons", "Title", "HelpFile", "Context"], 1),
        ["Oct"] = new(["Number"], 1),
        ["QBColor"] = new(["Color"], 1),
        ["Replace"] = new(["Expression", "Find", "Replace", "Start", "Count", "Compare"], 3),
        ["RGB"] = new(["Red", "Green", "Blue"], 3),
        ["Right"] = new(["String", "Length"], 2),
        ["Rnd"] = new(["Number"], 0),
        ["Round"] = new(["Number", "NumDigitsAfterDecimal"], 1),
        ["RTrim"] = new(["String"], 1),
        ["SavePicture"] = new(["Picture", "FileName"], 2),
        ["SaveSetting"] = new(["AppName", "Section", "Key", "Setting"], 4),
        ["Second"] = new(["Time"], 1),
        ["Shell"] = new(["Pathname", "WindowStyle"], 1),
        ["Sin"] = new(["Number"], 1),
        ["Space"] = new(["Number"], 1),
        ["Split"] = new(["Expression", "Delimiter", "Limit", "Compare"], 1),
        ["Sqr"] = new(["Number"], 1),
        ["Str"] = new(["Number"], 1),
        ["String"] = new(["Number", "Character"], 2, OmittedIsSyntaxError: true),
        // LocaleID, not the LCID its documentation names: `StrConv(String:="a", Conversion:=1, LCID:=0)` is "Named argument
        // not found" (measured, #1560).
        ["StrConv"] = new(["String", "Conversion", "LocaleID"], 2),
        ["StrReverse"] = new(["Expression"], 1),
        ["Tan"] = new(["Number"], 1),
        ["TimeSerial"] = new(["Hour", "Minute", "Second"], 3),
        ["TimeValue"] = new(["Time"], 1),
        ["Trim"] = new(["String"], 1),
        ["TypeName"] = new(["VarName"], 1),
        ["UCase"] = new(["String"], 1),
        ["Val"] = new(["String"], 1),
        ["VarType"] = new(["VarName"], 1),
        ["Weekday"] = new(["Date", "FirstDayOfWeek"], 1),
        ["WeekdayName"] = new(["Weekday", "Abbreviate", "FirstDayOfWeek"], 1),
        ["Year"] = new(["Date"], 1),
    };

    // Measured to take no names at all: a named argument to one is VB6's "Syntax error".
    private static readonly HashSet<string> TakesNoNamedArguments = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "CBool", "CByte", "CCur", "CDate", "CDbl", "CInt", "CLng", "CSng", "CStr", "CVar", "CVErr", "Fix", "InStr", "Input",
        "InputB", "Int", "LBound", "Len", "LenB", "Sgn", "StrComp", "UBound",
    };

    // The intrinsics with no parameters, which VB6 refuses a name in three ways (measured, #1475: `Now(X:=1)`).
    private static readonly Dictionary<string, string> NoParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Command"] = "Named arguments not allowed",
        ["Date"] = "Syntax error",
        ["DoEvents"] = "Wrong number of arguments or invalid property assignment",
        // `Erl(X:=1)` and `Erl(1)` alike (measured, #2528).
        ["Erl"] = "Wrong number of arguments or invalid property assignment",
        ["Now"] = "Named arguments not allowed",
        ["Time"] = "Named arguments not allowed",
        ["Timer"] = "Wrong number of arguments or invalid property assignment",
    };

    // A ParamArray from the first parameter: `Array(ArgList:=1)`, `Choose(Index:=1, Choice:="a")` and `Switch(VarExpr:=True)`
    // are "Argument in ParamArray may not be named" (measured; Switch with #1560).
    private static readonly HashSet<string> NamedIsAParamArray = new(StringComparer.OrdinalIgnoreCase) { "Array", "Choose", "Switch" };

    // A blank slot is missing syntax; a supplied Optional Variant can hold Missing as a value (#1512).
    internal sealed class IntrinsicArgumentValues(int capacity) : List<Vb6Value>(capacity)
    {
        internal HashSet<int> Omitted { get; } = new();
        internal long? LenVariableSize { get; set; }

        internal void AddArgument(Vb6Value value, bool omitted)
        {
            if (omitted)
                Omitted.Add(Count);
            Add(value);
        }
    }

    // Known maxima and required positions are checked together (#741, #1512).
    // Too few positional arguments follow the no-names set's Syntax error, or the named table's omission message
    // (measured, #1512). InStr and StrComp need two; the other no-names intrinsics need one.
    private static void CheckIntrinsicArgumentCount(string name, IReadOnlyList<Vb6Value> args)
    {
        // Only known signatures impose a maximum (#741); Array/Choose and unlisted intrinsics stay unlimited.
        int? maximum = NamedParameterTable.TryGetValue(name, out var signature) ? signature.Names.Length
            : TakesNoNamedArguments.Contains(name) ? name.ToUpperInvariant() switch
            {
                "INSTR" => 4,
                // Input with three is "Syntax error", not this check's message (measured, #1645): Input itself refuses it.
                "INPUT" or "INPUTB" => int.MaxValue,
                "STRCOMP" => 3,
                "LBOUND" or "UBOUND" => 2,
                _ => 1,
            } : null;
        if (maximum is { } limit && args.Count > limit)
            throw new VBCompileErrorException("Wrong number of arguments or invalid property assignment");

        int required;
        string message;
        if (TakesNoNamedArguments.Contains(name))
        {
            required = name.Equals("InStr", StringComparison.OrdinalIgnoreCase)
                || name.Equals("StrComp", StringComparison.OrdinalIgnoreCase)
                || name.Equals("Input", StringComparison.OrdinalIgnoreCase)
                || name.Equals("InputB", StringComparison.OrdinalIgnoreCase) ? 2 : 1;
            message = "Syntax error";
        }
        else if (NamedParameterTable.TryGetValue(name, out var parameters))
        {
            required = parameters.Required;
            message = parameters.OmittedIsSyntaxError ? "Syntax error" : "Argument not optional";
        }
        else
            return;

        for (var i = 0; i < required; i++)
            if (i >= args.Count || (args is IntrinsicArgumentValues values && values.Omitted.Contains(i)))
                throw new VBCompileErrorException(message);
    }

    /// <summary>The intrinsics that take named arguments, and their parameters, for NamedParameterParityTests.</summary>
    internal static IReadOnlyDictionary<string, IntrinsicParameters> IntrinsicsTakingNamedArguments => NamedParameterTable;

    /// <summary>Every intrinsic HexIDE implements, for NamedParameterParityTests.</summary>
    internal static IEnumerable<string> RegisteredIntrinsics => Builtins.Keys;

    /// <summary>
    /// Whether named arguments have a declared policy: measured behaviour (#1475), or an explicit refusal pending
    /// measurement (#1561).
    /// </summary>
    internal static bool SaysHowItTakesNames(string name) =>
        NamedParameterTable.ContainsKey(name) || TakesNoNamedArguments.Contains(name) || NamedIsAParamArray.Contains(name)
        || NoParameters.ContainsKey(name) || UnmeasuredNamedArguments.Contains(name);

    /// <summary>
    /// An intrinsic's arguments as positions, named ones placed by name and evaluated in parameter order (#1465). With none
    /// named, the values as they came, and so too for a name that is no intrinsic at all, which nothing runs: its caller
    /// answers it as VB6 does, "Sub or Function not defined".
    /// </summary>
    internal static async Task<List<Vb6Value>> ArrangeIntrinsicArguments(string name, IReadOnlyList<CallArg> args)
    {
        if (NamedArguments.Any(args))
        {
            if (UnmeasuredNamedArguments.Contains(name))
                throw new NotImplementedException($"Named arguments to {name} are not implemented yet");
            if (TakesNoNamedArguments.Contains(name))
                throw new VBCompileErrorException("Syntax error");
            if (NoParameters.TryGetValue(name, out var noParameters))
                throw new VBCompileErrorException(noParameters);
            if (NamedIsAParamArray.Contains(name))
                throw new VBCompileErrorException("Argument in ParamArray may not be named");
            if (NamedParameterTable.TryGetValue(name, out var parameters))
                args = await NamedArguments.Arrange(args, parameters.Names, i => i < parameters.Required,
                    notOptional: parameters.OmittedIsSyntaxError ? "Syntax error" : "Argument not optional");
            // Every registered intrinsic is in a table here (NamedParameterParityTests). One VB6 has and HexIDE does not is
            // answered as it is without names, by its caller.
            else if (Builtins.ContainsKey(name))
                throw new NotImplementedException($"Named arguments to {name} are not implemented yet");
        }
        var values = new IntrinsicArgumentValues(args.Count);
        foreach (var arg in args)
            values.AddArgument(arg.Value, arg.Omitted);
        return values;
    }
}
