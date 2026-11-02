using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

// Phase 3 — String intrinsics. 1-based indexing throughout; a Null string argument propagates to Null.
// Split/Join live in the Array group (they involve VBArray). The `$`-typed twins (Left$, Mid$, …) are these
// functions with the result made a String, which ExpressionExecutor applies at the call (#1071).
public partial class VB6BuiltIns
{
    private static void RegisterStrings(Dictionary<string, BuiltinFn> d)
    {
        d["Left"]       = (_, a, _) => NullOrStr(a[0], s => Left(s, AsInt(a[1])));
        d["Right"]      = (_, a, _) => NullOrStr(a[0], s => Right(s, AsInt(a[1])));
        d["Mid"]        = (_, a, _) => NullOrStr(a[0], s => Mid(s, AsInt(a[1]), Supplied(a, 2) ? AsInt(a[2]) : (int?)null));
        d["Len"]        = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(a is IntrinsicArgumentValues { LenVariableSize: { } size } ? size : (long)AsStr(a[0]).Length);   // Long — see InStr
        d["InStr"]      = (_, a, text) => InStr(a, text);
        d["LenB"]       = (_, a, _) => LenB(a);
        d["AscB"]       = (_, a, _) => AscB(a[0]);
        d["InStrB"]     = (_, a, _) => InStrB(a);
        d["MidB"]       = (_, a, _) => NullOrStr(a[0], s => MidB(s, AsInt(a[1]), Supplied(a, 2) ? AsInt(a[2]) : null));
        d["LeftB"]      = (_, a, _) => NullOrStr(a[0], s => LeftB(s, AsInt(a[1])));
        d["RightB"]     = (_, a, _) => NullOrStr(a[0], s => RightB(s, AsInt(a[1])));
        d["ChrB"]       = (_, _, _) => throw OddByteString("ChrB");
        d["InStrRev"]   = (_, a, text) => InStrRev(a, text);
        d["Replace"]    = (_, a, text) => new Vb6Value(Replace(a, text));
        d["Trim"]       = (_, a, _) => NullOrStr(a[0], s => s.Trim(' '));
        d["LTrim"]      = (_, a, _) => NullOrStr(a[0], s => s.TrimStart(' '));
        d["RTrim"]      = (_, a, _) => NullOrStr(a[0], s => s.TrimEnd(' '));
        d["UCase"]      = (_, a, _) => NullOrStr(a[0], VbCase.Upper);
        d["LCase"]      = (_, a, _) => NullOrStr(a[0], VbCase.Lower);
        d["StrReverse"] = (_, a, _) => NullOrStr(a[0], s => { var c = s.ToCharArray(); Array.Reverse(c); return new string(c); });
        d["Space"]      = (_, a, _) => new Vb6Value(new string(' ', Math.Max(0, AsInt(a[0]))));
        d["String"]     = (_, a, _) => new Vb6Value(new string(CharArg(a[1]), Math.Max(0, AsInt(a[0]))));
        d["Chr"]        = (_, a, _) => new Vb6Value(ChrInCodePage(CharCode(a[0]), AnsiText()));
        d["Asc"]        = (_, a, _) => new Vb6Value(AscInCodePage(FirstCharacterOf(a[0]), AnsiText()));
        d["ChrW"]       = (_, a, _) => ChrW(a[0]);
        d["AscW"]       = (_, a, _) => new Vb6Value((int)(short)FirstCharacterOf(a[0]));
        d["Str"]        = (_, a, _) => a[0].IsNull ? Vb6Value.Null : new Vb6Value(StrFn(a[0]));
        d["Val"]        = (_, a, _) => new Vb6Value(Val(AsStr(a[0])));
        d["StrComp"]    = (_, a, text) => StrComp(a, text);
        d["StrConv"]    = (_, a, _) => StrConv(a);
    }

    // StrComp(string1, string2[, compare]): -1, 0 or 1 as an Integer, or Null where either string is Null. Measured (#1560):
    // the compare is read first, as CLng reads it (0.5 is 0 and 1.5 is 2), so a Null one is 94 whatever the strings are; 0
    // is binary and 1 text (VbTextCompare). Validation follows the Null/empty paths below (#881). An array is 13 even beside a Null,
    // and anything else read as & reads it, so StrComp(5, "5") is 0. Given no compare, vb6.exe takes the calling module's
    // Option Compare (measured, #1572).
    private static Vb6Value StrComp(IReadOnlyList<Vb6Value> a, bool compareText)
    {
        var compare = Supplied(a, 2) ? CompareArgument(a[2]) : (compareText ? 1 : 0);
        var first = StrCompOperand(a[0]);
        var second = StrCompOperand(a[1]);
        if (first is null || second is null)
            return Vb6Value.Null;
        // 2 is refused even for empty strings; other modes need two non-empty strings (#881).
        if (compare == 2) throw InvalidCall();
        var text = first.Length > 0 && second.Length > 0 && IsTextCompare(compare);
        return new Vb6Value(text ? VbTextCompare.Compare(first, second) : Math.Sign(string.CompareOrdinal(first, second)));
    }

    private static string? StrCompOperand(Vb6Value v) =>
        v.Value is VBArray ? throw new VBRunTimeException(VBStandardError.TypeMismatch) : v.IsNull ? null : AsStr(v);

    /// <summary>
    /// StrConv(string, conversion[, LCID]). Measured (#1560), native and p-code alike:
    /// <list type="bullet">
    /// <item>The conversion is read first, as CLng reads it (1.5 is 2), a Null one being 94, and checked before the string:
    /// 1 is upper case, 2 lower and 3 proper, and 0 leaves the string alone. 4 to 32 (wide, narrow, katakana, hiragana)
    /// are 5 on a Western system, and so are 256, 512, and 64 or 128 with anything else. 64 (vbUnicode) and 128
    /// (vbFromUnicode) alone make a byte string, which HexIDE does not have (#1510), so they are refused.</item>
    /// <item>An LCID Windows does not know is 5, and a Null one 94. A known one, even Turkish, changes nothing.</item>
    /// <item>A Null string is Null and an array 13; anything else is read as &amp; reads it, so True is "TRUE" in upper
    /// case.</item>
    /// <item>The case goes through the ANSI code page: a character it lacks becomes its best fit, or "?" (ı is "I" in upper
    /// case, and 一 is "?"), and a case it lacks is not taken (ƒ stays ƒ, Ƒ not being in 1252). 0 does neither.</item>
    /// <item>Proper case starts a word after Chr(0), a tab, LF, VT, FF, CR or a space, and after nothing else, so
    /// "o'neil's" is "O'neil's" and "jean-luc" "Jean-luc".</item>
    /// </list>
    /// </summary>
    private static Vb6Value StrConv(IReadOnlyList<Vb6Value> a)
    {
        var conversion = CompareArgument(a[1]);
        long cases = conversion & 3, rest = conversion & ~3L;
        if (conversion < 0 || (rest & ~(64L | 128L)) != 0 || rest == (64L | 128L) || (rest != 0 && cases != 0))
            throw InvalidCall();
        if (Supplied(a, 2))
            CheckLocaleIdentifier(a[2]);
        if (a[0].Value is VBArray)
            throw new VBRunTimeException(VBStandardError.TypeMismatch);
        if (a[0].IsNull)
            return Vb6Value.Null;
        if (rest != 0)
            throw new NotImplementedException(
                "StrConv with vbUnicode or vbFromUnicode makes a byte string, which HexIDE does not have yet (#1510).");
        var s = AsStr(a[0]);
        return new Vb6Value(cases == 0 ? s : CaseThroughCodePage(s, (int)cases));
    }

    private static void CheckLocaleIdentifier(Vb6Value lcid)
    {
        var id = CompareArgument(lcid);
        if (id == 0)
            return;
        try
        {
            _ = System.Globalization.CultureInfo.GetCultureInfo((int)id);
        }
        catch (Exception e) when (e is System.Globalization.CultureNotFoundException or ArgumentOutOfRangeException)
        {
            throw InvalidCall();
        }
    }

    // StrConv's upper (1), lower (2) or proper (3) case, made through the ANSI code page (see StrConv).
    private static string CaseThroughCodePage(string s, int cases)
    {
        var ansi = AnsiText();
        var sb = new StringBuilder(s.Length);
        var wordStart = true;
        foreach (var c in ansi.GetString(ansi.GetBytes(s)))
        {
            var mapped = cases == 1 || cases == 3 && wordStart ? VbCase.Upper(c) : VbCase.Lower(c);
            sb.Append(mapped != c && ansi.GetString(ansi.GetBytes(mapped.ToString())) != mapped.ToString() ? c : mapped);
            wordStart = c is '\0' or '\t' or '\n' or '\v' or '\f' or '\r' or ' ';
        }
        return sb.ToString();
    }

    private static Vb6Value NullOrStr(Vb6Value v, Func<string, string> fn) =>
        v.IsNull ? Vb6Value.Null : new Vb6Value(fn(AsStr(v)));

    private static VBRunTimeException InvalidCall() => new(VBStandardError.InvalidProcedureCall);

    // The byte-string functions (#1510), measured on vb6.exe, native and p-code alike. A String is a run of bytes, two to a
    // character with the low byte first, and each of these counts bytes where its plain form counts characters: LenB("abc")
    // is 6, LeftB("abc", 4) is "ab", and InStrB("abc", "b") is 3.

    // LenB: a declared scalar's size, as Len gives it, and otherwise twice the length of the text, a Variant's included
    // (LenB of a Variant holding 12345 is 10). A Type's size in memory is padded, LenB of an Integer and a Long being 8
    // where Len is 6, and isn't built.
    private static Vb6Value LenB(IReadOnlyList<Vb6Value> a)
    {
        if (a[0].IsNull) return Vb6Value.Null;
        if (a is IntrinsicArgumentValues { LenVariableSize: { } size }) return new Vb6Value(size);
        if (a[0].Type == Vb6Value.ValueType.UserDefinedType)
            throw new NotImplementedException("LenB of a user-defined type is not implemented: it is the Type's size in memory, "
                + "padded, which HexIDE does not lay out (#1510).");
        return new Vb6Value(2L * AsStr(a[0]).Length);
    }

    // AscB: the string's first byte, a Byte. An empty string is 5, and Null 94.
    private static Vb6Value AscB(Vb6Value v)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        var s = AsStr(v);
        return s.Length > 0 ? new Vb6Value((byte)s[0]) : throw InvalidCall();
    }

    // InStrB([start,] string1, string2[, compare]): the byte at which string2 first starts, at or after the byte start, or 0. A
    // match may start inside a character. The compare is read and ignored: the search is always by byte, so "b" is not in
    // "ABC" even with vbTextCompare. Unlike InStr, an empty string2 is found at start only within string1.
    private static Vb6Value InStrB(IReadOnlyList<Vb6Value> a)
    {
        int start = 1, i = 0;
        if (a.Count >= 3) { start = AsInt(a[0]); i = 1; }
        if (Supplied(a, 3)) AsInt(a[3]);
        if (a[i].IsNull || a[i + 1].IsNull) return Vb6Value.Null;
        if (start < 1) throw InvalidCall();
        string s1 = AsStr(a[i]), s2 = AsStr(a[i + 1]);
        int bytes = 2 * s1.Length, findBytes = 2 * s2.Length;
        if (bytes == 0 || start > bytes) return new Vb6Value(0L);
        for (var at = start - 1; at + findBytes <= bytes; at++)
        {
            var k = 0;
            while (k < findBytes && ByteAt(s1, at + k) == ByteAt(s2, k))
                k++;
            if (k == findBytes)
                return new Vb6Value((long)at + 1);
        }
        return new Vb6Value(0L);
    }

    // MidB, LeftB and RightB: a start below 1 or a negative length is 5, and a length past the end takes what there is.
    private static string MidB(string s, int start, int? length)
    {
        if (start < 1 || length < 0) throw InvalidCall();
        var bytes = 2 * s.Length;
        if (start > bytes) return "";
        return ByteSlice(s, start - 1, Math.Min(length ?? bytes, bytes - (start - 1)), "MidB");
    }

    private static string LeftB(string s, int length) =>
        length < 0 ? throw InvalidCall() : ByteSlice(s, 0, Math.Min(length, 2 * s.Length), "LeftB");

    private static string RightB(string s, int length)
    {
        if (length < 0) throw InvalidCall();
        var take = Math.Min(length, 2 * s.Length);
        return ByteSlice(s, 2 * s.Length - take, take, "RightB");
    }

    private static byte ByteAt(string s, int i) => (byte)(i % 2 == 0 ? s[i / 2] : s[i / 2] >> 8);

    // The bytes from the 0-based start, as a String. Bytes that pair up make characters, wherever they start: MidB("abc", 2, 2)
    // is the character &H6200. An odd number of them is a String HexIDE cannot hold, its strings being whole characters.
    private static string ByteSlice(string s, int start, int length, string function)
    {
        if (length % 2 != 0) throw OddByteString(function);
        var chars = new char[length / 2];
        for (var k = 0; k < chars.Length; k++)
            chars[k] = (char)(ByteAt(s, start + 2 * k) | ByteAt(s, start + 2 * k + 1) << 8);
        return new string(chars);
    }

    private static NotImplementedException OddByteString(string function) =>
        new($"{function} gave a String of an odd number of bytes, which HexIDE cannot hold yet: its strings are whole "
            + "characters (#1510). ChrB is always one byte.");

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
    /// string (the caller writes it back to the target slot). With <paramref name="bytes"/>, positions and lengths
    /// count UTF-16 bytes instead of characters (#2623).</summary>
    public static Vb6Value MidStatementReplace(Vb6Value target, Vb6Value start, Vb6Value? length, Vb6Value replacement, bool bytes = false)
    {
        if (target.IsNull || replacement.IsNull) throw InvalidUseOfNull();
        string s = AsStr(target);
        string repl = AsStr(replacement);
        int st = AsInt(start);
        int units = bytes ? 2 : 1;
        if (st < 1 || st > s.Length * units) throw InvalidCall();
        int from = st - 1;
        int count = Math.Min(repl.Length * units, s.Length * units - from);
        if (length is { } lv) count = Math.Min(count, Math.Max(0, AsInt(lv)));
        if (count <= 0) return new Vb6Value(s);
        var chars = s.ToCharArray();
        for (int i = 0; i < count; i++)
        {
            if (!bytes) chars[from + i] = repl[i];
            else
            {
                // Replace just the addressed byte, keeping its neighbour even for an odd count or offset.
                int at = from + i;
                int shift = (at % 2) * 8;
                chars[at / 2] = (char)((chars[at / 2] & ~(0xff << shift)) | ByteAt(repl, i) << shift);
            }
        }
        return new Vb6Value(new string(chars));
    }

    // Long, NOT the magnitude rule. These have a FIXED declared return type in VB6 — measured:
    // Len, InStr, InStrRev, LBound, UBound, DateDiff and VarType are Long however small the answer
    // is, while Asc, Sgn, DatePart and the date parts really are Integer. Building the result from an
    // `int` runs it through Vb6Value(int)'s magnitude rule, which reports anything fitting Int16 as an
    // Integer — right for an arithmetic literal, wrong for a function with a declared return type. (#193)
    private static Vb6Value InStr(IReadOnlyList<Vb6Value> a, bool compareText)
    {
        // InStr([start,] string1, string2[, compare]). A leading numeric start is present only with 3+ args.
        int start = 1, i = 0;
        long compare = compareText ? 1 : 0;
        if (a.Count >= 3) { start = AsInt(a[0]); i = 1; }
        if (Supplied(a, 3)) compare = CompareArgument(a[3]);
        if (a[i].IsNull || a[i + 1].IsNull) return Vb6Value.Null;
        if (start < 1) throw InvalidCall();
        string s1 = AsStr(a[i]), s2 = AsStr(a[i + 1]);
        // An empty string1 holds nothing, and an empty string2 is found at start, however far past the end (measured, #1567).
        if (s1.Length == 0) return new Vb6Value(0L);
        if (s2.Length == 0) return new Vb6Value((long)start);
        var text = IsTextCompare(compare);
        if (start > s1.Length) return new Vb6Value(0L);
        // As text a match may start or end inside an expansion: "s" is in "ß" (measured, #1567).
        return new Vb6Value((long)Find(s1, s2, start - 1, text, wholeCharacters: false, out _) + 1);   // 0 when not found
    }

    /// <summary>Where <paramref name="find"/> is next in <paramref name="s"/> from the 0-based <paramref name="start"/>, or -1,
    /// as a compare of 0 (binary) or 1 (text) searches, and how many characters of <paramref name="s"/> the match spans: as
    /// text "ss" spans the one character ß. <paramref name="wholeCharacters"/> is how Replace and Split search, which take
    /// only a match that starts and ends on whole characters (see <see cref="VbTextCompare.IndexOf"/>).</summary>
    internal static int Find(string s, string find, int start, bool text, bool wholeCharacters, out int length) =>
        Find(s, find, start, text ? new VbTextCompare.Searchable(s) : null, wholeCharacters, out length);

    // The same, for a caller that searches one string many times, as Replace and Split do, with the string folded for a text
    // search once (text) or not at all (null, a binary search).
    internal static int Find(string s, string find, int start, VbTextCompare.Searchable? text, bool wholeCharacters, out int length)
    {
        if (text != null)
            return text.IndexOf(find, start, wholeCharacters, out length);
        length = find.Length;
        return s.IndexOf(find, start, StringComparison.Ordinal);
    }

    private static Vb6Value InStrRev(IReadOnlyList<Vb6Value> a, bool compareText)
    {
        // InStrRev(string1, string2[, start[, compare]]) — start defaults to -1 (from the end).
        var text = Supplied(a, 3) ? IsTextCompare(CompareArgument(a[3]), databaseAsText: true) : compareText;
        if (a[0].IsNull || a[1].IsNull) return Vb6Value.Null;
        string s1 = AsStr(a[0]), s2 = AsStr(a[1]);
        int start = Supplied(a, 2) ? AsInt(a[2]) : -1;
        // A start past the end is 0, even where the text is there to find (measured, #1567).
        if (start > s1.Length) return new Vb6Value(0L);
        int from = start < 0 ? s1.Length - 1 : start - 1;
        if (from < 0 || s1.Length == 0) return new Vb6Value(0L);
        // An empty string2 is found at start: .NET's LastIndexOf would say one past it.
        if (s2.Length == 0) return new Vb6Value((long)from + 1);
        return new Vb6Value((long)(text
            ? VbTextCompare.LastIndexOf(s1, s2, from)
            : s1.LastIndexOf(s2, from, StringComparison.Ordinal)) + 1);
    }

    private static string Replace(IReadOnlyList<Vb6Value> a, bool compareText)
    {
        // Replace(expression, find, replace[, start[, count[, compare]]]) — returns from `start` onward.
        var compare = Supplied(a, 5) ? IsTextCompare(CompareArgument(a[5]), databaseAsText: true) : compareText;
        string s = AsStr(a[0]), find = AsStr(a[1]), repl = AsStr(a[2]);
        int start = Supplied(a, 3) ? AsInt(a[3]) : 1;
        int count = Supplied(a, 4) ? AsInt(a[4]) : -1;
        if (start < 1) throw InvalidCall();
        string sub = start <= s.Length ? s.Substring(start - 1) : "";
        if (find.Length == 0 || count == 0) return sub;
        var sb = new StringBuilder();
        var text = compare ? new VbTextCompare.Searchable(sub) : null;
        int i = 0, done = 0;
        while (count < 0 || done < count)
        {
            // As text a match is whole characters, and need not be find's length: "ss" replaces the one character ß.
            var at = Find(sub, find, i, text, wholeCharacters: true, out var length);
            if (at < 0)
                break;
            sb.Append(sub, i, at - i).Append(repl);
            i = at + length;
            done++;
        }
        sb.Append(sub, i, sub.Length - i);
        return sb.ToString();
    }

    private static char CharArg(Vb6Value v) =>
        v.Type == Vb6Value.ValueType.String
            ? (AsStr(v).Length > 0 ? AsStr(v)[0] : throw InvalidCall())
            : (char)AsInt(v);

    private static Encoding? ansiText;

    // The ANSI code page's table with Windows' own fallbacks, best fit among them, as Chr and Asc read it (#1134).
    private static Encoding AnsiText()
    {
        var codePage = Vb6TextEncoding.Ansi.CodePage;
        if (ansiText?.CodePage != codePage)
            ansiText = CodePagesEncodingProvider.Instance.GetEncoding(codePage) ?? Encoding.GetEncoding(codePage);
        return ansiText;
    }

    // A character code, as Chr and ChrW take it: rounded half to even (65.5 and 66.5 are both 66), True is -1, Empty 0, a
    // numeric string is read as a number, Null is 94, and past a Long is 6 (all measured, #1134).
    private static int CharCode(Vb6Value v)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        if (v.Type == Vb6Value.ValueType.EmptyVariant) return 0;
        var code = Math.Round(AsDouble(v), MidpointRounding.ToEven);
        return code is >= int.MinValue and <= int.MaxValue ? (int)code : throw new VBRunTimeException(VBStandardError.Overflow);
    }

    // The first UTF-16 unit of the text, as Asc and AscW read it: anything else is made text first (Asc(True) is "T"'s), an
    // empty string or Empty is 5, and Null 94 (measured, #1134).
    private static char FirstCharacterOf(Vb6Value v)
    {
        if (v.IsNull) throw InvalidUseOfNull();
        var s = AsStr(v);
        return s.Length > 0 ? s[0] : throw InvalidCall();
    }

    /// <summary>
    /// Chr: the character a byte of the ANSI code page stands for, read with that code page's table. On 1252, &amp;H80 is
    /// "€", and the five bytes 1252 leaves undefined are their own code points, &amp;H81 U+0081. Outside 0 to 255 it is 5
    /// (all measured, #1134). A double-byte code page, such as 932, has not been measured, so a code beyond ASCII is refused
    /// there rather than guessed.
    /// </summary>
    internal static string ChrInCodePage(int code, Encoding ansi)
    {
        if (code is >= 0 and < 128)
            return ((char)code).ToString();
        RefuseDoubleByte(ansi, "Chr");
        return code is >= 0 and <= 255 ? ansi.GetString([(byte)code]) : throw InvalidCall();
    }

    /// <summary>
    /// Asc: the character's byte in the ANSI code page, found as Windows finds it, best fit first, so Ā is 65, "A", and "?"
    /// (63) for a character with none, such as Ж (measured, #1134). A double-byte code page is refused beyond ASCII, as
    /// <see cref="ChrInCodePage"/> is.
    /// </summary>
    internal static int AscInCodePage(char c, Encoding ansi)
    {
        if (c < 128)
            return c;
        RefuseDoubleByte(ansi, "Asc");
        return ansi.GetBytes(c.ToString())[0];
    }

    private static void RefuseDoubleByte(Encoding ansi, string function)
    {
        if (!ansi.IsSingleByte)
            throw new NotImplementedException(
                $"{function} beyond ASCII under the double-byte ANSI code page {ansi.CodePage} has not been measured against VB6 yet (#1509)");
    }

    // ChrW: a UTF-16 unit, -32768 to 65535, a negative code being its unsigned unit, so ChrW(&HF050), an Integer -4016, is
    // U+F050. Outside that it is 5 (measured, #1134).
    private static Vb6Value ChrW(Vb6Value v)
    {
        var code = CharCode(v);
        return code is >= short.MinValue and <= ushort.MaxValue ? new Vb6Value(((char)(code & 0xFFFF)).ToString()) : throw InvalidCall();
    }

    private static string StrFn(Vb6Value v)
    {
        // Str returns the number as text with a leading space for non-negatives (CStr has none). The digits are
        // CStr's, without the zero before the point: Str(0.5) is " .5" and Str(-0.5) is "-.5" (measured). The point
        // is always a period, which is documented rather than measured: the oracle's locale already uses one.
        double d = AsDouble(v);
        string s = v.Value is VbDate ? AsStr(v) : VbDisplay.ToText(v, CultureInfo.InvariantCulture);
        if (s.StartsWith("0.", StringComparison.Ordinal))
            s = s.Substring(1);
        else if (s.StartsWith("-0.", StringComparison.Ordinal))
            s = "-" + s.Substring(2);
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
            return j > 2 ? RadixNumber(Convert.ToInt64(s.Substring(2, j - 2), 16), Vb6Value.ValueType.Integer) : 0;
        }
        if (s.StartsWith("&O", StringComparison.OrdinalIgnoreCase))
        {
            int j = 2;
            while (j < s.Length && s[j] is >= '0' and <= '7') j++;
            return j > 2 ? RadixNumber(Convert.ToInt64(s.Substring(2, j - 2), 8), Vb6Value.ValueType.Integer) : 0;
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
        if (!double.TryParse(s.Substring(0, i), NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
            return 0;
        if (!double.IsFinite(d))
            throw new VBRunTimeException(VBStandardError.Overflow);
        return d;
    }
}
