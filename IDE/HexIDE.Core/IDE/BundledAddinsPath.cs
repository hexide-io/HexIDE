namespace HexIDE.IDE;

/// <summary>The bundled add-ins live outside the macOS bundle's executable directory (#4495).</summary>
public static class BundledAddinsPath
{
    public static string Directory => For(AppContext.BaseDirectory, OperatingSystem.IsMacOS());

    internal static string For(string baseDirectory, bool macOS)
    {
        var directory = Path.TrimEndingDirectorySeparator(baseDirectory);
        var contents = Path.GetDirectoryName(directory);
        if (macOS && Path.GetFileName(directory) == "MacOS" && contents is not null
            && Path.GetFileName(contents) == "Contents"
            && Path.GetDirectoryName(contents)?.EndsWith(".app", StringComparison.OrdinalIgnoreCase) == true)
            return Path.Combine(contents, "Resources", "addins");

        return Path.Combine(baseDirectory, "addins");
    }
}
