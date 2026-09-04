using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
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
        var extensionTailBuilder = new StringBuilder();
        bool inExtensionSection = false;
        int knownKeyCount = 0;

        foreach (var rawLine in source.Split('\n'))
        {
            // Once we hit an INI-style section header, everything to EOF is a verbatim extension tail
            if (inExtensionSection)
            {
                extensionTailBuilder.AppendLine(rawLine.TrimEnd('\r'));
                continue;
            }

            var trimmed = rawLine.Trim();

            if (trimmed.StartsWith('['))
            {
                inExtensionSection = true;
                extensionTailBuilder.AppendLine(rawLine.TrimEnd('\r'));
                continue;
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
                    project.RelativeModulePaths.Add((Path.GetFileNameWithoutExtension(value.Trim()), value.Trim(), ModuleKind.UserControl));
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.PropertyPageKey, StringComparison.OrdinalIgnoreCase))
            {
                var semi = value.IndexOf(';');
                if (semi >= 0)
                    project.RelativeModulePaths.Add((value[..semi].Trim(), value[(semi + 1)..].Trim(), ModuleKind.PropertyPage));
                else
                    project.RelativeModulePaths.Add((Path.GetFileNameWithoutExtension(value.Trim()), value.Trim(), ModuleKind.PropertyPage));
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
                if (refParts.Length >= 3 && int.TryParse(refParts[2], out var lcid))
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
        }

        if (extensionTailBuilder.Length > 0)
            project.ExtensionTail = extensionTailBuilder.ToString().TrimEnd();

        return project;
    }
}

public class SerializedProject
{
    public VBProjectType ProjectType { get; set; }
    public string? Name { get; set; }
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
        var extension = System.IO.Path.GetExtension(path);
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
    public const string TypeKey = "Type";
    public const string FormKey = "Form";
    public const string ModuleKey = "Module";
    public const string ClassKey = "Class";
    public const string UserControlKey = "UserControl";
    public const string PropertyPageKey = "PropertyPage";
    public const string UserDocumentKey = "UserDocument";
    public const string RelatedDocKey = "RelatedDoc";

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
    public const string StartupKey = "Startup";

    /// <summary>The <c>Startup=</c> value naming <c>Sub Main</c> rather than a form. VB6's own spelling,
    /// with the space and that casing; matched case-insensitively on read since the value is a name.</summary>
    public const string SubMainStartup = "Sub Main";
    public const string ReferenceKey = "Reference";
}
