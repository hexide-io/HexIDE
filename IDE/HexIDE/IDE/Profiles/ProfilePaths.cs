using System.IO;
using System.Linq;

namespace HexIDE.IDE.Profiles;

internal static class ProfilePaths
{
    public static string[] Components(string reference) => reference.Replace('\\', '/').Split('/');

    public static bool IsSafeReference(string reference) => Components(reference).All(component =>
        component.Length > 0 && !component.EndsWith('.') && !component.EndsWith(' ')
        && !component.Any(c => c < ' ' || "<>:\"|?*".Contains(c)) && !IsDeviceName(component));

    private static bool IsDeviceName(string component)
    {
        var stem = component.Split('.')[0].TrimEnd(' ').ToUpperInvariant();
        return stem is "CON" or "PRN" or "AUX" or "NUL" or "CONIN$" or "CONOUT$"
            || (stem.Length == 4 && (stem.StartsWith("COM", StringComparison.Ordinal)
                || stem.StartsWith("LPT", StringComparison.Ordinal)) && "123456789¹²³".Contains(stem[3]));
    }

    // A preflight only: the future loader must protect the actual reads from concurrent replacement.
    public static bool HasLink(string directory, string reference)
    {
        var current = Path.GetFullPath(directory);
        foreach (var component in new[] { "" }.Concat(Components(reference)))
        {
            if (component.Length > 0)
                current = Path.Combine(current, component);
            try
            {
                if ((File.GetAttributes(current) & FileAttributes.ReparsePoint) != 0)
                    return true;
            }
            catch (FileNotFoundException) { return false; }
            catch (DirectoryNotFoundException) { return false; }
        }
        return false;
    }
}
