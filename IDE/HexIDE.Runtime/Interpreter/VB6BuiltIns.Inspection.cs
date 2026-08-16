using System;
using System.Collections.Generic;
using System.Globalization;
using Avalonia.Controls;
using VT = HexIDE.Runtime.Interpreter.Vb6Value.ValueType;
using VTP = HexIDE.Runtime.Interpreter.Vb6Value.ValueTypePrimitive;

namespace HexIDE.Runtime.Interpreter;

// Phase 3 — Inspection / type-test intrinsics. TypeName/VarType map the value model to VB6's names and
// VbVarType codes; the Is* family reports the subtype. All behaviour pinned against vb6.exe — notably:
// IsNumeric(True)=True, IsNumeric(Empty)=True, IsNumeric(Date)=False; IsDate(<number>)=False; and TypeName
// of an array appends "()" while VarType adds vbArray (8192).
public partial class VB6BuiltIns
{
    private static void RegisterInspection(Dictionary<string, BuiltinFn> d)
    {
        d["TypeName"]  = (_, a, _) => new Vb6Value(TypeNameOf(a[0]));
        d["VarType"]   = (_, a, _) => new Vb6Value(TypeInfo(a[0]).code);      // vbXxx code, an Integer
        d["IsNumeric"] = (_, a, _) => new Vb6Value(IsNumericValue(a[0]));
        d["IsDate"]    = (_, a, _) => new Vb6Value(IsDateValue(a[0]));
        d["IsEmpty"]   = (_, a, _) => new Vb6Value(a[0].Type == VT.EmptyVariant);
        d["IsNull"]    = (_, a, _) => new Vb6Value(a[0].Type == VT.Null);
        d["IsArray"]   = (_, a, _) => new Vb6Value(a[0].Type.IsArray);
        d["IsObject"]  = (_, a, _) => new Vb6Value(IsObjectValue(a[0]));
        // No vbError/CVErr subtype exists in the value model yet, so IsError is always False for now.
        d["IsError"]   = (_, a, _) => new Vb6Value(false);
    }

    /// <summary>The debugger's Type-column label for a value — like <see cref="TypeNameOf"/> but names a
    /// user-defined <c>Type</c> instance by its own <c>TypeName</c> (which the intrinsic map would flatten to
    /// "Variant"). Public entry point for the Locals inspector.</summary>
    public static string DebugTypeName(Vb6Value v)
        => v.Value is VbUdt udt ? udt.TypeName : TypeNameOf(v);

    // TypeName returns an object's class name ("TextBox", "Form1", ...); we approximate from the runtime type
    // (best-effort — VB6 returns the declared VB class). Everything else routes through the primitive map.
    private static string TypeNameOf(Vb6Value v)
    {
        if (!v.Type.IsArray)
        {
            if (v.Value is Control ctrl) return StripVb(ctrl.GetType().Name);
            if (v.Value is ICSharpProxy px) return StripVb(px.GetType().Name);
        }
        return TypeInfo(v).name;
    }

    private static string StripVb(string n) => n.StartsWith("VB", StringComparison.Ordinal) ? n.Substring(2) : n;

    // Maps a value to its VB6 TypeName string and VbVarType code. Arrays append "()" / add vbArray (8192).
    private static (string name, int code) TypeInfo(Vb6Value v)
    {
        var t = v.Type;
        if (t.IsArray)
        {
            // An untyped/Variant array's element is stored as EmptyVariant but reads as "Variant()" / vbVariant.
            var (en, ec) = t.Type.Match(_ => ("Variant", 12), p => p == VTP.EmptyVariant ? ("Variant", 12) : FromPrimitive(p));
            return (en + "()", 8192 + ec);
        }
        // A class instance reports its class name; Nothing reports "Nothing" (both VbVarType 9 = vbObject).
        if (t == VT.Object)
            return v.Value is VbObject vo ? (vo.ClassName, 9) : ("Nothing", 9);
        return t.Type.Match(_ => ("Object", 9), FromPrimitive);
    }

    private static (string, int) FromPrimitive(VTP p) => p switch
    {
        VTP.Byte              => ("Byte", 17),
        VTP.Integer           => ("Integer", 2),
        VTP.Long              => ("Long", 3),
        VTP.Single            => ("Single", 4),
        VTP.Double            => ("Double", 5),
        VTP.Currency          => ("Currency", 6),
        VTP.Decimal           => ("Decimal", 14),
        VTP.Date              => ("Date", 7),
        VTP.String            => ("String", 8),
        VTP.Boolean           => ("Boolean", 11),
        VTP.Null              => ("Null", 1),
        VTP.EmptyVariant      => ("Empty", 0),
        VTP.Nothing           => ("Nothing", 9),
        VTP.Color             => ("Long", 3),   // VB6 has no colour type — a colour is a Long
        VTP.Control           => ("Object", 9),
        VTP.CSharpProxyObject => ("Object", 9),
        _                     => ("Variant", 12),
    };

    // IsNumeric: numeric subtypes, Boolean, Empty and colours are numeric; Date, Null, objects and arrays are
    // not; a String is numeric iff it parses (leniently, per VB6 — hex/octal, thousands, accounting signs).
    private static bool IsNumericValue(Vb6Value v)
    {
        switch (v.Value)
        {
            case byte or int or long or float or double or decimal:
            case bool:
                return true;
        }
        if (v.Type == VT.EmptyVariant || v.Type == VT.Color) return true;
        if (v.Type == VT.Date) return false;
        if (v.Value is string s) return IsNumericString(s);
        return false;
    }

    private static bool IsNumericString(string raw)
    {
        var s = raw.Trim();
        if (s.Length == 0) return false;

        // &H hex / &O octal literals are numeric strings.
        if (s.Length > 2 && s[0] == '&')
        {
            char kind = char.ToUpperInvariant(s[1]);
            if (kind == 'H') return AllChars(s, 2, Uri.IsHexDigit);
            if (kind == 'O') return AllChars(s, 2, c => c is >= '0' and <= '7');
            return false;
        }

        // Accounting negative: a fully-parenthesised value; and a single trailing sign.
        if (s.Length >= 2 && s[0] == '(' && s[^1] == ')') s = s[1..^1].Trim();
        if (s.Length >= 1 && (s[^1] == '+' || s[^1] == '-')) s = s[..^1].Trim();
        if (s.Length == 0) return false;

        // VB6 accepts 'D'/'d' as the Double exponent marker; .NET only knows 'E'/'e'.
        s = s.Replace('D', 'E').Replace('d', 'e');

        const NumberStyles styles = NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint
            | NumberStyles.AllowThousands | NumberStyles.AllowExponent
            | NumberStyles.AllowLeadingWhite | NumberStyles.AllowTrailingWhite;
        return double.TryParse(s, styles, CultureInfo.InvariantCulture, out _);
    }

    private static bool AllChars(string s, int from, Func<char, bool> pred)
    {
        for (int i = from; i < s.Length; i++)
            if (!pred(s[i])) return false;
        return s.Length > from;
    }

    // IsDate: a Date value, or a String parseable as a date/time. A bare number is NOT a date (verified),
    // even though CDate would convert it. Empty/Null/objects are not dates.
    private static bool IsDateValue(Vb6Value v)
    {
        if (v.Type == VT.Date) return true;
        if (v.Value is string s)
            return DateTime.TryParse(s, CultureInfo.CurrentCulture, DateTimeStyles.NoCurrentDateDefault, out _);
        return false;
    }

    private static bool IsObjectValue(Vb6Value v) =>
        v.Type == VT.Control || v.Type == VT.CSharpProxyObject || v.Type == VT.Nothing || v.Type == VT.Object;
}
