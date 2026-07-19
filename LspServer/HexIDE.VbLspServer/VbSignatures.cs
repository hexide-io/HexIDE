// SPDX-License-Identifier: GPL-3.0-or-later
// Copyright (C) 2026 The HexIDE Authors
// HexIDE.VbLspServer — static signature table for VB6 built-in functions
// Used by textDocument/signatureHelp (infrastructure-first pass).

namespace HexIDE.VbLspServer;

internal static class VbSignatures
{
    internal sealed record SignatureEntry(string Label, string[] Parameters, string? Documentation = null);

    private static readonly Dictionary<string, SignatureEntry[]> _table =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Dialogs / I/O
            ["MsgBox"] =
            [
                new SignatureEntry(
                    "MsgBox(Prompt, [Buttons As VbMsgBoxStyle = vbOKOnly], [Title], [HelpFile], [Context]) As VbMsgBoxResult",
                    ["Prompt", "Buttons", "Title", "HelpFile", "Context"],
                    "Displays a message in a dialog box and returns a value indicating which button was clicked.")
            ],
            ["InputBox"] =
            [
                new SignatureEntry(
                    "InputBox(Prompt, [Title], [Default], [XPos], [YPos], [HelpFile], [Context]) As String",
                    ["Prompt", "Title", "Default", "XPos", "YPos", "HelpFile", "Context"],
                    "Displays a dialog box that prompts the user to enter a value.")
            ],

            // String functions
            ["Len"] =
            [
                new SignatureEntry("Len(Expression) As Long", ["Expression"],
                    "Returns the number of characters in a string.")
            ],
            ["Left"] =
            [
                new SignatureEntry("Left(String, Length As Long) As String", ["String", "Length"],
                    "Returns a specified number of characters from the left side of a string.")
            ],
            ["Right"] =
            [
                new SignatureEntry("Right(String, Length As Long) As String", ["String", "Length"],
                    "Returns a specified number of characters from the right side of a string.")
            ],
            ["Mid"] =
            [
                new SignatureEntry("Mid(String, Start As Long, [Length As Long]) As String",
                    ["String", "Start", "Length"],
                    "Returns a specified number of characters from a string.")
            ],
            ["InStr"] =
            [
                new SignatureEntry("InStr([Start As Long,] String1, String2, [Compare As VbCompareMethod]) As Long",
                    ["Start", "String1", "String2", "Compare"],
                    "Returns the position of the first occurrence of one string within another.")
            ],
            ["InStrRev"] =
            [
                new SignatureEntry("InStrRev(StringCheck, StringMatch, [Start As Long = -1], [Compare As VbCompareMethod]) As Long",
                    ["StringCheck", "StringMatch", "Start", "Compare"],
                    "Returns the position of an occurrence of one string within another, searching from the end.")
            ],
            ["Replace"] =
            [
                new SignatureEntry("Replace(Expression, Find, Replace, [Start As Long = 1], [Count As Long = -1], [Compare As VbCompareMethod]) As String",
                    ["Expression", "Find", "Replace", "Start", "Count", "Compare"],
                    "Returns a string in which a specified substring has been replaced.")
            ],
            ["Split"] =
            [
                new SignatureEntry("Split(Expression, [Delimiter = \" \"], [Limit As Long = -1], [Compare As VbCompareMethod]) As String()",
                    ["Expression", "Delimiter", "Limit", "Compare"],
                    "Returns a zero-based, one-dimensional array of substrings.")
            ],
            ["Join"] =
            [
                new SignatureEntry("Join(SourceArray, [Delimiter = \" \"]) As String",
                    ["SourceArray", "Delimiter"],
                    "Returns a string created by joining the substrings in an array.")
            ],
            ["Trim"] =
            [
                new SignatureEntry("Trim(String) As String", ["String"],
                    "Returns a copy of a string with both leading and trailing spaces removed.")
            ],
            ["LTrim"] =
            [
                new SignatureEntry("LTrim(String) As String", ["String"],
                    "Returns a copy of a string with leading spaces removed.")
            ],
            ["RTrim"] =
            [
                new SignatureEntry("RTrim(String) As String", ["String"],
                    "Returns a copy of a string with trailing spaces removed.")
            ],
            ["UCase"] =
            [
                new SignatureEntry("UCase(String) As String", ["String"],
                    "Returns a string converted to uppercase.")
            ],
            ["LCase"] =
            [
                new SignatureEntry("LCase(String) As String", ["String"],
                    "Returns a string converted to lowercase.")
            ],
            ["Space"] =
            [
                new SignatureEntry("Space(Number As Integer) As String", ["Number"],
                    "Returns a string consisting of the specified number of spaces.")
            ],
            ["String"] =
            [
                new SignatureEntry("String(Number As Long, Character) As String", ["Number", "Character"],
                    "Returns a string made up of a repeated character.")
            ],
            ["StrReverse"] =
            [
                new SignatureEntry("StrReverse(Expression As String) As String", ["Expression"],
                    "Returns a string in which the character order of a specified string is reversed.")
            ],
            ["StrComp"] =
            [
                new SignatureEntry("StrComp(String1, String2, [Compare As VbCompareMethod]) As Integer",
                    ["String1", "String2", "Compare"],
                    "Returns a value indicating the result of a string comparison.")
            ],
            ["Format"] =
            [
                new SignatureEntry("Format(Expression, [Format], [FirstDayOfWeek], [FirstWeekOfYear]) As String",
                    ["Expression", "Format", "FirstDayOfWeek", "FirstWeekOfYear"],
                    "Returns a string formatted according to instructions in a format expression.")
            ],

            // Type conversion
            ["CStr"]  = [new SignatureEntry("CStr(Expression) As String",  ["Expression"], "Converts an expression to String.")],
            ["CInt"]  = [new SignatureEntry("CInt(Expression) As Integer", ["Expression"], "Converts an expression to Integer.")],
            ["CLng"]  = [new SignatureEntry("CLng(Expression) As Long",    ["Expression"], "Converts an expression to Long.")],
            ["CDbl"]  = [new SignatureEntry("CDbl(Expression) As Double",  ["Expression"], "Converts an expression to Double.")],
            ["CSng"]  = [new SignatureEntry("CSng(Expression) As Single",  ["Expression"], "Converts an expression to Single.")],
            ["CBool"] = [new SignatureEntry("CBool(Expression) As Boolean",["Expression"], "Converts an expression to Boolean.")],
            ["CByte"] = [new SignatureEntry("CByte(Expression) As Byte",   ["Expression"], "Converts an expression to Byte.")],
            ["CCur"]  = [new SignatureEntry("CCur(Expression) As Currency",["Expression"], "Converts an expression to Currency.")],
            ["CDate"] = [new SignatureEntry("CDate(Expression) As Date",   ["Expression"], "Converts an expression to Date.")],
            ["CVar"]  = [new SignatureEntry("CVar(Expression) As Variant", ["Expression"], "Converts an expression to Variant.")],
            ["CLDbl"] = [new SignatureEntry("CLDbl(Expression) As Double", ["Expression"], "Converts an expression to Long Double.")],

            // Math
            ["Abs"]  = [new SignatureEntry("Abs(Number) As Number",  ["Number"], "Returns the absolute value of a number.")],
            ["Int"]  = [new SignatureEntry("Int(Number) As Integer", ["Number"], "Returns the integer portion of a number.")],
            ["Fix"]  = [new SignatureEntry("Fix(Number) As Integer", ["Number"], "Returns the integer portion of a number (truncates toward zero).")],
            ["Sqr"]  = [new SignatureEntry("Sqr(Number) As Double",  ["Number"], "Returns the square root of a number.")],
            ["Rnd"]  = [new SignatureEntry("Rnd([Number]) As Single", ["Number"], "Returns a random number between 0 and 1.")],
            ["Sgn"]  = [new SignatureEntry("Sgn(Number) As Integer", ["Number"], "Returns an integer indicating the sign of a number.")],
            ["Log"]  = [new SignatureEntry("Log(Number) As Double",  ["Number"], "Returns the natural logarithm of a number.")],
            ["Exp"]  = [new SignatureEntry("Exp(Number) As Double",  ["Number"], "Returns e raised to the specified power.")],
            ["Sin"]  = [new SignatureEntry("Sin(Number) As Double",  ["Number"], "Returns the sine of an angle.")],
            ["Cos"]  = [new SignatureEntry("Cos(Number) As Double",  ["Number"], "Returns the cosine of an angle.")],
            ["Tan"]  = [new SignatureEntry("Tan(Number) As Double",  ["Number"], "Returns the tangent of an angle.")],
            ["Atn"]  = [new SignatureEntry("Atn(Number) As Double",  ["Number"], "Returns the arctangent of a number.")],

            // Arrays
            ["Array"] =
            [
                new SignatureEntry("Array(ArgList) As Variant", ["ArgList"],
                    "Returns a Variant containing an array.")
            ],
            ["UBound"] =
            [
                new SignatureEntry("UBound(ArrayName, [Dimension As Integer = 1]) As Long",
                    ["ArrayName", "Dimension"],
                    "Returns the largest available subscript for the specified dimension of an array.")
            ],
            ["LBound"] =
            [
                new SignatureEntry("LBound(ArrayName, [Dimension As Integer = 1]) As Long",
                    ["ArrayName", "Dimension"],
                    "Returns the smallest available subscript for the specified dimension of an array.")
            ],

            // Character / ASCII
            ["Chr"]  = [new SignatureEntry("Chr(CharCode As Long) As String",  ["CharCode"], "Returns the character associated with the specified character code.")],
            ["Asc"]  = [new SignatureEntry("Asc(String As String) As Integer", ["String"],   "Returns the character code of the first character in a string.")],
            ["ChrW"] = [new SignatureEntry("ChrW(CharCode As Long) As String", ["CharCode"], "Returns the Unicode character associated with the specified code.")],
            ["AscW"] = [new SignatureEntry("AscW(String As String) As Integer",["String"],   "Returns the Unicode character code of the first character.")],

            // Date / Time
            ["Now"]      = [new SignatureEntry("Now() As Date",              [],         "Returns the current date and time.")],
            ["Date"]     = [new SignatureEntry("Date() As Date",             [],         "Returns the current system date.")],
            ["Time"]     = [new SignatureEntry("Time() As Date",             [],         "Returns the current system time.")],
            ["Timer"]    = [new SignatureEntry("Timer() As Single",          [],         "Returns the number of seconds elapsed since midnight.")],
            ["DateAdd"]  = [new SignatureEntry("DateAdd(Interval, Number, Date) As Date", ["Interval", "Number", "Date"], "Returns a Date to which a specified time interval has been added.")],
            ["DateDiff"] = [new SignatureEntry("DateDiff(Interval, Date1, Date2, [FirstDayOfWeek], [FirstWeekOfYear]) As Long", ["Interval", "Date1", "Date2", "FirstDayOfWeek", "FirstWeekOfYear"], "Returns the difference between two dates.")],
            ["DatePart"] = [new SignatureEntry("DatePart(Interval, Date, [FirstDayOfWeek], [FirstWeekOfYear]) As Integer", ["Interval", "Date", "FirstDayOfWeek", "FirstWeekOfYear"], "Returns the specified part of a given date.")],
            ["DateSerial"] = [new SignatureEntry("DateSerial(Year As Integer, Month As Integer, Day As Integer) As Date", ["Year", "Month", "Day"], "Returns a Date for the specified year, month, and day.")],
            ["TimeSerial"] = [new SignatureEntry("TimeSerial(Hour As Integer, Minute As Integer, Second As Integer) As Date", ["Hour", "Minute", "Second"], "Returns a Date containing a time for a specific hour, minute, and second.")],
            ["Day"]    = [new SignatureEntry("Day(Date) As Integer",    ["Date"],  "Returns the day of the month (1–31).")],
            ["Month"]  = [new SignatureEntry("Month(Date) As Integer",  ["Date"],  "Returns the month (1–12).")],
            ["Year"]   = [new SignatureEntry("Year(Date) As Integer",   ["Date"],  "Returns the year.")],
            ["Hour"]   = [new SignatureEntry("Hour(Time) As Integer",   ["Time"],  "Returns the hour (0–23).")],
            ["Minute"] = [new SignatureEntry("Minute(Time) As Integer", ["Time"],  "Returns the minute (0–59).")],
            ["Second"] = [new SignatureEntry("Second(Time) As Integer", ["Time"],  "Returns the second (0–59).")],
            ["Weekday"] = [new SignatureEntry("Weekday(Date, [FirstDayOfWeek]) As Integer", ["Date", "FirstDayOfWeek"], "Returns the day of the week (1–7).")],

            // Inspection
            ["TypeName"]  = [new SignatureEntry("TypeName(VarName) As String",   ["VarName"], "Returns a String that provides information about a variable.")],
            ["VarType"]   = [new SignatureEntry("VarType(VarName) As VbVarType", ["VarName"], "Returns a value indicating the subtype of a variable.")],
            ["IsNull"]    = [new SignatureEntry("IsNull(Expression) As Boolean",  ["Expression"], "Returns True if the expression evaluates to Null.")],
            ["IsEmpty"]   = [new SignatureEntry("IsEmpty(Expression) As Boolean", ["Expression"], "Returns True if the variable has been initialized.")],
            ["IsObject"]  = [new SignatureEntry("IsObject(Expression) As Boolean",["Expression"], "Returns True if the variable is an object reference.")],
            ["IsArray"]   = [new SignatureEntry("IsArray(VarName) As Boolean",    ["VarName"],    "Returns True if the variable is an array.")],
            ["IsDate"]    = [new SignatureEntry("IsDate(Expression) As Boolean",  ["Expression"], "Returns True if the expression can be converted to a date.")],
            ["IsNumeric"] = [new SignatureEntry("IsNumeric(Expression) As Boolean",["Expression"],"Returns True if the expression can be evaluated as a number.")],
            ["IsMissing"] = [new SignatureEntry("IsMissing(ArgName) As Boolean",  ["ArgName"],    "Returns True if an optional argument was not passed.")],
            ["IsError"]   = [new SignatureEntry("IsError(Expression) As Boolean", ["Expression"], "Returns True if the expression is an error value.")],

            // Error handling
            ["Err"]   = [new SignatureEntry("Err.Number / Err.Description / Err.Source", [], "The Err object contains information about runtime errors.")],
            ["CVErr"] = [new SignatureEntry("CVErr(ErrorCode As Long) As Variant", ["ErrorCode"], "Returns a Variant with error subtype containing the specified error code.")],

            // Object / misc
            ["CreateObject"] = [new SignatureEntry("CreateObject(Class As String, [ServerName As String]) As Object", ["Class", "ServerName"], "Creates and returns a reference to an ActiveX object.")],
            ["GetObject"]    = [new SignatureEntry("GetObject([Pathname], [Class]) As Object", ["Pathname", "Class"], "Returns a reference to an object from a file.")],
            ["Shell"]        = [new SignatureEntry("Shell(Pathname As String, [WindowStyle As VbAppWinStyle]) As Double", ["Pathname", "WindowStyle"], "Runs an executable program.")],
            ["Environ"]      = [new SignatureEntry("Environ(Expression) As String", ["Expression"], "Returns the value of an operating system environment variable.")],
            ["RGB"]          = [new SignatureEntry("RGB(Red As Integer, Green As Integer, Blue As Integer) As Long", ["Red", "Green", "Blue"], "Returns a Long representing an RGB colour value.")],
            ["QBColor"]      = [new SignatureEntry("QBColor(Color As Integer) As Long", ["Color"], "Returns the RGB colour code corresponding to a QBasic colour number.")],
            ["Randomize"]    = [new SignatureEntry("Randomize([Number])", ["Number"], "Initializes the random-number generator.")],
            ["Choose"]       = [new SignatureEntry("Choose(Index As Single, Choice-1, [Choice-2, ...]) As Variant", ["Index", "Choice-1", "Choice-2, ..."], "Selects and returns a value from a list of arguments.")],
            ["Switch"]       = [new SignatureEntry("Switch(Expr-1, Value-1, [Expr-2, Value-2, ...]) As Variant", ["Expr-1", "Value-1", "Expr-2, Value-2, ..."], "Evaluates a list of expressions and returns a value associated with the first true expression.")],
            ["IIf"]          = [new SignatureEntry("IIf(Expression As Boolean, TruePart, FalsePart) As Variant", ["Expression", "TruePart", "FalsePart"], "Returns one of two parts, depending on the evaluation of an expression.")],
        };

    /// <summary>
    /// Returns the signature entries for the given function name, or an empty array.
    /// </summary>
    internal static SignatureEntry[] Get(string functionName)
        => _table.TryGetValue(functionName, out var entries) ? entries : [];

    /// <summary>
    /// Returns all known built-in function names and their primary signature entry.
    /// </summary>
    internal static IEnumerable<(string Name, SignatureEntry Entry)> GetAll()
    {
        foreach (var kv in _table)
            yield return (kv.Key, kv.Value[0]);
    }
}
