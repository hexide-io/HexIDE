using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HexIDE.Runtime.Interpreter;

// Phase 3 — String intrinsics. 1-based indexing throughout; a Null string argument propagates to Null.
// Split/Join live in the Array group (they involve VBArray). The `$`-typed twins (Left$, Mid$, …) need
// type-hint dispatch and are deferred.
public partial class VB6BuiltIns
{
    private static void RegisterStrings(Dictionary<string, BuiltinFn> d)
    {
        d["Left"]       = (_, a, _) => NullOrStr(a[0], s => Left(s, AsInt(a[1])));
        d["Right"]      = (_, a, _) => NullOrStr(a[0], s => Right(s, AsInt(a[1])));
        d["Mid"]        = (_, a, _) => NullOrStr(a[0], s => Mid(s, AsInt(a[1]), Supplied(a, 2) ? AsInt(a[2]) : (int?)null));
        d["Len"]        = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(AsStr(a[0]).Length);
        d["InStr"]      = (_, a, _) => InStr(a);
        d["InStrRev"]   = (_, a, _) => InStrRev(a);
        d["Replace"]    = (_, a, _) => new Vb6Value(Replace(a));
        d["Trim"]       = (_, a, _) => NullOrStr(a[0], s => s.Trim(' '));
        d["LTrim"]      = (_, a, _) => NullOrStr(a[0], s => s.TrimStart(' '));
        d["RTrim"]      = (_, a, _) => NullOrStr(a[0], s => s.TrimEnd(' '));
        d["UCase"]      = (_, a, _) => NullOrStr(a[0], s => s.ToUpperInvariant());
        d["LCase"]      = (_, a, _) => NullOrStr(a[0], s => s.ToLowerInvariant());
        d["StrReverse"] = (_, a, _) => NullOrStr(a[0], s => { var c = s.ToCharArray(); Array.Reverse(c); return new string(c); });
        d["Space"]      = (_, a, _) => new Vb6Value(new string(' ', Math.Max(0, AsInt(a[0]))));
        d["String"]     = (_, a, _) => new Vb6Value(new string(CharArg(a[1]), Math.Max(0, AsInt(a[0]))));
        d["Chr"]        = (_, a, _) => new Vb6Value(((char)AsInt(a[0])).ToString());
        d["Asc"]        = (_, a, _) => Asc(a[0]);
        d["Str"]        = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(StrFn(a[0]));
        d["Val"]        = (_, a, _) => new Vb6Value(Val(AsStr(a[0])));
    }

    private static Vb6Value NullOrStr(Vb6Value v, Func<string, string> fn) =>
        v.IsNull ? Vb6Value.Null : new Vb6Value(fn(AsStr(v)));

    private static VBRunTimeException InvalidCall() => new(VBStandardError.InvalidProcedureCall);

    private static string Left(string s, int n)
    {
        if (n < 0) throw InvalidCall();
        return n >= s.Length ? s : s.Substring(0, n);
    }

    private static string Right(string s, int n)
    {
        if (n < 0) throw InvalidCall();
        return n >= s.Length ? s : s.Substring(s.Length - n);
    }

    private static string Mid(string s, int start, int? len)
    {
        if (start < 1) throw InvalidCall();
        if (start > s.Length) return "";                    // fixed: was `>=`, so Mid("Test",4) now returns "t"
        int from = start - 1;
        int count = len is { } l ? Math.Min(Math.Max(0, l), s.Length - from) : s.Length - from;
        return s.Substring(from, count);
    }

    /// <summary>The Mid STATEMENT `Mid(target, start[, length]) = replacement`: overwrite characters of
    /// <paramref name="target"/> IN PLACE from 1-based <paramref name="start"/>, replacing at most
    /// <paramref name="length"/> chars and at most <c>replacement.Length</c> chars — the total length never changes.
    /// <paramref name="start"/> outside [1, Len(target)] raises Err 5 (oracle-verified vs vb6.exe). Returns the new
    /// string (the caller writes it back to the target slot).</summary>
    public static Vb6Value MidStatementReplace(Vb6Value target, Vb6Value start, Vb6Value? length, Vb6Value replacement)
    {
        string s = AsStr(target);
        string repl = AsStr(replacement);
        int st = AsInt(start);
        if (st < 1 || st > s.Length) throw InvalidCall();
        int from = st - 1;
        int count = Math.Min(repl.Length, s.Length - from);
        if (length is { } lv) count = Math.Min(count, Math.Max(0, AsInt(lv)));
        if (count <= 0) return new Vb6Value(s);
        var chars = s.ToCharArray();
        for (int i = 0; i < count; i++) chars[from + i] = repl[i];
        return new Vb6Value(new string(chars));
    }

    private static Vb6Value InStr(IReadOnlyList<Vb6Value> a)
    {
        // InStr([start,] string1, string2[, compare]). A leading numeric start is present only with 3+ args.
        int start = 1, i = 0, compare = 0;
        if (a.Count >= 3) { start = AsInt(a[0]); i = 1; }
        if (Supplied(a, 3)) compare = AsInt(a[3]);
        if (a[i].IsNull || a[i + 1].IsNull) return Vb6Value.Null;
        if (start < 1) throw InvalidCall();
        string s1 = AsStr(a[i]), s2 = AsStr(a[i + 1]);
        if (start > s1.Length) return new Vb6Value(0);
        var cmp = compare == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return new Vb6Value(s1.IndexOf(s2, start - 1, cmp) + 1);   // 1-based; 0 when not found
    }

    private static Vb6Value InStrRev(IReadOnlyList<Vb6Value> a)
    {
        // InStrRev(string1, string2[, start[, compare]]) — start defaults to -1 (from the end).
        if (a[0].IsNull || a[1].IsNull) return Vb6Value.Null;
        string s1 = AsStr(a[0]), s2 = AsStr(a[1]);
        int start = Supplied(a, 2) ? AsInt(a[2]) : -1;
        int compare = Supplied(a, 3) ? AsInt(a[3]) : 0;
        var cmp = compare == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        int from = start < 0 ? s1.Length - 1 : Math.Min(start - 1, s1.Length - 1);
        if (from < 0 || s1.Length == 0) return new Vb6Value(s2.Length == 0 ? 0 : 0);
        return new Vb6Value(s1.LastIndexOf(s2, from, cmp) + 1);
    }

    private static string Replace(IReadOnlyList<Vb6Value> a)
    {
        // Replace(expression, find, replace[, start[, count[, compare]]]) — returns from `start` onward.
        string s = AsStr(a[0]), find = AsStr(a[1]), repl = AsStr(a[2]);
        int start = Supplied(a, 3) ? AsInt(a[3]) : 1;
        int count = Supplied(a, 4) ? AsInt(a[4]) : -1;
        int compare = Supplied(a, 5) ? AsInt(a[5]) : 0;
        if (start < 1) throw InvalidCall();
        string sub = start <= s.Length ? s.Substring(start - 1) : "";
        if (find.Length == 0 || count == 0) return sub;
        var cmp = compare == 1 ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        var sb = new StringBuilder();
        int i = 0, done = 0;
        while (i <= sub.Length - find.Length)
        {
            if ((count < 0 || done < count) && string.Compare(sub, i, find, 0, find.Length, cmp) == 0)
            {
                sb.Append(repl);
                i += find.Length;
                done++;
            }
            else
            {
                sb.Append(sub[i]);
                i++;
            }
        }
        sb.Append(sub.Substring(i));
        return sb.ToString();
    }

    private static char CharArg(Vb6Value v) =>
        v.Type == Vb6Value.ValueType.String
            ? (AsStr(v).Length > 0 ? AsStr(v)[0] : throw InvalidCall())
            : (char)AsInt(v);

    private static Vb6Value Asc(Vb6Value v)
    {
        if (v.IsNull) return Vb6Value.Null;
        var s = AsStr(v);
        if (s.Length == 0) throw InvalidCall();
        return new Vb6Value((int)s[0]);
    }

    private static string StrFn(Vb6Value v)
    {
        // Str returns the number as text with a leading space for non-negatives (CStr has none).
        double d = AsDouble(v);
        string s = AsStr(v);
        return d >= 0 ? " " + s : s;
    }

    private static double Val(string s)
    {
        // Leading-numeric parse: ignores embedded whitespace, honours &H/&O, stops at the first non-numeric char.
        s = s.Replace(" ", "").Replace("\t", "").Replace("\n", "").Replace("\r", "");
        if (s.Length == 0) return 0;
        if (s.StartsWith("&H", StringComparison.OrdinalIgnoreCase))
        {
            int j = 2;
            while (j < s.Length && Uri.IsHexDigit(s[j])) j++;
            return j > 2 ? Convert.ToInt64(s.Substring(2, j - 2), 16) : 0;
        }
        if (s.StartsWith("&O", StringComparison.OrdinalIgnoreCase))
        {
            int j = 2;
            while (j < s.Length && s[j] is >= '0' and <= '7') j++;
            return j > 2 ? Convert.ToInt64(s.Substring(2, j - 2), 8) : 0;
        }
        int i = 0;
        if (i < s.Length && (s[i] == '+' || s[i] == '-')) i++;
        while (i < s.Length && char.IsDigit(s[i])) i++;
        if (i < s.Length && s[i] == '.') { i++; while (i < s.Length && char.IsDigit(s[i])) i++; }
        if (i < s.Length && (s[i] == 'e' || s[i] == 'E'))
        {
            int e = i + 1;
            if (e < s.Length && (s[e] == '+' || s[e] == '-')) e++;
            if (e < s.Length && char.IsDigit(s[e])) { i = e; while (i < s.Length && char.IsDigit(s[i])) i++; }
        }
        return double.TryParse(s.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : 0;
    }
}
