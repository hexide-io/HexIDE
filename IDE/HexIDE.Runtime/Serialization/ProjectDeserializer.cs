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
            if (value.StartsWith('"') && value.EndsWith('"'))
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
                // Format: "Name; relative\path.bas"
                var semi = value.IndexOf(';');
                if (semi >= 0)
                    project.RelativeModulePaths.Add((value[..semi].Trim(), value[(semi + 1)..].Trim(), ModuleKind.StandardModule));
                else
                    project.RelativeModulePaths.Add((value.Trim(), value.Trim(), ModuleKind.StandardModule));
                knownKeyCount++;
            }
            else if (key.Equals(SerializedProject.ClassKey, StringComparison.OrdinalIgnoreCase))
            {
                var semi = value.IndexOf(';');
                if (semi >= 0)
                    project.RelativeModulePaths.Add((value[..semi].Trim(), value[(semi + 1)..].Trim(), ModuleKind.ClassModule));
                else
                    project.RelativeModulePaths.Add((value.Trim(), value.Trim(), ModuleKind.ClassModule));
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
                var refStr = value;
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
    public const string StartupKey = "Startup";
    public const string ReferenceKey = "Reference";
}
