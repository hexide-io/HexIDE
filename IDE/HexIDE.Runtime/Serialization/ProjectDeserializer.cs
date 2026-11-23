using System;
using System.Collections.Generic;
using System.IO;
using System.Globalization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using Serilog;

namespace HexIDE.Runtime.Serialization;

public class ProjectDeserializer
{
    private static VBProjectType ProjectTypeFromString(string type)
    {
        if (type.Equals("exe", StringComparison.OrdinalIgnoreCase))
            return VBProjectType.EXE;
        if (type.Equals("oledll", StringComparison.OrdinalIgnoreCase))
            return VBProjectType.OleDll;
        if (type.Equals("oleexe", StringComparison.OrdinalIgnoreCase))
            return VBProjectType.OleExe;
        if (type.Equals("control", StringComparison.OrdinalIgnoreCase))
            return VBProjectType.Control;

        Log.Warning("Unknown VB6 project type '{ProjectType}', defaulting to EXE", type);
        return VBProjectType.EXE;
    }

    public SerializedProject Deserialize(string source, IDeserializeErrorSink errorSink)
    {
        SerializedProject project = new();
        var sourceOffset = 0;
        int knownKeyCount = 0;

        foreach (var rawLine in source.Split('\n'))
        {
            var lineStart = sourceOffset;
            sourceOffset += rawLine.Length + 1;
            var trimmed = rawLine.Trim();

            // Keep the original section text, including its line endings and unknown values' trailing spaces (#4821).
            if (trimmed.StartsWith('['))
            {
                project.ExtensionTail = source[lineStart..];
                break;
            }

            if (string.IsNullOrWhiteSpace(trimmed))
                continue;

            var parts = trimmed.Split('=', 2);
            if (parts.Length == 0)
                continue;

            var key = parts[0].Trim();
            var value = parts.Length == 2 ? parts[1].Trim() : "";

            // The same value with its TRAILING whitespace intact, for the one key where that whitespace is
            // data: a Reference's name can legitimately end in a space. VB6's own DHTML Application.vbp
            // ships `#MSHTMLPG Control Library ` — trimming it means the file comes back one byte shorter
            // than it went in. Leading whitespace is still dropped; that is indentation, not content.
            //
            // Kept as a separate value rather than loosening the Trim() above, because every other key
            // here wants the trimmed one and a blanket change would start preserving stray spaces
            // wherever an author happened to leave one.
            var rawParts = rawLine.TrimEnd('\r', '\n').Split('=', 2);
            var untrimmedValue = rawParts.Length == 2 ? rawParts[1].TrimStart() : value;
            // Require at least the two quotes: a lone `"` (an unterminated value in a corrupt/truncated .vbp) both
            // starts and ends with `"`, and Substring(1, -1) would throw and abort the whole project open.
            if (value.Length >= 2 && value.StartsWith('"') && value.EndsWith('"'))
                value = value.Substring(1, value.Length - 2).Replace("\"\"", "\"");

            if (key.Equals(SerializedProject.NameKey, StringComparison.OrdinalIgnoreCase))
            {
                project.Name = value;
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.DescriptionKey, StringComparison.OrdinalIgnoreCase))
            {
                project.Description = value;
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.TypeKey, StringComparison.OrdinalIgnoreCase))
            {
                project.ProjectType = ProjectTypeFromString(value);
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.FormKey, StringComparison.OrdinalIgnoreCase))
            {
                project.RelativeFormPaths.Add(value);
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.UserControlKey, StringComparison.OrdinalIgnoreCase))
            {
                var semi = value.IndexOf(';');
                if (semi >= 0)
                    project.RelativeModulePaths.Add((value[..semi].Trim(), value[(semi + 1)..].Trim(), ModuleKind.UserControl));
                else
                    project.RelativeModulePaths.Add((SerializedProject.FileNameWithoutExtensionOf(value.Trim()), value.Trim(), ModuleKind.UserControl));
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.PropertyPageKey, StringComparison.OrdinalIgnoreCase))
            {
                var semi = value.IndexOf(';');
                if (semi >= 0)
                    project.RelativeModulePaths.Add((value[..semi].Trim(), value[(semi + 1)..].Trim(), ModuleKind.PropertyPage));
                else
                    project.RelativeModulePaths.Add((SerializedProject.FileNameWithoutExtensionOf(value.Trim()), value.Trim(), ModuleKind.PropertyPage));
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.UserDocumentKey, StringComparison.OrdinalIgnoreCase))
            {
                var semi = value.IndexOf(';');
                var path = semi >= 0 ? value[(semi + 1)..].Trim() : value.Trim();
                // UserDocuments (ActiveX Documents) are unsupported — still warn — but PRESERVE the line
                // verbatim so the .vbp round-trips intact rather than silently losing the node.
                Log.Warning("UserDocument '{Path}' — ActiveX Documents are not supported in HexIDE; preserving the entry verbatim", path);
                project.SkippedUserDocumentPaths.Add(path);
                project.PreservedItemLines.Add(trimmed);
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.ModuleKey, StringComparison.OrdinalIgnoreCase))
            {
                // Format: "Name; relative\path.ext"
                var semi = value.IndexOf(';');
                var itemName = semi >= 0 ? value[..semi].Trim() : value.Trim();
                var itemPath = semi >= 0 ? value[(semi + 1)..].Trim() : value.Trim();

                if (SerializedProject.IsVb6CodeFile(itemPath))
                {
                    project.RelativeModulePaths.Add((itemName, itemPath, ModuleKind.StandardModule));
                }
                else
                {
                    // A non-code file on a code line. VB6 writes this whenever "Add As Related Document" is
                    // left unticked, which is its default. Treating it as source is not harmless: the save
                    // path prepends an Attribute VB_Name header to it and the Save-As path renames it by
                    // extension. Reclassify — but keep the ORIGINAL LINE, because reclassifying is an
                    // inference about intent and rewriting the project file on an inference is not.
                    Log.Information(
                        "Item '{Path}' on a ModuleKey line is not a VB6 source file; treating it as a related "
                      + "document. Its line is preserved as-is.", itemPath);
                    project.RelativeRelatedDocPaths.Add((itemName, itemPath, trimmed));
                }
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.ClassKey, StringComparison.OrdinalIgnoreCase))
            {
                // Format: "Name; relative\path.ext"
                var semi = value.IndexOf(';');
                var itemName = semi >= 0 ? value[..semi].Trim() : value.Trim();
                var itemPath = semi >= 0 ? value[(semi + 1)..].Trim() : value.Trim();

                if (SerializedProject.IsVb6CodeFile(itemPath))
                {
                    project.RelativeModulePaths.Add((itemName, itemPath, ModuleKind.ClassModule));
                }
                else
                {
                    // A non-code file on a code line. VB6 writes this whenever "Add As Related Document" is
                    // left unticked, which is its default. Treating it as source is not harmless: the save
                    // path prepends an Attribute VB_Name header to it and the Save-As path renames it by
                    // extension. Reclassify — but keep the ORIGINAL LINE, because reclassifying is an
                    // inference about intent and rewriting the project file on an inference is not.
                    Log.Information(
                        "Item '{Path}' on a ClassKey line is not a VB6 source file; treating it as a related "
                      + "document. Its line is preserved as-is.", itemPath);
                    project.RelativeRelatedDocPaths.Add((itemName, itemPath, trimmed));
                }
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.RelatedDocKey, StringComparison.OrdinalIgnoreCase))
            {
                // Format: "RelatedDoc=relative\path.md" — no "Name; " prefix, unlike every other item key.
                var path = value.Trim();
                project.RelativeRelatedDocPaths.Add(
                    (SerializedProject.FileNameOf(path), path, null));
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.StartupKey, StringComparison.OrdinalIgnoreCase))
            {
                project.StartupFormName = value;
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.ReferenceKey, StringComparison.OrdinalIgnoreCase))
            {
                // Format: *\G{GUID}#Version#LCID#LibPath#Name (LibPath and Name may both be empty).
                var refStr = untrimmedValue;
                if (refStr.StartsWith("*\\G", StringComparison.OrdinalIgnoreCase))
                    refStr = refStr[3..];
                var refParts = refStr.Split('#', 5);
                if (refParts.Length >= 3 && int.TryParse(refParts[2], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var lcid))
                {
                    var path = refParts.Length >= 4 && !string.IsNullOrEmpty(refParts[3]) ? refParts[3] : null;
                    var name = refParts.Length >= 5 && !string.IsNullOrEmpty(refParts[4]) ? refParts[4] : null;
                    project.References.Add(new VbReference(refParts[0], refParts[1], lcid, path, name));
                    knownKeyCount++;
                }
                else
                {
                    // Unrecognised Reference format: preserve the raw line verbatim rather than dropping it,
                    // so a .vbp whose dependency line we can't parse is never corrupted on save. (Treated
                    // like any other unknown line — no knownKeyCount bump.)
                    Log.Warning("Could not parse Reference line; preserving verbatim: {Value}", value);
                    project.UnknownPreSectionLines.Add((knownKeyCount, trimmed));
                }
            }
            else
            {
                // Unknown key — preserve verbatim for round-trip
                project.UnknownPreSectionLines.Add((knownKeyCount, trimmed));
            }
            if (knownKeyCount > project.KnownLineKeys.Count)
            {
                var slot = project.KnownLineKeys.Count;
                project.KnownLineKeys.Add(key);
                if (project.RelativeFormPaths.Count > project.FormLineSlots.Count)
                    project.FormLineSlots.Add(slot);
                if (project.RelativeModulePaths.Count > project.ModuleLineSlots.Count)
                    project.ModuleLineSlots.Add(slot);
                if (project.RelativeRelatedDocPaths.Count > project.RelatedDocumentLineSlots.Count)
                    project.RelatedDocumentLineSlots.Add(slot);
                if (project.PreservedItemLines.Count > project.PreservedItemLineSlots.Count)
                    project.PreservedItemLineSlots.Add(slot);
            }
        }

        if (project.Description is null)
            MakeRoomForDescription(project.UnknownPreSectionLines, knownKeyCount);

        return project;
    }

    /// <summary>
    /// Leaves a slot for a <c>Description=</c> line in a file that has none, after its <c>HelpContextID</c> line, or
    /// straight after the last known key when there is no such line. VB6 writes it there (#42).
    /// </summary>
    /// <remarks>
    /// The writer puts a known key after the unknown lines whose hint is at or below its count. In a file with no
    /// description, every unknown line after the last known key has the same hint, so a description typed in later
    /// would follow all of them, <c>MajorVer</c> and the rest included. Raising the hint of the lines after
    /// <c>HelpContextID</c> by one splits that run where the line belongs. With no description to write, the writer
    /// emits the run unchanged, in the same order, so an untouched file still saves byte for byte. A file that has a
    /// description already splits the run where it stands, so nothing is raised.
    /// </remarks>
    private static void MakeRoomForDescription(List<(int PositionHint, string RawLine)> unknowns, int last)
    {
        var helpContext = unknowns.FindIndex(u => u.PositionHint == last
            && u.RawLine.StartsWith(SerializedProject.HelpContextIdKey + "=", StringComparison.OrdinalIgnoreCase));
        for (var i = helpContext + 1; i < unknowns.Count; i++)
            if (unknowns[i].PositionHint == last)
                unknowns[i] = (last + 1, unknowns[i].RawLine);
    }
}

public class SerializedProject
{
    // Recognised keys in read order; item lists retain their source slot even if loading reclassifies them.
    public List<string> KnownLineKeys { get; } = new();
    public List<int> FormLineSlots { get; } = new();
    public List<int> ModuleLineSlots { get; } = new();
    public List<int> RelatedDocumentLineSlots { get; } = new();
    public List<int> PreservedItemLineSlots { get; } = new();

    public VBProjectType ProjectType { get; set; }
    public string? Name { get; set; }
    public string? Description { get; set; }
    public List<string> RelativeFormPaths { get; } = new();
    public List<(string Name, string Path, ModuleKind Kind)> RelativeModulePaths { get; } = new();

    /// <summary>
    /// Files the project carries but does not compile. <c>OriginalItemLine</c> is non-null only where the
    /// entry was reclassified from a <c>Module=</c>/<c>Class=</c> line, so the writer can put that line back
    /// exactly as it found it rather than rewriting a project file on the strength of a guess.
    /// </summary>
    public List<(string Name, string Path, string? OriginalItemLine)> RelativeRelatedDocPaths { get; } = new();

    /// <summary>
    /// Extensions a <c>Module=</c> or <c>Class=</c> line may legitimately point at. Anything else on one of
    /// those lines is a non-code file VB6 added with its "Add As Related Document" tickbox left off — the
    /// tickbox is not sticky and defaults off, so this is the common case rather than the odd one.
    /// </summary>
    private static readonly HashSet<string> Vb6CodeExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".bas", ".cls", ".ctl", ".pag", ".frm", ".dob", ".dsr" };

    /// <summary>True when an item path names a file VB6 would compile.</summary>
    public static bool IsVb6CodeFile(string path)
    {
        // FileNameOf first: on a host where backslash is not a separator, GetExtension("my.dir\README")
        // returns ".dir\README" — a directory's dot decides the answer for a file that has no extension.
        var extension = System.IO.Path.GetExtension(FileNameOf(path));
        // No extension at all is left alone: reclassifying something we cannot classify would be a guess on
        // top of a guess, and the conservative reading keeps it a module.
        return extension.Length == 0 || Vb6CodeExtensions.Contains(extension);
    }
    public List<VbReference> References { get; } = new();
    public List<string> SkippedUserDocumentPaths { get; } = new();

    // Verbatim item lines (e.g. UserDocument=) for parsed-but-unmodelled project items, carried so a
    // round-trip never drops the node. (Missing-file forms are added to this list by the loader, which
    // is the layer that knows whether a file exists on disk.)
    public List<string> PreservedItemLines { get; } = new();

    public string? StartupFormName { get; set; }

    // Unknown key=value lines that appeared before any [Section] header.
    // PositionHint = count of recognised keys seen before this line.
    public List<(int PositionHint, string RawLine)> UnknownPreSectionLines { get; } = new();

    // Everything from the first [SectionName] line to EOF, preserved verbatim.
    public string? ExtensionTail { get; set; }

    public const string NameKey = "Name";

    /// <summary>
    /// The project's description, as Project Properties sets it. Measured with VB6's Save Project As: written quoted,
    /// a quote inside doubled, after <c>HelpContextID</c> and before <c>CompatibleMode</c>, and not written at all when
    /// there is none (#42).
    /// </summary>
    public const string DescriptionKey = "Description";

    /// <summary>Not modelled, and kept as read. VB6 writes it between <c>Name</c> and <c>Description</c>.</summary>
    public const string HelpContextIdKey = "HelpContextID";
    public const string TypeKey = "Type";
    public const string FormKey = Vb6ProjectPaths.FormKey;
    public const string ModuleKey = Vb6ProjectPaths.ModuleKey;
    public const string ClassKey = Vb6ProjectPaths.ClassKey;
    public const string UserControlKey = Vb6ProjectPaths.UserControlKey;
    public const string PropertyPageKey = Vb6ProjectPaths.PropertyPageKey;
    public const string UserDocumentKey = Vb6ProjectPaths.UserDocumentKey;
    public const string RelatedDocKey = Vb6ProjectPaths.RelatedDocKey;
    public const string DesignerKey = Vb6ProjectPaths.DesignerKey;
    public const string ResFileKey = Vb6ProjectPaths.ResFileKey;
    public const string MakePathKey = Vb6ProjectPaths.MakePathKey;

    public static string FileNameOf(string projectFilePath) => Vb6ProjectPaths.FileNameOf(projectFilePath);
    public static string FileNameWithoutExtensionOf(string projectFilePath) => Vb6ProjectPaths.FileNameWithoutExtensionOf(projectFilePath);
    public static string ToHostPath(string projectFilePath) => Vb6ProjectPaths.ToHostPath(projectFilePath);
    public static string ToProjectFilePath(string hostRelativePath) => Vb6ProjectPaths.ToProjectFilePath(hostRelativePath);
    public static (string Key, int Start, int Length)? ItemLineFile(string line) => Vb6ProjectPaths.ItemLineFile(line);

    /// <summary>
    /// <paramref name="line"/>, a member's line in a project file in <paramref name="fromDirectory"/>, as it must read
    /// in one in <paramref name="toDirectory"/>: its file resolved against the first folder and named relative to the
    /// second, in the line's own shape (#737). The member's file stays where it is. The line comes back as it was when
    /// the two folders are one, when it names no file, and when its path names a drive or starts at a root, which no
    /// change of folder moves.
    /// </summary>
    public static string RebaseItemLine(string line, string fromDirectory, string toDirectory)
    {
        if (fromDirectory.Length == 0 || toDirectory.Length == 0 || SameDirectory(fromDirectory, toDirectory)
            || ItemLineFile(line) is not { } file)
            return line;

        var path = line.Substring(file.Start, file.Length);
        var rebased = RebasePath(path, fromDirectory, toDirectory);
        return ReferenceEquals(rebased, path) ? line : line[..file.Start] + rebased + line[(file.Start + file.Length)..];
    }

    /// <summary>
    /// <paramref name="path"/>, written in a project file in <paramref name="fromDirectory"/>, as one in
    /// <paramref name="toDirectory"/> must write it to name the same file. <paramref name="path"/> itself when the folders
    /// are one, when it is empty, and when it names a drive or starts at a root.
    /// </summary>
    public static string RebasePath(string path, string fromDirectory, string toDirectory)
    {
        if (path.Length == 0 || fromDirectory.Length == 0 || toDirectory.Length == 0
            || SameDirectory(fromDirectory, toDirectory))
            return path;
        // Tested on the path as the file wrote it, not the host's reading of it: on Linux `C:\x` is not rooted, and
        // joining it to a folder would invent a file.
        if (path[0] is '\\' or '/' || (path.Length >= 2 && path[1] == ':'))
            return path;

        var resolved = System.IO.Path.GetFullPath(System.IO.Path.Join(fromDirectory, ToHostPath(path)));
        return ToProjectFilePath(System.IO.Path.GetRelativePath(toDirectory, resolved));
    }

    // Reference paths on the project's drive are relative even when the input was absolute (#2274).
    // Parse drive paths independently of the host: Linux must save Windows projects the same way.
    public static string ReferencePath(string path, string fromDirectory, string toDirectory)
    {
        static bool DrivePath(string value) => value.Length >= 3 && char.IsAsciiLetter(value[0])
            && value[1] == ':' && value[2] is '\\' or '/';
        if (DrivePath(path) && DrivePath(toDirectory)
            && char.ToUpperInvariant(path[0]) == char.ToUpperInvariant(toDirectory[0]))
        {
            var source = new Uri(path.Replace('\\', '/'));
            var destination = new Uri(toDirectory.Replace('\\', '/').TrimEnd('/') + "/");
            return Uri.UnescapeDataString(destination.MakeRelativeUri(source).ToString()).Replace('/', '\\');
        }
        if (System.IO.Path.IsPathFullyQualified(path) && !DrivePath(path) && !path.StartsWith(@"\\")
            && !DrivePath(toDirectory))
            return ToProjectFilePath(System.IO.Path.GetRelativePath(toDirectory, path));
        return RebasePath(path, fromDirectory, toDirectory);
    }

    private static bool SameDirectory(string a, string b) =>
        string.Equals(
            System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(a)),
            System.IO.Path.TrimEndingDirectorySeparator(System.IO.Path.GetFullPath(b)),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    public const string StartupKey = "Startup";

    public const string SubMainStartup = Vb6ProjectPaths.SubMainStartup;
    public const string ReferenceKey = "Reference";
}
