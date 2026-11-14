using System;
using System.Globalization;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace HexIDE.Runtime.Interpreter;

// VB6 reads NLS date data, whereas .NET normally reads ICU, even on Windows (#2041).
internal static partial class WindowsDateLocale
{
    private static readonly CalendarCache Calendars = new(Read);

    internal sealed class CalendarCache(Func<string, uint, string> read)
    {
        private readonly Dictionary<string, bool> gregorian = new(StringComparer.OrdinalIgnoreCase);

        internal bool AppliesTo(CultureInfo culture)
        {
            if (culture.Name.Length == 0 || culture.Calendar is not GregorianCalendar)
                return false;
            lock (gregorian)
            {
                if (!gregorian.TryGetValue(culture.Name, out var applies))
                {
                    // ICU and Windows can disagree even about the default calendar (ar-SA on newer ICU).
                    applies = Enum.TryParse<GregorianCalendarTypes>(read(culture.Name, 0x80001009), out var calendar)
                        && Enum.IsDefined(calendar); // LOCALE_ICALENDARTYPE | LOCALE_NOUSEROVERRIDE
                    gregorian.Add(culture.Name, applies);
                }
                return applies;
            }
        }
    }

    internal static bool AppliesTo(CultureInfo culture) =>
        OperatingSystem.IsWindows() && Calendars.AppliesTo(culture);

    internal static void Apply(CultureInfo culture)
    {
        var userName = new char[85];
        var length = GetUserDefaultLocaleName(userName, userName.Length);
        Apply(culture, length > 0 ? new string(userName, 0, length - 1) : "", Read);
    }

    internal static void Apply(CultureInfo culture, string userName, Func<string, uint, string> read,
        Func<string, string>? readEra = null)
    {
        var isUser = culture.UseUserOverride && string.Equals(culture.Name, userName, StringComparison.OrdinalIgnoreCase);
        Apply(culture.DateTimeFormat, type => read(culture.Name, type | (isUser ? 0u : 0x80000000u)),
            () => (readEra ?? ReadEra)(culture.Name));
    }

    // The reader seam also lets Linux exercise the mapping without pretending to measure Windows or VB6.
    internal static void Apply(DateTimeFormatInfo dates, Func<uint, string> read, Func<string> readEra)
    {
        string? era = null;
        string Era() => era ??= readEra();
        dates.DateSeparator = read(0x1d); // LOCALE_SDATE
        dates.ShortDatePattern = ManagedPattern(read(0x1f), Era); // LOCALE_SSHORTDATE
        dates.LongDatePattern = ManagedPattern(read(0x20), Era); // LOCALE_SLONGDATE
        dates.MonthNames = Months(0x38, read);
        dates.AbbreviatedMonthNames = Months(0x44, read);
        dates.MonthGenitiveNames = Months(0x10000038, read);
        dates.AbbreviatedMonthGenitiveNames = Months(0x10000044, read);
        var days = new string[7];
        var abbreviated = new string[7];
        for (var i = 0; i < 7; i++)
        {
            // NLS starts with Monday; DateTimeFormatInfo starts with Sunday.
            days[(i + 1) % 7] = read((uint)(0x2a + i));
            abbreviated[(i + 1) % 7] = read((uint)(0x31 + i));
        }
        dates.DayNames = days;
        dates.AbbreviatedDayNames = abbreviated;
    }

    // NLS doubles apostrophes inside literals; .NET escapes them, and treats backslashes as escapes.
    internal static string ManagedPattern(string pattern, Func<string> readEra)
    {
        var result = new System.Text.StringBuilder();
        var quoted = false;
        for (var index = 0; index < pattern.Length; index++)
        {
            char current = pattern[index];
            if (current == 'g' && !quoted)
            {
                while (index + 1 < pattern.Length && pattern[index + 1] == 'g')
                    index++;
                result.Append('\'').Append(readEra().Replace("\\", "\\\\").Replace("'", "\\'")).Append('\'');
            }
            else if (current == '\\')
                result.Append("\\\\");
            else if (current == '\'' && quoted && index + 1 < pattern.Length && pattern[index + 1] == '\'')
            {
                result.Append("\\'");
                index++;
            }
            else
            {
                if (current == '\'')
                    quoted = !quoted;
                result.Append(current);
            }
        }
        return result.ToString();
    }

    private static string[] Months(uint first, Func<uint, string> read)
    {
        var names = new string[13];
        for (var i = 0; i < 12; i++)
            names[i] = read(first + (uint)i);
        names[12] = "";
        return names;
    }

    private static string Read(string locale, uint type)
    {
        var length = GetLocaleInfoEx(locale, type, null, 0);
        if (length == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        var buffer = new char[length];
        if (GetLocaleInfoEx(locale, type, buffer, buffer.Length) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return new string(buffer, 0, length - 1);
    }

    private static string ReadEra(string locale)
    {
        var date = new SystemTime { Year = 2000, Month = 12, Day = 31 };
        var length = GetDateFormatEx(locale, 0, in date, "gg", null, 0, null);
        if (length == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        var buffer = new char[length];
        if (GetDateFormatEx(locale, 0, in date, "gg", buffer, buffer.Length, null) == 0)
            throw new System.ComponentModel.Win32Exception(Marshal.GetLastPInvokeError());
        return new string(buffer, 0, length - 1);
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime
    {
        public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds;
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetDateFormatEx(string locale, uint flags, in SystemTime date, string format,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] char[]? output, int count, string? calendar);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetLocaleInfoEx(string localeName, uint localeType,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 3)] char[]? data, int count);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16, SetLastError = true)]
    private static partial int GetUserDefaultLocaleName(
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] char[] localeName, int count);
}
