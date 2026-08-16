using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace HexIDE.Runtime.Interpreter;

// Phase 3.7 — Format / Format$. Numeric formatting (facets 3.7.1 + 3.7.2), verified against vb6.exe:
//  * Rounds HALF-AWAY-FROM-ZERO on a decimal view of the value (Format(2.5,"0")="3") — NOT CInt's banker's.
//  * '0' forces a digit; '#' shows one only when significant; ',' groups; '%' scales x100; other chars literal.
//  * Sections "pos;neg;zero;null" route by sign — the negative section receives the ABSOLUTE value (so it must
//    supply its own sign/parens); a pure-literal section (e.g. "zero") is emitted verbatim.
//  * Scientific "0.00E+00": E+ always shows the exponent sign, E- only for a negative exponent.
//  * A trailing ',' at the end of the integer part scales down by 1000 each ("#,##0," of 1234567 -> "1,235").
// Date, string and Boolean masks land in 3.7.3 / 3.7.4. (Format$ awaits the $-type-hint dispatch.)
public partial class VB6BuiltIns
{
    private static void RegisterFormat(Dictionary<string, BuiltinFn> d)
    {
        d["Format"] = (_, a, _) => FormatValue(a);
    }

    private static Vb6Value FormatValue(IReadOnlyList<Vb6Value> a)
    {
        if (a.Count < 1) throw InvalidCall();
        var v = a[0];
        string fmt = a.Count >= 2 && a[1].Type != Vb6Value.ValueType.EmptyVariant ? AsStr(a[1]) : "";

        if (fmt.Length == 0)
            return new Vb6Value(GeneralNumberOrString(v));

        var cc = CultureInfo.CurrentCulture;
        string fmtKey = fmt.Trim().ToLowerInvariant();
        // Out-of-decimal-range / non-finite magnitudes can't feed the decimal engine; render them in scientific
        // notation for the numeric named formats instead of throwing an uncatchable OverflowException (see
        // OutOfDecimalRangeNumeric — "fixed"/"standard"/"currency" of such a value is an approximation of VB6).
        if (fmtKey is "general number" or "fixed" or "standard" or "percent" or "scientific" or "currency"
            && OutOfDecimalRangeNumeric(v) is { } sciNamed)
            return new Vb6Value(sciNamed);
        switch (fmtKey)
        {
            case "general number": return new Vb6Value(GeneralNumber(v));
            case "fixed":          return new Vb6Value(FormatPlainNumeric(NumToDecimal(v), "0.00"));
            case "standard":       return new Vb6Value(FormatPlainNumeric(NumToDecimal(v), "#,##0.00"));
            case "percent":        return new Vb6Value(FormatPlainNumeric(NumToDecimal(v), "0.00%"));
            case "scientific":     return new Vb6Value(FormatScientific(NumToDecimal(v), "0.00E+00"));
            case "currency":       return new Vb6Value(FormatPlainNumeric(NumToDecimal(v),
                                        cc.NumberFormat.CurrencySymbol + "#,##0.00"));
            // Named date/time formats resolve through the current culture's patterns (locale-dependent).
            case "general date":   return new Vb6Value(AsDate(v).ToString(cc));
            case "long date":      return new Vb6Value(AsDate(v).ToString("D", cc));
            case "medium date":    return new Vb6Value(AsDate(v).ToString("dd-MMM-yy", cc));
            case "short date":     return new Vb6Value(AsDate(v).ToString("d", cc));
            case "long time":      return new Vb6Value(AsDate(v).ToString("T", cc));
            case "medium time":    return new Vb6Value(AsDate(v).ToString("hh:mm tt", cc));
            case "short time":     return new Vb6Value(AsDate(v).ToString("HH:mm", cc));
            // Boolean named formats (nonzero / True -> the first word).
            case "yes/no":         return new Vb6Value(ToBoolNum(v) ? "Yes" : "No");
            case "true/false":     return new Vb6Value(ToBoolNum(v) ? "True" : "False");
            case "on/off":         return new Vb6Value(ToBoolNum(v) ? "On" : "Off");
        }

        // A custom mask with an (unquoted) digit placeholder is numeric — even for a Date value (its serial).
        // A non-numeric string under a numeric mask is returned unchanged (verified: Format("abc","0.00")="abc").
        if (HasUnquotedPlaceholder(fmt))
        {
            if (v.Value is string ns && !decimal.TryParse(ns, NumberStyles.Any, cc, out _))
                return new Vb6Value(ns);
            return new Vb6Value(FormatNumericSections(v, fmt));
        }

        // String masks (@ & < >) take the value's string form.
        if (HasStringToken(fmt))
            return new Vb6Value(FormatString(AsStr(v), fmt));

        // A Date value, or any value under a date mask (a number is read as its OLE serial), formats as a date.
        if (v.Value is DateTime dtv)
            return new Vb6Value(FormatDate(dtv, fmt));
        if (IsDateMask(fmt))
            return new Vb6Value(FormatDate(AsDate(v), fmt));

        return new Vb6Value(GeneralNumberOrString(v));
    }

    // ---- numeric ----

    // General Number: the value's shortest decimal, current-culture decimal point, no grouping.
    private static string GeneralNumber(Vb6Value v) =>
        OutOfDecimalRangeNumeric(v)
        ?? NumToDecimal(v).ToString("0.#############################", CultureInfo.CurrentCulture);

    private static string GeneralNumberOrString(Vb6Value v) => v.Value switch
    {
        string s => s,
        bool b => b ? "True" : "False",
        DateTime dt => dt.ToString(CultureInfo.CurrentCulture),   // General Date
        _ => GeneralNumber(v),
    };

    // ---- date/time ----

    // Custom date mask. Tokens (case-insensitive): d/dd/ddd/dddd, m/mm/mmm/mmmm, y/yy/yyyy, h/hh, n/nn, s/ss,
    // w/ww, q, c, ttttt, plus AM/PM · am/pm · A/P · a/p. Pinned against vb6.exe: 'h' is 24-hour unless an AM/PM
    // token is present; 'm'/'mm' right after an 'h'/'hh' token means MINUTE (else month); 'n' is always minute;
    // 'w' uses the vbSunday base; 'y' alone is the day of the year.
    private static string FormatDate(DateTime dt, string mask)
    {
        var dfi = CultureInfo.CurrentCulture.DateTimeFormat;
        bool ampm = ContainsAmPm(mask);
        var sb = new StringBuilder();
        bool afterHour = false;   // set by h/hh, survives literals — drives the m-is-minute rule
        int i = 0;
        while (i < mask.Length)
        {
            char c = mask[i];
            if (c == '"') { i++; while (i < mask.Length && mask[i] != '"') { sb.Append(mask[i]); i++; } i++; continue; }
            if (c == '\\') { i++; if (i < mask.Length) { sb.Append(mask[i]); i++; } continue; }
            if (Match(mask, i, "AM/PM")) { sb.Append(dt.Hour < 12 ? "AM" : "PM"); i += 5; afterHour = false; continue; }
            if (Match(mask, i, "am/pm")) { sb.Append(dt.Hour < 12 ? "am" : "pm"); i += 5; afterHour = false; continue; }
            if (Match(mask, i, "A/P")) { sb.Append(dt.Hour < 12 ? "A" : "P"); i += 3; afterHour = false; continue; }
            if (Match(mask, i, "a/p")) { sb.Append(dt.Hour < 12 ? "a" : "p"); i += 3; afterHour = false; continue; }
            if (Match(mask, i, "ttttt")) { sb.Append(dt.ToString(dfi.LongTimePattern, CultureInfo.CurrentCulture)); i += 5; continue; }

            char lc = char.ToLowerInvariant(c);
            int run = RunLength(mask, i, lc);
            switch (lc)
            {
                case 'd':
                    sb.Append(run >= 4 ? dfi.GetDayName(dt.DayOfWeek)
                        : run == 3 ? dfi.GetAbbreviatedDayName(dt.DayOfWeek)
                        : dt.Day.ToString(run == 2 ? "00" : "0"));
                    afterHour = false; break;
                case 'm':
                    if (run >= 4) sb.Append(dfi.GetMonthName(dt.Month));
                    else if (run == 3) sb.Append(dfi.GetAbbreviatedMonthName(dt.Month));
                    else sb.Append((afterHour ? dt.Minute : dt.Month).ToString(run == 2 ? "00" : "0"));
                    afterHour = false; break;
                case 'y':
                    sb.Append(run >= 3 ? dt.Year.ToString("0000")
                        : run == 2 ? (dt.Year % 100).ToString("00")
                        : dt.DayOfYear.ToString());
                    afterHour = false; break;
                case 'h':
                    int hr = ampm ? (dt.Hour % 12 == 0 ? 12 : dt.Hour % 12) : dt.Hour;
                    sb.Append(hr.ToString(run >= 2 ? "00" : "0"));
                    afterHour = true; break;
                case 'n':
                    sb.Append(dt.Minute.ToString(run >= 2 ? "00" : "0"));
                    afterHour = false; break;
                case 's':
                    sb.Append(dt.Second.ToString(run >= 2 ? "00" : "0"));
                    afterHour = false; break;
                case 'w':
                    sb.Append(run >= 2 ? WeekOfYear(dt).ToString() : (((int)dt.DayOfWeek) + 1).ToString());
                    afterHour = false; break;
                case 'q':
                    sb.Append(((dt.Month - 1) / 3 + 1).ToString());
                    afterHour = false; break;
                case 'c':
                    sb.Append(dt.ToString(CultureInfo.CurrentCulture));
                    afterHour = false; break;
                default:
                    sb.Append(c); i++; continue;   // literal char (separators etc.) — preserves afterHour
            }
            i += run;
        }
        return sb.ToString();
    }

    private static bool IsDateMask(string mask)
    {
        if (ContainsAmPm(mask)) return true;
        for (int i = 0; i < mask.Length; i++)
        {
            char c = mask[i];
            if (c == '"') { i++; while (i < mask.Length && mask[i] != '"') i++; continue; }
            if (c == '\\') { i++; continue; }
            if ("ymdhnsqwtc".IndexOf(char.ToLowerInvariant(c)) >= 0) return true;
        }
        return false;
    }

    private static bool ContainsAmPm(string mask)
    {
        for (int i = 0; i < mask.Length; i++)
        {
            char c = mask[i];
            if (c == '"') { i++; while (i < mask.Length && mask[i] != '"') i++; continue; }
            if (c == '\\') { i++; continue; }
            if (Match(mask, i, "AM/PM") || Match(mask, i, "am/pm") || Match(mask, i, "A/P") || Match(mask, i, "a/p")) return true;
        }
        return false;
    }

    private static bool Match(string s, int i, string tok) =>
        i + tok.Length <= s.Length && s.Substring(i, tok.Length) == tok;

    // ---- string / Boolean ----

    private static bool ToBoolNum(Vb6Value v)
    {
        if (v.Value is bool b) return b;
        try { return NumToDecimal(v) != 0m; }
        catch (OverflowException) { return true; }        // out-of-decimal-range magnitude is nonzero
        catch (VBRunTimeException) { return false; }
    }

    private static bool HasStringToken(string mask)
    {
        for (int i = 0; i < mask.Length; i++)
        {
            char c = mask[i];
            if (c == '"') { i++; while (i < mask.Length && mask[i] != '"') i++; continue; }
            if (c == '\\') { i++; continue; }
            if (c is '@' or '&' or '<' or '>') return true;
        }
        return false;
    }

    // String mask: '@' = char-or-space, '&' = char-or-nothing, '!' = left-align (default is right-align),
    // '<'/'>' force lower/upper case. Excess characters overflow (never truncated). Verified vs vb6.exe:
    // Format("abc","@@@@@")="  abc", Format("abc","&&&&&")="abc", Format("abc","!@@@@@")="abc  ".
    private static string FormatString(string s, string mask)
    {
        bool leftAlign = IndexOfUnquoted(mask, '!') >= 0;

        int P = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            char c = mask[i];
            if (c == '"') { i++; while (i < mask.Length && mask[i] != '"') i++; }
            else if (c == '\\') i++;
            else if (c is '@' or '&') P++;
        }

        int L = s.Length;
        string overflowPrefix = "", overflowSuffix = "", chars;
        int empLead, empTrail;
        if (leftAlign)
        {
            int used = Math.Min(L, P);
            chars = s.Substring(0, used);
            overflowSuffix = L > P ? s.Substring(P) : "";
            empLead = 0; empTrail = P - used;
        }
        else
        {
            int overflow = Math.Max(0, L - P);
            overflowPrefix = s.Substring(0, overflow);
            chars = s.Substring(overflow);
            empLead = P - chars.Length; empTrail = 0;
        }

        var sb = new StringBuilder();
        sb.Append(overflowPrefix);
        int ph = 0, ci = 0;
        for (int i = 0; i < mask.Length; i++)
        {
            char c = mask[i];
            if (c == '"') { i++; while (i < mask.Length && mask[i] != '"') { sb.Append(mask[i]); i++; } continue; }
            if (c == '\\') { i++; if (i < mask.Length) sb.Append(mask[i]); continue; }
            if (c is '<' or '>' or '!') continue;               // modifiers, not literals
            if (c is '@' or '&')
            {
                bool empty = ph < empLead || ph >= P - empTrail;
                if (empty) { if (c == '@') sb.Append(' '); }    // '&' emits nothing
                else sb.Append(chars[ci++]);
                ph++;
            }
            else sb.Append(c);                                  // literal
        }
        sb.Append(overflowSuffix);

        string result = sb.ToString();
        if (IndexOfUnquoted(mask, '>') >= 0) result = result.ToUpper(CultureInfo.CurrentCulture);
        if (IndexOfUnquoted(mask, '<') >= 0) result = result.ToLower(CultureInfo.CurrentCulture);
        return result;
    }

    private static int RunLength(string s, int i, char lc)
    {
        int n = 0;
        while (i + n < s.Length && char.ToLowerInvariant(s[i + n]) == lc) n++;
        return n;
    }

    // Routes the value to its section (pos;neg;zero;null) then formats it. The negative section is fed the
    // absolute value; a section with no placeholder is a literal.
    private static string FormatNumericSections(Vb6Value v, string fmt)
    {
        if (OutOfDecimalRangeNumeric(v) is { } sci) return sci;
        var sections = SplitSections(fmt);
        decimal val = NumToDecimal(v);
        string section;
        decimal sv;
        if (val > 0m) { section = sections[0]; sv = val; }
        else if (val < 0m)
        {
            if (sections.Count >= 2) { section = sections[1]; sv = -val; }   // abs; section supplies the sign
            else { section = sections[0]; sv = val; }                        // single section -> formatter adds '-'
        }
        else { section = sections.Count >= 3 ? sections[2] : sections[0]; sv = 0m; }

        if (!HasUnquotedPlaceholder(section))
            return RenderLiterals(section);
        return IsScientific(section) ? FormatScientific(sv, section) : FormatPlainNumeric(sv, section);
    }

    private static string FormatPlainNumeric(decimal value, string fmt)
    {
        var nf = CultureInfo.CurrentCulture.NumberFormat;
        if (fmt.IndexOf('%') >= 0) value *= 100m;

        // Split into leading literals / number pattern / trailing literals around the placeholder run.
        int first = -1, last = -1;
        for (int i = 0; i < fmt.Length; i++)
            if (fmt[i] is '0' or '#') { if (first < 0) first = i; last = i; }
        string prefix = fmt.Substring(0, first);
        string middle = fmt.Substring(first, last - first + 1);
        string suffix = fmt.Substring(last + 1);

        // A trailing comma at the end of the integer part scales the value down by 1000 each; a comma after the
        // fraction is simply dropped. Either way the commas leave the literal suffix.
        int lead = 0;
        while (lead < suffix.Length && suffix[lead] == ',') lead++;
        if (lead > 0)
        {
            if (middle.IndexOf('.') < 0) value /= Pow10m(3 * lead);
            suffix = suffix.Substring(lead);
        }

        bool negative = value < 0m;
        decimal abs = Math.Abs(value);

        int dot = middle.IndexOf('.');
        string intMask = dot >= 0 ? middle.Substring(0, dot) : middle;
        string fracPh = OnlyPlaceholders(dot >= 0 ? middle.Substring(dot + 1) : "");
        int fracLen = fracPh.Length;
        int minInt = CountChar(intMask, '0');
        bool grouping = intMask.IndexOf(',') >= 0;

        // VB6 rounds to the value's precision and zero-pads a FIXED mask beyond that precision (Format(0.5, twenty
        // zeros) = "0.5" + 19 zeros); it never errors. Cap the rounding/scaling at a long-safe fractional width —
        // deeper digits are zeros for any Single/Double/Currency value anyway — then pad the rest. This avoids both
        // Math.Round's 28-digit limit (ArgumentOutOfRangeException) and the decimal→long overflow for a wide mask.
        int calcFrac = Math.Min(fracLen, 18);
        abs = Math.Round(abs, calcFrac, MidpointRounding.AwayFromZero);
        decimal truncated = Math.Truncate(abs);
        string intDigits = (truncated == 0m ? "" : truncated.ToString("F0", CultureInfo.InvariantCulture))
            .PadLeft(minInt, '0');

        string fracOut = "";
        if (fracLen > 0)
        {
            long scaled = (long)((abs - truncated) * Pow10m(calcFrac));
            string fracDigits = scaled.ToString(CultureInfo.InvariantCulture).PadLeft(calcFrac, '0').PadRight(fracLen, '0');
            int show = fracLen;
            while (show > 0 && fracPh[show - 1] == '#' && fracDigits[show - 1] == '0') show--;
            fracOut = fracDigits.Substring(0, show);
        }

        var sb = new StringBuilder();
        if (negative) sb.Append(nf.NegativeSign);
        sb.Append(RenderLiterals(prefix));
        sb.Append(grouping ? GroupThousands(intDigits, nf.NumberGroupSeparator) : intDigits);
        if (fracOut.Length > 0) { sb.Append(nf.NumberDecimalSeparator); sb.Append(fracOut); }
        sb.Append(RenderLiterals(suffix));
        return sb.ToString();
    }

    // Mantissa in [1,10) x 10^exp. E+ always prints the exponent sign; E- prints it only when negative.
    private static string FormatScientific(decimal value, string fmt)
    {
        bool negative = value < 0m;
        double abs = (double)Math.Abs(value);

        int e = IndexOfUnquoted(fmt, 'E');
        char eChar = 'E';
        if (e < 0) { e = IndexOfUnquoted(fmt, 'e'); eChar = 'e'; }
        string mantMask = fmt.Substring(0, e);

        string afterE = fmt.Substring(e + 1);
        char signChar = '+';
        int si = 0;
        if (afterE.Length > 0 && (afterE[0] == '+' || afterE[0] == '-')) { signChar = afterE[0]; si = 1; }
        string expMask = afterE.Substring(si);
        int minExpDigits = CountChar(expMask, '0');

        int exp = abs == 0 ? 0 : (int)Math.Floor(Math.Log10(abs));
        int mantFrac = OnlyPlaceholders(mantMask.Contains('.') ? mantMask[(mantMask.IndexOf('.') + 1)..] : "").Length;
        decimal mant = abs == 0 ? 0m : Math.Round((decimal)(abs / Math.Pow(10, exp)), mantFrac, MidpointRounding.AwayFromZero);
        if (mant >= 10m) { mant /= 10m; exp++; mant = Math.Round(mant, mantFrac, MidpointRounding.AwayFromZero); }

        string expSign = exp < 0 ? "-" : (signChar == '+' ? "+" : "");
        string expDigits = Math.Abs(exp).ToString(CultureInfo.InvariantCulture).PadLeft(minExpDigits, '0');

        var sb = new StringBuilder();
        if (negative) sb.Append(CultureInfo.CurrentCulture.NumberFormat.NegativeSign);
        sb.Append(FormatPlainNumeric(mant, mantMask));
        sb.Append(eChar).Append(expSign).Append(expDigits);
        return sb.ToString();
    }

    private static decimal NumToDecimal(Vb6Value v)
    {
        if (v.Value is decimal m) return m;
        if (v.Value is bool b) return b ? -1m : 0m;
        if (Vb6Value.TryNumericToDouble(v, out var d)) return (decimal)d;
        if (v.Value is string s && decimal.TryParse(s, NumberStyles.Any, CultureInfo.CurrentCulture, out var ds)) return ds;
        throw new VBRunTimeException(VBStandardError.TypeMismatch);
    }

    // The decimal-based numeric engine can't represent a magnitude beyond decimal range (|x| > ~7.9e28) or a
    // non-finite double — `(decimal)d` would throw an UNCATCHABLE OverflowException. VB6 doesn't error there: it
    // renders General Number / no-mask / Scientific in scientific notation (matched here via G15). Returns that
    // string for an out-of-range/non-finite value, or null when the value IS decimal-representable (the precise
    // engine then handles it unchanged). NB: a *fixed* mask ("0.00") of such a value is fully EXPANDED by VB6 (its
    // double-precision digit model) — HexIDE approximates that as scientific too (a documented divergence).
    private static string? OutOfDecimalRangeNumeric(Vb6Value v)
    {
        if (!Vb6Value.TryNumericToDouble(v, out var d)) return null;
        if (double.IsNaN(d)) return "1.#QNAN";
        if (double.IsPositiveInfinity(d)) return "1.#INF";
        if (double.IsNegativeInfinity(d)) return "-1.#INF";
        try { _ = (decimal)d; return null; }   // in decimal range → let the precise engine format it
        catch (OverflowException) { return d.ToString("G15", CultureInfo.CurrentCulture); }
    }

    // ---- mask helpers (quote/escape aware) ----

    private static bool IsScientific(string mask) => IndexOfUnquoted(mask, 'E') >= 0 || IndexOfUnquoted(mask, 'e') >= 0;

    private static bool HasUnquotedPlaceholder(string s)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') { i++; while (i < s.Length && s[i] != '"') i++; }
            else if (c == '\\') i++;
            else if (c is '0' or '#') return true;
        }
        return false;
    }

    private static int IndexOfUnquoted(string s, char ch)
    {
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') { i++; while (i < s.Length && s[i] != '"') i++; }
            else if (c == '\\') i++;
            else if (c == ch) return i;
        }
        return -1;
    }

    private static List<string> SplitSections(string s)
    {
        var result = new List<string>();
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') { sb.Append(c); i++; while (i < s.Length) { sb.Append(s[i]); if (s[i] == '"') break; i++; } }
            else if (c == '\\') { sb.Append(c); i++; if (i < s.Length) sb.Append(s[i]); }
            else if (c == ';') { result.Add(sb.ToString()); sb.Clear(); }
            else sb.Append(c);
        }
        result.Add(sb.ToString());
        return result;
    }

    // Emits a literal run: "..." contents verbatim, \x -> x, everything else as itself.
    private static string RenderLiterals(string s)
    {
        var sb = new StringBuilder();
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '"') { i++; while (i < s.Length && s[i] != '"') { sb.Append(s[i]); i++; } }
            else if (c == '\\') { i++; if (i < s.Length) sb.Append(s[i]); }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    private static string OnlyPlaceholders(string s)
    {
        var sb = new StringBuilder();
        foreach (var c in s) if (c is '0' or '#') sb.Append(c);
        return sb.ToString();
    }

    private static int CountChar(string s, char c)
    {
        int n = 0;
        foreach (var ch in s) if (ch == c) n++;
        return n;
    }

    private static decimal Pow10m(int n)
    {
        decimal r = 1m;
        for (int i = 0; i < n; i++) r *= 10m;
        return r;
    }

    private static string GroupThousands(string digits, string sep)
    {
        if (digits.Length <= 3) return digits;
        var sb = new StringBuilder();
        int firstGroup = digits.Length % 3;
        if (firstGroup == 0) firstGroup = 3;
        sb.Append(digits, 0, firstGroup);
        for (int i = firstGroup; i < digits.Length; i += 3)
        {
            sb.Append(sep);
            sb.Append(digits, i, 3);
        }
        return sb.ToString();
    }
}
