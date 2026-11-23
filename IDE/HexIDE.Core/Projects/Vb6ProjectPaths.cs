using System;

namespace HexIDE.Projects;

internal static class Vb6ProjectPaths
{
    public const string FormKey = "Form";
    public const string ModuleKey = "Module";
    public const string ClassKey = "Class";
    public const string UserControlKey = "UserControl";
    public const string PropertyPageKey = "PropertyPage";
    public const string UserDocumentKey = "UserDocument";
    public const string RelatedDocKey = "RelatedDoc";

    // Keys HexIDE does not model, kept as unknown lines, whose value is a path VB6's Save As names from the new folder.
    public const string DesignerKey = "Designer";
    public const string ResFileKey = "ResFile32";
    public const string MakePathKey = "Path32";

    // ── A VB6 path is a Windows path, on every host ───────────────────────────────────────────────────
    //
    // A .vbp is a Windows-native format: every path inside one is backslash-separated, whatever machine
    // reads it. System.IO.Path is therefore the WRONG tool for these strings — it answers about the HOST
    // filesystem. On Linux a backslash is an ordinary filename character, so
    // Path.GetFileName("docs\README.md") hands back the whole string and a related document ends up named
    // after its own directory. The mirror image bites on write: Path.GetRelativePath yields
    // "docs/README.md" there, which then goes into a file that has to say "docs\README.md".
    //
    // Both directions are silent — a wrong value that still looks like a path — and both are invisible on
    // a Windows dev machine. Only the Linux CI job catches them, which is exactly how this arrived.

    private static readonly char[] PathSeparators = ['\\', '/'];

    /// <summary>
    /// The last segment of a path as it appears inside a project file. Forward slashes count as separators
    /// too: a hand-edited or tool-generated .vbp can carry them, and VB6 itself accepts them.
    /// </summary>
    public static string FileNameOf(string projectFilePath)
    {
        var cut = projectFilePath.LastIndexOfAny(PathSeparators);
        return cut < 0 ? projectFilePath : projectFilePath[(cut + 1)..];
    }

    /// <summary>
    /// The last segment of a path inside a project file, with its extension removed. Matches
    /// <c>Path.GetFileNameWithoutExtension</c>'s treatment of a leading dot — <c>.gitignore</c> is a name,
    /// not an extension.
    /// </summary>
    public static string FileNameWithoutExtensionOf(string projectFilePath)
    {
        var name = FileNameOf(projectFilePath);
        var dot = name.LastIndexOf('.');
        return dot <= 0 ? name : name[..dot];
    }

    /// <summary>
    /// Rewrites a path read out of a project file into the host's separator, for FILESYSTEM RESOLUTION
    /// only. The raw value is still what gets written back, so .vbp fidelity is unaffected.
    /// </summary>
    public static string ToHostPath(string projectFilePath) =>
        projectFilePath.Replace('\\', System.IO.Path.DirectorySeparatorChar)
                       .Replace('/', System.IO.Path.DirectorySeparatorChar);

    /// <summary>
    /// Rewrites a host-computed relative path into the separator a .vbp must carry. Applied to everything
    /// emitted into a project file, so a project saved on a non-Windows host is still a valid VB6 project
    /// rather than one only HexIDE can read back.
    /// </summary>
    public static string ToProjectFilePath(string hostRelativePath) =>
        hostRelativePath.Replace(System.IO.Path.DirectorySeparatorChar, '\\')
                        .Replace(System.IO.Path.AltDirectorySeparatorChar, '\\');

    /// <summary>
    /// Where the path a project-file line names sits in the line: after <c>Name;</c> on a <c>Module=</c> or
    /// <c>Class=</c> line, inside the quotes on <c>ResFile32=</c> and <c>Path32=</c>, the whole value on the bare-path
    /// keys. Null for a key whose path does not follow the project file, and for a line whose path is empty.
    /// </summary>
    /// <remarks>
    /// <c>HelpFile=</c> names a path too, but VB6's Save As writes it as it was typed (measured, #1406), so it is not one
    /// of these.
    /// </remarks>
    public static (string Key, int Start, int Length)? ItemLineFile(string line)
    {
        var eq = line.IndexOf('=');
        if (eq < 0)
            return null;
        var key = line[..eq].Trim();
        var start = eq + 1;
        if (key.Equals(ModuleKey, StringComparison.OrdinalIgnoreCase)
            || key.Equals(ClassKey, StringComparison.OrdinalIgnoreCase))
        {
            var semi = line.IndexOf(';', start);
            if (semi >= 0)
                start = semi + 1;
        }
        else if (!key.Equals(FormKey, StringComparison.OrdinalIgnoreCase)
                 && !key.Equals(UserControlKey, StringComparison.OrdinalIgnoreCase)
                 && !key.Equals(PropertyPageKey, StringComparison.OrdinalIgnoreCase)
                 && !key.Equals(UserDocumentKey, StringComparison.OrdinalIgnoreCase)
                 && !key.Equals(RelatedDocKey, StringComparison.OrdinalIgnoreCase)
                 && !key.Equals(DesignerKey, StringComparison.OrdinalIgnoreCase)
                 && !key.Equals(ResFileKey, StringComparison.OrdinalIgnoreCase)
                 && !key.Equals(MakePathKey, StringComparison.OrdinalIgnoreCase))
            return null;

        var end = line.Length;
        while (start < end && char.IsWhiteSpace(line[start]))
            start++;
        while (end > start && char.IsWhiteSpace(line[end - 1]))
            end--;
        if (key.Equals(ResFileKey, StringComparison.OrdinalIgnoreCase) || key.Equals(MakePathKey, StringComparison.OrdinalIgnoreCase))
        {
            // Quoted, as VB6 writes both. A value without its quotes is left as it is.
            if (end - start < 2 || line[start] != '"' || line[end - 1] != '"')
                return null;
            start++;
            end--;
        }
        return end > start ? (key, start, end - start) : null;
    }

    /// <summary>The <c>Startup=</c> value naming <c>Sub Main</c> rather than a form. VB6's own spelling,
    /// with the space and that casing; matched case-insensitively on read since the value is a name.</summary>
    public const string SubMainStartup = "Sub Main";
}
