// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors
// HexIDE.VbLspServer — static signature table for VB6 built-in functions
// Used by textDocument/signatureHelp (infrastructure-first pass).

namespace HexIDE.VbLspServer;

internal static class VbSignatures
{
    /// <param name="ParamArray">The last parameter takes any number of arguments where the label doesn't say so, as
    /// <c>Array(ArgList)</c>'s doesn't. A label ending its list in <c>...</c> says so itself.</param>
    internal sealed record SignatureEntry(string Label, string[] Parameters, string? Documentation = null, bool ParamArray = false)
    {
        /// <summary>
        /// Whether every argument past the last parameter is still that parameter, so signature help keeps it active
        /// (#2599). Without a list, an extra argument is a mistake VB6 refuses, and no parameter is active for it.
        /// </summary>
        public bool LastIsList => ParamArray || Label.Contains("...", StringComparison.Ordinal);
    }

    private static readonly Dictionary<string, SignatureEntry[]> _table =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Financial positional surface (#2523); these display names do not enable named runtime calls.
            ["NPer"] = [new SignatureEntry("NPer(Rate, Pmt, PV, [FV], [Type]) As Double", ["Rate", "Pmt", "PV", "FV", "Type"])],
            ["IPmt"] = [new SignatureEntry("IPmt(Rate, Per, NPer, PV, [FV], [Type]) As Double", ["Rate", "Per", "NPer", "PV", "FV", "Type"])],
            ["PPmt"] = [new SignatureEntry("PPmt(Rate, Per, NPer, PV, [FV], [Type]) As Double", ["Rate", "Per", "NPer", "PV", "FV", "Type"])],
            ["Rate"] = [new SignatureEntry("Rate(NPer, Pmt, PV, [FV], [Type], [Guess]) As Double", ["NPer", "Pmt", "PV", "FV", "Type", "Guess"])],
            ["Pmt"] = [new SignatureEntry("Pmt(Rate, NPer, PV, [FV], [Type]) As Double", ["Rate", "NPer", "PV", "FV", "Type"])],
            ["FV"] = [new SignatureEntry("FV(Rate, NPer, Pmt, [PV], [Type]) As Double", ["Rate", "NPer", "Pmt", "PV", "Type"])],
            ["PV"] = [new SignatureEntry("PV(Rate, NPer, Pmt, [FV], [Type]) As Double", ["Rate", "NPer", "Pmt", "FV", "Type"])],
            // Filesystem positional surface (#1561); runtime named arguments await measurement.
            ["CurDir"] = [new SignatureEntry("CurDir([Drive]) As String", ["Drive"])],
            ["Dir"] = [new SignatureEntry("Dir([PathName], [Attributes]) As String", ["PathName", "Attributes"])],
            ["FileLen"] = [new SignatureEntry("FileLen(PathName) As Long", ["PathName"])],
            ["FileDateTime"] = [new SignatureEntry("FileDateTime(PathName) As Date", ["PathName"])],
            ["GetAttr"] = [new SignatureEntry("GetAttr(PathName) As Long", ["PathName"])],
            ["LoadPicture"] = [new SignatureEntry("LoadPicture([FileName], [Size], [ColorDepth], [X], [Y])", ["FileName", "Size", "ColorDepth", "X", "Y"])],
            ["SavePicture"] = [new SignatureEntry("SavePicture(Picture, FileName)", ["Picture", "FileName"])],
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
            ["Filter"] =
            [
                new SignatureEntry("Filter(SourceArray, Match As String, [Include As Boolean = True], [Compare As VbCompareMethod]) As String()",
                    ["SourceArray", "Match", "Include", "Compare"],
                    "Returns the elements of a string array that contain, or do not contain, a substring.")
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
            ["GetSetting"] =
            [
                new SignatureEntry("GetSetting(AppName As String, Section As String, Key As String, [Default]) As String",
                    ["AppName", "Section", "Key", "Default"],
                    "The text a program saved under a key with SaveSetting, or the default where it saved none.")
            ],
            ["GetAllSettings"] =
            [
                new SignatureEntry("GetAllSettings(AppName As String, Section As String)",
                    ["AppName", "Section"],
                    "Every key in a section with its text, as a String array of two columns, or Empty where the section holds none.")
            ],
            ["SaveSetting"] =
            [
                new SignatureEntry("SaveSetting(AppName As String, Section As String, Key As String, Setting As String)",
                    ["AppName", "Section", "Key", "Setting"],
                    "Keeps a piece of text under the program's name, a section and a key, for GetSetting to read in a later run.")
            ],
            ["DeleteSetting"] =
            [
                new SignatureEntry("DeleteSetting(AppName As String, [Section], [Key])",
                    ["AppName", "Section", "Key"],
                    "Deletes one key's text, a whole section, or, with no section, every section the program has.")
            ],
            ["StrComp"] =
            [
                new SignatureEntry("StrComp(String1, String2, [Compare As VbCompareMethod]) As Integer",
                    ["String1", "String2", "Compare"],
                    "Returns a value indicating the result of a string comparison.")
            ],
            ["StrConv"] =
            [
                new SignatureEntry("StrConv(String, Conversion As VbStrConv, [LocaleID As Long]) As String",
                    ["String", "Conversion", "LocaleID"],
                    "Returns a string converted as specified.")
            ],
            ["Error"] =
            [
                new SignatureEntry("Error([ErrorNumber]) As String", ["ErrorNumber"],
                    "Returns the error message that corresponds to a given error number.")
            ],
            ["Format"] =
            [
                new SignatureEntry("Format(Expression, [Format], [FirstDayOfWeek], [FirstWeekOfYear]) As String",
                    ["Expression", "Format", "FirstDayOfWeek", "FirstWeekOfYear"],
                    "Returns a string formatted according to instructions in a format expression.")
            ],

            ["FormatDateTime"] =
            [
                new SignatureEntry("FormatDateTime(Date, [NamedFormat]) As String", ["Date", "NamedFormat"],
                    "Formats a date or time using the program's locale and the selected named format.")
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
            ["CDec"]  = [new SignatureEntry("CDec(Expression) As Variant", ["Expression"], "Converts an expression to a Decimal Variant.")],
            ["CVDate"] = [new SignatureEntry("CVDate(Expression) As Variant", ["Expression"], "Converts an expression to a Date Variant.")],
            ["Val"]   = [new SignatureEntry("Val(String As String) As Double", ["String"], "Reads the number at the start of a string.")],
            ["Str"]   = [new SignatureEntry("Str(Number) As String",   ["Number"], "Converts a number to a string, with a leading space for its sign.")],
            ["Hex"]   = [new SignatureEntry("Hex(Number) As String",   ["Number"], "Converts a number to hexadecimal digits.")],
            ["Oct"]   = [new SignatureEntry("Oct(Number) As String",   ["Number"], "Converts a number to octal digits.")],

            // Math
            ["Abs"]  = [new SignatureEntry("Abs(Number) As Number",  ["Number"], "Returns the absolute value of a number.")],
            ["Int"]  = [new SignatureEntry("Int(Number) As Integer", ["Number"], "Returns the integer portion of a number.")],
            ["Fix"]  = [new SignatureEntry("Fix(Number) As Integer", ["Number"], "Returns the integer portion of a number (truncates toward zero).")],
            ["Round"] = [new SignatureEntry("Round(Number, [NumDigitsAfterDecimal As Long]) As Variant", ["Number", "NumDigitsAfterDecimal"], "Rounds a number to a number of decimal places, a half to the even digit.")],
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
                    "Returns a Variant containing an array.", ParamArray: true)
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
            // Byte strings (#1510): each counts the string's bytes, two to a character, where its plain form counts characters.
            ["ChrB"] = [new SignatureEntry("ChrB(CharCode As Long) As String", ["CharCode"], "Returns a string of the one byte given.")],
            ["AscB"] = [new SignatureEntry("AscB(String As String) As Byte",   ["String"],   "Returns the first byte of a string.")],
            ["LenB"] = [new SignatureEntry("LenB(Expression) As Long",         ["Expression"], "Returns the number of bytes in a string, or a variable's size.")],
            ["LeftB"] = [new SignatureEntry("LeftB(String, Length As Long) As String", ["String", "Length"], "Returns the first bytes of a string.")],
            ["RightB"] = [new SignatureEntry("RightB(String, Length As Long) As String", ["String", "Length"], "Returns the last bytes of a string.")],
            ["MidB"] = [new SignatureEntry("MidB(String, Start As Long, [Length As Long]) As String", ["String", "Start", "Length"],
                "Returns bytes from a string, from the byte at Start.")],
            ["InStrB"] = [new SignatureEntry("InStrB([Start As Long,] String1, String2, [Compare As VbCompareMethod]) As Long",
                ["Start", "String1", "String2", "Compare"], "Returns the byte at which one string first occurs within another.")],

            // Date / Time
            ["Now"]      = [new SignatureEntry("Now() As Date",              [],         "Returns the current date and time.")],
            ["DateValue"] = [new SignatureEntry("DateValue(Date As String) As Date", ["Date"], "Converts a string to the date it names.")],
            ["TimeValue"] = [new SignatureEntry("TimeValue(Time As String) As Date", ["Time"], "Converts a string to the time it names.")],
            ["MonthName"] = [new SignatureEntry("MonthName(Month As Long, [Abbreviate As Boolean = False]) As String", ["Month", "Abbreviate"], "Returns the name of a month, 1 to 12.")],
            ["WeekdayName"] = [new SignatureEntry("WeekdayName(Weekday As Long, [Abbreviate As Boolean = False], [FirstDayOfWeek As VbDayOfWeek]) As String", ["Weekday", "Abbreviate", "FirstDayOfWeek"], "Returns the name of a day of the week.")],
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
            // Descriptive positional label; VB6's parameter name and named calls remain unmeasured (#2622).
            ["ObjPtr"]    = [new SignatureEntry("ObjPtr(object) As Long", ["object"], "Returns an object's identity; Nothing has identity zero. Named arguments are not implemented.")],
            ["IsArray"]   = [new SignatureEntry("IsArray(VarName) As Boolean",    ["VarName"],    "Returns True if the variable is an array.")],
            ["IsDate"]    = [new SignatureEntry("IsDate(Expression) As Boolean",  ["Expression"], "Returns True if the expression can be converted to a date.")],
            ["IsNumeric"] = [new SignatureEntry("IsNumeric(Expression) As Boolean",["Expression"],"Returns True if the expression can be evaluated as a number.")],
            ["IsMissing"] = [new SignatureEntry("IsMissing(ArgName) As Boolean",  ["ArgName"],    "Returns True if an optional argument was not passed.")],
            ["IsError"]   = [new SignatureEntry("IsError(Expression) As Boolean", ["Expression"], "Returns True if the expression is an error value.")],

            // Error handling
            ["Err"]   = [new SignatureEntry("Err.Number / Err.Description / Err.Source", [], "The Err object contains information about runtime errors.")],
            ["CVErr"] = [new SignatureEntry("CVErr(ErrorCode As Long) As Variant", ["ErrorCode"], "Returns a Variant with error subtype containing the specified error code.")],
            ["Erl"]   = [new SignatureEntry("Erl() As Long", [], "The line number at or above where the error being handled was raised; 0 where there is none.")],

            // Object / misc
            ["CallByName"] = [new SignatureEntry("CallByName(Object, ProcName, CallType, Args...)", ["Object", "ProcName", "CallType", "Args"], "Calls a member by name using VbMethod, VbGet, VbLet or VbSet.")],
            ["CreateObject"] = [new SignatureEntry("CreateObject(Class As String, [ServerName As String]) As Object", ["Class", "ServerName"], "Creates and returns a reference to an ActiveX object.")],
            ["GetObject"]    = [new SignatureEntry("GetObject([Pathname], [Class]) As Object", ["Pathname", "Class"], "Returns a reference to an object from a file.")],
            ["Partition"]    = [new SignatureEntry("Partition(Number, Start, Stop, Interval) As String", ["Number", "Start", "Stop", "Interval"], "Returns the range, such as \" 20: 29\", that Number falls in.")],
            ["Shell"]        = [new SignatureEntry("Shell(Pathname As String, [WindowStyle As VbAppWinStyle]) As Double", ["Pathname", "WindowStyle"], "Runs an executable program.")],
            ["Command"]      = [new SignatureEntry("Command() As String", [], "Returns the command line the program was started with.")],
            ["IMEStatus"]    = [new SignatureEntry("IMEStatus() As Long", [], "Returns zero when no input method editor is available.")],
            ["DoEvents"]     = [new SignatureEntry("DoEvents() As Integer", [], "Lets waiting events run before the program goes on.")],
            ["FreeFile"]     = [new SignatureEntry("FreeFile([RangeNumber As Integer]) As Integer", ["RangeNumber"], "Returns the next file number free for Open.")],
            // Positional labels only: FileAttr's named arguments have not been measured (#2521).
            ["FileAttr"]     = [new SignatureEntry("FileAttr(number, [attribute = 1]) As Long", ["number", "attribute"], "Returns an open file's mode. Only attribute 1 is supported; Input mode is unmeasured.")],
            ["EOF"]          = [new SignatureEntry("EOF(FileNumber As Integer) As Boolean", ["FileNumber"], "Returns whether a file has been read to its end.")],
            ["LOF"]          = [new SignatureEntry("LOF(FileNumber As Integer) As Long", ["FileNumber"], "Returns the length of an open file in bytes.")],
            ["Loc"]          = [new SignatureEntry("Loc(FileNumber As Integer) As Long", ["FileNumber"], "Returns the position in an open file, in 128-byte blocks for a sequential one.")],
            ["Seek"]         = [new SignatureEntry("Seek(FileNumber As Integer) As Long", ["FileNumber"], "Returns the byte the next read or write of an open file is at, from 1.")],
            ["Input"]        = [new SignatureEntry("Input(Number As Long, [#]FileNumber As Integer) As String", ["Number", "FileNumber"], "Returns the next characters of a file open for Input.")],
            ["InputB"]       = [new SignatureEntry("InputB(Number As Long, [#]FileNumber As Integer) As String", ["Number", "FileNumber"], "Returns the next bytes of a file open for Input.")],
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
