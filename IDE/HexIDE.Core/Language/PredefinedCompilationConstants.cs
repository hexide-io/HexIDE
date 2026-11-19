using System.Runtime.InteropServices;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Language;

/// <summary>The numeric predefined #If tables shared by run engines and editor previews (#4821).</summary>
public static class PredefinedCompilationConstants
{
    // vb6.exe SP6, #1112/#4245. An undefined name is not an entry in the table.
    private static readonly IReadOnlyDictionary<string, int> Vb6 = Numbers(("Win16", 0), ("Win32", 1));

    // Excel x64 row K (#4245, oracle PR #3965) and Excel x86 W1–W6 (#4399, oracle PR #4398).
    private static readonly IReadOnlyDictionary<string, int> Vba64 =
        Numbers(("VBA6", 1), ("VBA7", 1), ("Win32", 1), ("Win64", 1), ("Win16", 0), ("Mac", 0));
    private static readonly IReadOnlyDictionary<string, int> Vba32 =
        Numbers(("VBA6", 1), ("VBA7", 1), ("Win32", 1), ("Win64", 0), ("Win16", 0), ("Mac", 0));

    // #4821: RDCore.SDK PredefinedConditionalConstants.cs (Of), SDK 0.1.8-alpha (unchanged since 0.1.0-alpha).
    // HexIDE runs 64-bit only. DEBUG is deliberately omitted.
    private static readonly IReadOnlyDictionary<string, int> RdcoreWindowsBuiltIns =
        Numbers(("Win16", 0), ("Win32", 1), ("Win64", 1), ("Mac", 0), ("Linux", 0), ("Linux64", 0), ("VBA6", 1), ("VBA7", 1));
    private static readonly IReadOnlyDictionary<string, int> RdcoreMacBuiltIns =
        Numbers(("Win16", 0), ("Win32", 0), ("Win64", 0), ("Mac", 1), ("Linux", 0), ("Linux64", 0), ("VBA6", 1), ("VBA7", 1));
    private static readonly IReadOnlyDictionary<string, int> RdcoreLinuxBuiltIns =
        Numbers(("Win16", 0), ("Win32", 0), ("Win64", 0), ("Mac", 0), ("Linux", 1), ("Linux64", 1), ("VBA6", 1), ("VBA7", 1));

    // Excel for Mac 16.115, arm64: measured in #4821. Linux/Linux64 are not defined.
    private static readonly IReadOnlyDictionary<string, int> VbaMacBuiltIns =
        Numbers(("VBA6", 1), ("VBA7", 1), ("Win32", 0), ("Win64", 1), ("Win16", 0), ("Mac", 1));

    /// <summary>Select the target's table on the current or specified host; this selects constants only.</summary>
    public static IReadOnlyDictionary<string, int> ForTarget(ProjectTarget target, VbaBitness bitness, OSPlatform? host = null)
    {
        host ??= OperatingSystem.IsWindows() ? OSPlatform.Windows :
            OperatingSystem.IsMacOS() ? OSPlatform.OSX : OperatingSystem.IsLinux() ? OSPlatform.Linux : default;
        if (target == ProjectTarget.RDCore)
            return host == OSPlatform.OSX ? RdcoreMacBuiltIns :
                host == OSPlatform.Linux ? RdcoreLinuxBuiltIns : RdcoreWindowsBuiltIns;
        // Linux has no VBA to measure, so its VBA7 projects retain the Windows tables.
        if (bitness == VbaBitness.Bit64)
            return host == OSPlatform.OSX ? VbaMacBuiltIns : Vba64;
        // 32-bit VBA7 on Mac is unmeasured (Office for Mac is 64-bit only); it keeps the Windows table.
        // VB6 retains its table on every host.
        return bitness == VbaBitness.Bit32 ? Vba32 : Vb6;
    }

    private static Dictionary<string, int> Numbers(params (string Name, int Value)[] constants)
    {
        var table = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var (name, value) in constants) table[name] = value;
        return table;
    }
}
