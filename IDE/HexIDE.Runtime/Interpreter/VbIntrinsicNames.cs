using System;
using System.Collections.Generic;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// Every intrinsic FUNCTION name VB6 defines — whether or not HexIDE implements it.
///
/// This exists so an unimplemented intrinsic fails LOUDLY instead of silently. Without it, `s = CurDir`
/// finds no procedure and no registry entry, falls through to VB6's implicit-declaration rule, and
/// evaluates to Empty: the program keeps running with a wrong value and the user goes hunting for a bug in
/// their own code. That is the worst outcome the interpreter can produce — worse than refusing to run at
/// all, because nothing announces it. (#191)
///
/// <para>
/// The list is of VB6's surface, NOT of our gaps, and that is deliberate: the registry is consulted first,
/// so implementing a function needs no edit here. It only ever grows if VB6 turns out to have a name this
/// missed.
/// </para>
///
/// <para>
/// A DECLARED variable of the same name still wins — resolution reaches this only when nothing else claims
/// the name, so `Dim Command As String` keeps working. What changes is an UNDECLARED use, which VB6 would
/// have resolved to the intrinsic rather than inventing a variable.
/// </para>
///
/// <para>Derived from the enumeration behind <c>docs/MISSING_LANGUAGE.md</c>.</para>
/// </summary>
internal static class VbIntrinsicNames
{
    private static readonly HashSet<string> Names = new(StringComparer.OrdinalIgnoreCase)
    {
        "Abs", "Array", "Asc", "AscB", "AscW", "Atn", "CallByName", "CBool", "CByte", "CCur", "CDate", "CDbl",
        "CDec", "Choose", "Chr", "ChrB", "ChrW", "CInt", "CLng", "Command", "Cos", "CreateObject", "CSng", "CStr",
        "CurDir", "CVar", "CVDate", "CVErr", "Date", "DateAdd", "DateDiff", "DatePart", "DateSerial", "DateValue",
        "Day", "DDB", "Dir", "DoEvents", "Environ", "EOF", "Erl", "Error", "Exp", "FileAttr", "FileDateTime",
        "FileLen", "Filter", "Fix", "Format", "FormatCurrency", "FormatDateTime", "FormatNumber", "FormatPercent",
        "FreeFile", "FV", "GetAllSettings", "GetAttr", "GetObject", "GetSetting", "Hex", "Hour", "IIf", "IMEStatus",
        "Input", "InputB", "InputBox", "InStr", "InStrB", "InStrRev", "Int", "IPmt", "IRR", "IsArray", "IsDate",
        "IsEmpty", "IsError", "IsMissing", "IsNull", "IsNumeric", "IsObject", "Join", "LBound", "LCase", "Left",
        "LeftB", "Len", "LenB", "LoadPicture", "LoadResData", "LoadResPicture", "LoadResString", "Loc", "LOF",
        "Log", "LTrim", "Mid", "MidB", "Minute", "MIRR", "Month", "MonthName", "MsgBox", "Now", "NPer", "NPV",
        "ObjPtr", "Oct", "Partition", "Pmt", "PPmt", "PV", "QBColor", "Rate", "Replace", "RGB", "Right", "RightB",
        "Rnd", "Round", "RTrim", "Second", "Seek", "Sgn", "Shell", "Sin", "SLN", "Space", "Spc", "Split", "Sqr",
        "Str", "StrComp", "StrConv", "String", "StrPtr", "StrReverse", "Switch", "SYD", "Tab", "Tan", "Time",
        "Timer", "TimeSerial", "TimeValue", "Trim", "TypeName", "UBound", "UCase", "Val", "VarPtr", "VarType",
        "Weekday", "WeekdayName"
    };

    /// <summary>True if <paramref name="name"/> is a VB6 intrinsic function. Callers check the builtin
    /// registry FIRST — a true here means "VB6 has this and we do not".</summary>
    internal static bool IsIntrinsic(string name) => Names.Contains(name);
}
