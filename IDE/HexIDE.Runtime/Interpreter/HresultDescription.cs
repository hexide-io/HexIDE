using System.Runtime.InteropServices;

namespace HexIDE.Runtime.Interpreter;

internal static partial class HresultDescription
{
    public static string For(int number)
    {
        if (!OperatingSystem.IsWindows())
            return "Automation error";
        var buffer = new char[32768];
        // FROM_SYSTEM | IGNORE_INSERTS | MAX_WIDTH_MASK: Windows replaces the final line break with a space (#4730).
        var count = FormatMessageW(0x00001000 | 0x00000200 | 0x000000ff, IntPtr.Zero,
            unchecked((uint)number), 0, buffer, buffer.Length, IntPtr.Zero);
        return count == 0 ? "Automation error" : "Automation error\n" + new string(buffer, 0, count);
    }

    [LibraryImport("kernel32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int FormatMessageW(uint flags, IntPtr source, uint message, uint language,
        [Out, MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 5)] char[] buffer, int size, IntPtr arguments);
}
