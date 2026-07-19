using System;
using System.Collections.Generic;
using System.Runtime.Versioning;
using Serilog;

namespace HexIDE.IDE;

public class ReferenceLibraryService : IReferenceLibraryService
{
    public IReadOnlyList<AvailableReference> GetAvailableReferences()
    {
        if (!OperatingSystem.IsWindows())
            return Array.Empty<AvailableReference>();
        return ReadFromRegistry();
    }

    [SupportedOSPlatform("windows")]
    private static List<AvailableReference> ReadFromRegistry()
    {
        var results = new List<AvailableReference>();
        try
        {
            using var typeLibKey = Microsoft.Win32.Registry.ClassesRoot.OpenSubKey("TypeLib");
            if (typeLibKey == null) return results;

            foreach (var guidName in typeLibKey.GetSubKeyNames())
            {
                using var guidKey = typeLibKey.OpenSubKey(guidName);
                if (guidKey == null) continue;

                foreach (var versionName in guidKey.GetSubKeyNames())
                {
                    using var versionKey = guidKey.OpenSubKey(versionName);
                    if (versionKey == null) continue;

                    var name = versionKey.GetValue(null) as string;
                    if (string.IsNullOrEmpty(name)) continue;

                    string? libPath = null;
                    foreach (var lcidName in versionKey.GetSubKeyNames())
                    {
                        using var lcidKey = versionKey.OpenSubKey(lcidName);
                        if (lcidKey == null) continue;

                        var win64Path = lcidKey.OpenSubKey("win64")?.GetValue(null) as string;
                        var win32Path = lcidKey.OpenSubKey("win32")?.GetValue(null) as string;
                        libPath = win64Path ?? win32Path;
                        if (libPath != null) break;
                    }

                    results.Add(new AvailableReference(guidName, versionName, name, libPath));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to enumerate type libraries from registry");
        }

        results.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        return results;
    }
}
