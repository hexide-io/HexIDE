using System;
using System.Runtime.InteropServices;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Runtime.Dialogs;

internal static partial class ProgramButtonCaptions
{
    // https://learn.microsoft.com/windows/win32/dlgbox/mb-getstring
    // Measured by hexide-int, 2026-11-16, Windows Server 2025 build 26100, UI 0x0409,
    // from 32-bit and 64-bit PowerShell (#4250): wBtn is command ID minus one.
    // wBtn | Caption     | Command ID
    // 0    | OK          | IDOK 1
    // 1    | Cancel      | IDCANCEL 2
    // 2    | &Abort      | IDABORT 3
    // 3    | &Retry      | IDRETRY 4
    // 4    | &Ignore     | IDIGNORE 5
    // 5    | &Yes        | IDYES 6
    // 6    | &No         | IDNO 7
    // 7    | &Close      | IDCLOSE 8
    // 8    | Help        | IDHELP 9
    // 9    | &Try Again  | IDTRYAGAIN 10
    // 10   | &Continue   | IDCONTINUE 11
    // 11–15 | NULL
    internal static string Get(MessageBoxResult result, string fallback)
    {
        if (!OperatingSystem.IsWindows())
            return fallback;
        var index = result switch
        {
            MessageBoxResult.Ok => 0,
            MessageBoxResult.Cancel => 1,
            MessageBoxResult.Abort => 2,
            MessageBoxResult.Retry => 3,
            MessageBoxResult.Ignore => 4,
            MessageBoxResult.Yes => 5,
            MessageBoxResult.No => 6,
            MessageBoxResult.TryAgain => 9,
            MessageBoxResult.Continue => 10,
            _ => -1
        };
        return index < 0 ? fallback : FromNative(Marshal.PtrToStringUni(MB_GetString((uint)index)), fallback);
    }

    internal static string FromNative(string? caption, string fallback)
    {
        if (caption is null)
            return fallback;
        var converted = new StringBuilder();
        for (var i = 0; i < caption.Length; i++)
        {
            if (caption[i] == '&')
            {
                if (i + 1 < caption.Length && caption[i + 1] == '&')
                {
                    converted.Append('&');
                    i++;
                }
                else
                    converted.Append('_');
            }
            else if (caption[i] == '_')
                converted.Append("__"); // a literal underscore, which Avalonia would read as an access key
            else
                converted.Append(caption[i]);
        }
        return converted.ToString();
    }

    [LibraryImport("user32.dll")]
    private static partial IntPtr MB_GetString(uint wBtn);
}
