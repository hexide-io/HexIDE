using System;
using System.IO;
using System.Linq;
using HexIDE.Runtime.ProjectElements;
using Path = System.IO.Path;

namespace HexIDE.Runtime.Serialization;

public class ProjectSerializer
{
    private static string ProjectTypeToString(VBProjectType type) => type switch
    {
        VBProjectType.EXE     => "EXE",
        VBProjectType.OleDll  => "OleDll",
        VBProjectType.OleExe  => "OleExe",
        VBProjectType.Control => "Control",
        _                     => throw new ArgumentOutOfRangeException(nameof(type), type, null)
    };

    public string Serialize(ProjectDefinition project, string projectPath)
    {
        var writer = new StringWriter { NewLine = "\r\n" };
        // Sort by PositionHint so interleaving is correct even if unknowns were appended out of order
        // (e.g. by the loader). The trailing-remainder loop already guarantees none are lost; this
        // hardens their ORDER. OrderBy is stable, so equal-hint lines keep their insertion order.
        var unknowns = project.UnknownPreSectionLines.OrderBy(u => u.PositionHint).ToList();
        int knownKeyCount = 0;
        int unknownIdx = 0;

        void WriteKnownLine(string line)
        {
            // Interleave unknown lines whose position hint falls at or before this known key
            while (unknownIdx < unknowns.Count && unknowns[unknownIdx].PositionHint <= knownKeyCount)
                writer.WriteLine(unknowns[unknownIdx++].RawLine);
            writer.WriteLine(line);
            knownKeyCount++;
        }

        WriteKnownLine($"{SerializedProject.NameKey}=\"{(project.Name ?? "").Replace("\"", "\"\"")}\"");
        WriteKnownLine($"{SerializedProject.TypeKey}={ProjectTypeToString(project.ProjectType)}");

        foreach (var form in project.Forms)
        {
            if (form.AbsolutePath == null)
                continue;
            var relativePath = Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, form.AbsolutePath);
            WriteKnownLine($"{SerializedProject.FormKey}={relativePath}");
        }

        foreach (var module in project.Modules)
        {
            if (module.AbsolutePath == null)
                continue;
            var relativePath = Path.GetRelativePath(Path.GetDirectoryName(projectPath)!, module.AbsolutePath);
            var key = module.Kind switch
            {
                ModuleKind.ClassModule  => SerializedProject.ClassKey,
                ModuleKind.UserControl  => SerializedProject.UserControlKey,
                ModuleKind.PropertyPage => SerializedProject.PropertyPageKey,
                _                       => SerializedProject.ModuleKey
            };
            WriteKnownLine($"{key}={module.Name}; {relativePath}");
        }

        // Preserved item lines (unsupported UserDocuments, missing-file forms): emit verbatim so the
        // round-trip never drops a node. Not modelled, so written raw rather than via WriteKnownLine.
        foreach (var preserved in project.PreservedItemLines)
            writer.WriteLine(preserved);

        // Startup form name survives even when the form itself couldn't be loaded (missing .frm), so the
        // Startup= line is never dropped. The live StartupForm (if any) takes precedence over the fallback.
        var startupName = project.StartupForm?.Name ?? project.StartupFormName;
        if (!string.IsNullOrEmpty(startupName))
            WriteKnownLine($"{SerializedProject.StartupKey}={startupName}");

        foreach (var r in project.References)
        {
            // VB6 reference format: *\G{GUID}#Version#LCID#LibPath#Name. The trailing Name is REQUIRED:
            // vb6.exe /make reports "could not be loaded" for a name-less reference even when the GUID
            // resolves via the registry. LibPath may be empty — VB6 then resolves the typelib from the GUID.
            var path = r.LibPath ?? "";
            var name = r.Name ?? "";
            WriteKnownLine($"{SerializedProject.ReferenceKey}=*\\G{r.Guid}#{r.Version}#{r.Lcid}#{path}#{name}");
        }

        // Emit any remaining unknown lines that appeared after all known keys
        while (unknownIdx < unknowns.Count)
            writer.WriteLine(unknowns[unknownIdx++].RawLine);

        // Append the extension tail (third-party [SectionName]...EOF content)
        if (!string.IsNullOrEmpty(project.ExtensionTail))
        {
            writer.WriteLine();
            writer.Write(project.ExtensionTail);
        }

        return writer.ToString();
    }
}
