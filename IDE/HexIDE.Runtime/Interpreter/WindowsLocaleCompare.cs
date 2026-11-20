using System.Runtime.InteropServices;
using HexIDE.IDE;

namespace HexIDE.Runtime.Interpreter;

// #4151: locale order belongs to Windows, never the host's ICU tables.
internal static partial class WindowsLocaleCompare
{
    public static int Compare(string first, string second, long identifier)
    {
        var name = new char[85];
        int count = LCIDToLocaleName((uint)identifier, name, name.Length, 0x08000000); // LOCALE_ALLOW_NEUTRAL_NAMES
        if (count == 0)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        var locale = new string(name, 0, count - 1);
        return Compare(first, second, locale);
    }

    public static int Compare(string first, string second, string locale)
    {
        // NORM_IGNORECASE, with the default word sort (the measured co-op/coop row).
        int result = CompareStringEx(locale, 0x00000001, first, first.Length, second, second.Length,
            IntPtr.Zero, IntPtr.Zero, 0);
        if (result == 0)
            throw new VBRunTimeException(VBStandardError.InvalidProcedureCall);
        return result - 2; // CSTR_LESS_THAN / EQUAL / GREATER_THAN are 1 / 2 / 3.
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int LCIDToLocaleName(uint locale, [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 2)] char[] name,
        int count, uint flags);

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int CompareStringEx(string locale, uint flags, string first, int firstLength,
        string second, int secondLength, IntPtr version, IntPtr reserved, nint parameter);
}
