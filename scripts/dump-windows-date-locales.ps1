<#
.SYNOPSIS
    Read Windows' own date data for every specific locale, for HexIDE's off-Windows date table (#3928).

.DESCRIPTION
    VB6 takes a program's dates from Windows' NLS data: the short and long date patterns, the date separator, and the
    month and day names. .NET reads ICU instead, even on Windows, and the two disagree in many cultures. es-US writes
    its short date month first in Windows and day first in ICU, so a date written on Windows can be read back as a
    different date on Linux (#2041, #3928). On Windows, HexIDE asks Windows (WindowsDateLocale). Elsewhere it reads
    the table this script writes.

    Every value is read with LOCALE_NOUSEROVERRIDE, so the table holds each locale's defaults, never this machine's
    user customisations. The fields are the ones WindowsDateLocale.Apply reads, plus:
      - the calendar type, which decides whether the table applies;
      - the era that a "gg" in a pattern stands for (#3946).

    Run it on Windows only. The table describes the Windows build it was generated on, which it records. Windows
    changes locale data between releases, so a later build may differ.

.PARAMETER OutFile
    Where to write the JSON. Defaults to the runtime's embedded copy.

.EXAMPLE
    pwsh scripts/dump-windows-date-locales.ps1
#>
[CmdletBinding()]
param(
    [string]$OutFile = (Join-Path $PSScriptRoot '..' 'IDE' 'HexIDE.Runtime' 'Interpreter' 'Data' 'windows-date-locales.json')
)
$ErrorActionPreference = 'Stop'
if (-not $IsWindows) { throw 'This reads Windows NLS data, so it runs on Windows only.' }

Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

public static class WindowsDateLocales
{
    private delegate bool EnumLocalesProcEx(IntPtr name, uint flags, IntPtr lParam);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool EnumSystemLocalesEx(EnumLocalesProcEx callback, uint flags, IntPtr lParam, IntPtr reserved);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetLocaleInfoEx(string name, uint type, StringBuilder data, int length);
    [StructLayout(LayoutKind.Sequential)]
    private struct SystemTime { public ushort Year, Month, DayOfWeek, Day, Hour, Minute, Second, Milliseconds; }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern int GetDateFormatEx(string name, uint flags, ref SystemTime date, string format, StringBuilder data, int length, string calendar);

    private const uint LocaleSpecificData = 0x20, NoUserOverride = 0x80000000;

    public static List<string> SpecificLocales()
    {
        var names = new List<string>();
        if (!EnumSystemLocalesEx((name, flags, lParam) => { names.Add(Marshal.PtrToStringUni(name)); return true; },
                LocaleSpecificData, IntPtr.Zero, IntPtr.Zero))
            throw new InvalidOperationException("EnumSystemLocalesEx failed: " + Marshal.GetLastWin32Error());
        names.Sort(StringComparer.Ordinal);
        return names;
    }

    public static string Info(string name, uint type)
    {
        var data = new StringBuilder(256);
        if (GetLocaleInfoEx(name, type | NoUserOverride, data, data.Capacity) == 0)
            throw new InvalidOperationException(string.Format("GetLocaleInfoEx({0}, 0x{1:x}) failed: {2}", name, type, Marshal.GetLastWin32Error()));
        return data.ToString();
    }

    // Every date VB6 can hold, years 100 to 9999, is in the one Gregorian era, so any date in that range will do.
    public static string Era(string name)
    {
        var date = new SystemTime { Year = 2000, Month = 12, Day = 31 };
        var data = new StringBuilder(256);
        // GetDateFormatEx refuses LOCALE_NOUSEROVERRIDE (1004), and a user cannot customise the era anyway.
        if (GetDateFormatEx(name, 0, ref date, "gg", data, data.Capacity, null) == 0)
            throw new InvalidOperationException(string.Format("GetDateFormatEx({0}, gg) failed: {1}", name, Marshal.GetLastWin32Error()));
        return data.ToString();
    }
}
'@

function Names([string]$name, [uint32]$first, [int]$count) {
    , @(0..($count - 1) | ForEach-Object { [WindowsDateLocales]::Info($name, $first + [uint32]$_) })
}
function Same($a, $b) { ($a -join "`u{1}") -ceq ($b -join "`u{1}") }

$version = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion'
$build = "$([Environment]::OSVersion.Version.Major).$([Environment]::OSVersion.Version.Minor).$($version.CurrentBuild).$($version.UBR)"
$locales = [ordered]@{}
foreach ($name in [WindowsDateLocales]::SpecificLocales()) {
    $months = Names $name 0x38 12
    $abbreviatedMonths = Names $name 0x44 12
    $genitive = Names $name 0x10000038 12
    $abbreviatedGenitive = Names $name 0x10000044 12
    $entry = [ordered]@{
        calendar = [WindowsDateLocales]::Info($name, 0x1009)   # LOCALE_ICALENDARTYPE
        dateSeparator = [WindowsDateLocales]::Info($name, 0x1d) # LOCALE_SDATE
        shortDate = [WindowsDateLocales]::Info($name, 0x1f)     # LOCALE_SSHORTDATE
        longDate = [WindowsDateLocales]::Info($name, 0x20)      # LOCALE_SLONGDATE
        era = [WindowsDateLocales]::Era($name)
        months = $months
        abbreviatedMonths = $abbreviatedMonths
    }
    # The genitive forms are left out where they are the same as the nominative, which is most locales.
    if (-not (Same $genitive $months)) { $entry.genitiveMonths = $genitive }
    if (-not (Same $abbreviatedGenitive $abbreviatedMonths)) { $entry.abbreviatedGenitiveMonths = $abbreviatedGenitive }
    $entry.days = Names $name 0x2a 7               # LOCALE_SDAYNAME1, Monday, to 7, Sunday
    $entry.abbreviatedDays = Names $name 0x31 7
    $locales[$name] = $entry
}

$options = [System.Text.Json.JsonSerializerOptions]::new()
$options.Encoder = [System.Text.Encodings.Web.JavaScriptEncoder]::UnsafeRelaxedJsonEscaping
$lines = [System.Collections.Generic.List[string]]::new()
$lines.Add('{')
$lines.Add('  "note": ' + [System.Text.Json.JsonSerializer]::Serialize('Generated by scripts/dump-windows-date-locales.ps1 from Windows NLS data, with LOCALE_NOUSEROVERRIDE, so no machine''s own settings are in it. Do not hand-edit: regenerate. Days run Monday to Sunday, as NLS numbers them. genitiveMonths and abbreviatedGenitiveMonths appear only where they differ from months and abbreviatedMonths. era is what a "gg" in a pattern writes (#3946).', $options) + ',')
$lines.Add('  "windowsBuild": ' + [System.Text.Json.JsonSerializer]::Serialize($build, $options) + ',')
$lines.Add('  "locales": {')
$names = @($locales.Keys)
for ($i = 0; $i -lt $names.Count; $i++) {
    $json = [System.Text.Json.JsonSerializer]::Serialize($locales[$names[$i]], $options)
    $lines.Add('    ' + [System.Text.Json.JsonSerializer]::Serialize($names[$i], $options) + ': ' + $json + $(if ($i -lt $names.Count - 1) { ',' } else { '' }))
}
$lines.Add('  }')
$lines.Add('}')
[System.IO.File]::WriteAllText([System.IO.Path]::GetFullPath($OutFile), ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
"Wrote $($names.Count) locales from Windows $build to $OutFile"
