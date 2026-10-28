using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
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
        // Measured: VarType is LONG, despite every vbXxx code fitting an Integer — the comment here used to
        // say Integer and was wrong. Fixed declared return type, so not the magnitude rule. (#193)
        d["VarType"]   = (_, a, _) => new Vb6Value((long)TypeInfo(a[0]).code);
        d["IsNumeric"] = (_, a, _) => new Vb6Value(IsNumericValue(a[0]));
        d["IsDate"]    = (_, a, _) => new Vb6Value(IsDateValue(a[0]));
        d["IsEmpty"]   = (_, a, _) => new Vb6Value(a[0].Type == VT.EmptyVariant);
        d["IsNull"]    = (_, a, _) => new Vb6Value(a[0].Type == VT.Null);
        d["IsArray"]   = (_, a, _) => new Vb6Value(a[0].Type.IsArray);
        d["IsObject"]  = (_, a, _) => new Vb6Value(IsObjectValue(a[0]));
        d["ObjPtr"]    = (self, a, _) => self.ObjPtr(a);

        // True only for an argument the caller left out, and only where there was nothing to put in its
        // place — an Optional Variant without a default, untyped or explicitly As Variant (#866).
        // Measured; see "IsMissing and the shape of an omitted argument" in docs/vb6-fidelity-oracle/ismissing-and-the-shape-of-an-omitted-argument.md.
        // A ParamArray is missing when the call gave it nothing, so it is empty (measured, #2462).
        d["IsMissing"] = (_, a, _) => new Vb6Value(a[0].IsMissing
            || a[0].Value is VBArray { IsParamArray: true } paramArray && paramArray.UpperBound() < paramArray.LowerBound());

        // An omitted argument is a Variant of subtype vbError, so IsError reports it — measured as True.
        d["IsError"]   = (_, a, _) => new Vb6Value(a[0].IsMissing || a[0].Type == VT.Error);
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
        if (v.Value is DebugProxy) throw new VBCompileErrorException("Syntax error");
        if (!v.Type.IsArray)
        {
            if (v.Value is Control ctrl) return ControlTypeName(ctrl);
            if (v.Value is ICSharpProxy px) return InBoxTypeName(px);
        }
        return TypeInfo(v).name;
    }

    private static string StripVb(string n) => n.StartsWith("VB", StringComparison.Ordinal) ? n.Substring(2) : n;

    /// <summary>A control's class, as TypeName gives it and TypeOf matches it: <c>TextBox</c>, <c>CommonDialog</c>.</summary>
    internal static string ControlTypeName(Control control) => control switch
    {
        // Preserve the old hosted Canvas name: #1631 did not measure a UserControl's TypeName.
        HexIDE.Runtime.BuiltinControls.VBUserControl => nameof(Canvas),
        // A menu's separator is a Menu to a program, as every menu is (#1126).
        HexIDE.Runtime.BuiltinControls.VBMenuSeparator => "Menu",
        // One class draws both scroll bars, which VB6 names by their direction (measured, #1556): it was "ScrollBar", and
        // TypeOf c Is HScrollBar was False.
        HexIDE.Runtime.BuiltinControls.VBScrollBar bar => bar.Orientation == Avalonia.Layout.Orientation.Vertical ? "VScrollBar" : "HScrollBar",
        _ => StripVb(control.GetType().Name),
    };

    // Measured (#840): TypeName(Err) is "ErrObject" and TypeName(App) is "App", which neither class's own name gives.
    private static string InBoxTypeName(ICSharpProxy px) => px switch
    {
        ToolbarItems items => items.TypeName,
        ToolbarItem item => item.TypeName,
        VbErr => "ErrObject",
        VbApp => "App",
        VbScreen => "Screen",
        VbClipboard => "Clipboard",
        VbCollection => "Collection",
        PanelPicture => throw new NotImplementedException("Panel.Picture TypeName is unmeasured (#1087)"),
        StatusBarPanel => "IPanel",
        StatusBarPanels => "IPanels",
        ImageListImages => "IImages",
        ImageListImage => "IImage",
        ImageListPictureObject => "Picture",
        Components.InBox.TreeNodes => "INodes",
        Components.InBox.TreeNode => "INode",
        // A control array is an Object to TypeName: measured on a menu array, `TypeName(mnuLevel)` (#1126).
        HexIDE.Runtime.BuiltinControls.ControlArrayGroup => "Object",
        // So is a form's Controls: measured, `TypeName(Controls)` and `TypeName(Me.Controls)` (#1060).
        FormControls => "Object",
        // TypeName(Forms) is "Object", as a form's Controls is (measured, #2496).
        VbForms => "Object",
        _ => StripVb(px.GetType().Name),
    };

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
        // CommonDialog's TypeName is unmeasured; retain its existing label while correcting VarType.
        VTP.AutomationUnsignedLong => ("Long", 19),
        VTP.Single            => ("Single", 4),
        VTP.Double            => ("Double", 5),
        VTP.Currency          => ("Currency", 6),
        VTP.Decimal           => ("Decimal", 14),
        VTP.Date              => ("Date", 7),
        VTP.String            => ("String", 8),
        VTP.Boolean           => ("Boolean", 11),
        VTP.Null              => ("Null", 1),
        VTP.EmptyVariant      => ("Empty", 0),
        // An omitted argument, which VB6 reports as an Error-subtype Variant — TypeName "Error", VarType
        // vbError (10). Deliberately not "Empty"/0: IsEmpty is False for it, and conflating the two is
        // what makes IsMissing impossible to express.
        VTP.Missing           => ("Error", 10),
        VTP.Error             => ("Error", 10),
        VTP.Nothing           => ("Nothing", 9),
        VTP.Color             => ("Long", 3),   // VB6 has no colour type — a colour is a Long
        VTP.Object            => ("Object", 9),   // Object/class/Collection arrays (measured, #1482)
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
            case byte or int or long or uint or float or double or decimal:
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
        return double.TryParse(WithoutThousandsAfterDigit(s), styles, VbLocale.Current, out _);   // the locale CDbl reads in (#777)
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
            return TryReadDate(s, out _);
        return false;
    }

    private static bool IsObjectValue(Vb6Value v) =>
        v.Type == VT.Control || v.Type == VT.CSharpProxyObject || v.Type == VT.Nothing || v.Type == VT.Object;

    // #2622's native/p-code measurements constrain identities, not addresses (measuring PRs #2624 and #2628).
    // Weak keys neither retain objects nor change Class_Terminate ownership; distinct objects never reuse an identity.
    private readonly ConditionalWeakTable<object, ObjectIdentity> objectIdentities = new();
    private long nextObjectIdentity;
    private sealed record ObjectIdentity(long Number);

    private Vb6Value ObjPtr(IReadOnlyList<Vb6Value> args)
    {
        if (args.Count != 1)
            throw new NotImplementedException("ObjPtr argument counts other than one are unmeasured (#2622)");
        var value = args[0];
        if (!IsObjectValue(value))
        {
            if (value.Type == VT.Integer || value.Type == VT.String)
                throw new VBRunTimeException(VBStandardError.TypeMismatch);
            throw new NotImplementedException("ObjPtr of this non-object type is unmeasured (#2622)");
        }
        if (value.Value is null)
            return new Vb6Value(0L);
        return new Vb6Value(objectIdentities.GetValue(value.Value, _ =>
        {
            if (nextObjectIdentity == int.MaxValue)
                throw new NotImplementedException("ObjPtr identity space exhausted (#2622)");
            return new ObjectIdentity(++nextObjectIdentity);
        }).Number);
    }
}
