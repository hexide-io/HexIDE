using System;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// The VB6 module-file header for standard (<c>.bas</c>) and class (<c>.cls</c>) modules.
///
/// HexIDE keeps the header OUT of the editable <see cref="ModuleDefinition.Code"/> — mirroring how
/// <c>FormSerializer</c> keeps a form's structural header out of <c>FormDefinition.Code</c> — so the code
/// editor shows only the code body, exactly as the VB6 IDE does (it hides the <c>VERSION</c>/<c>Attribute</c>
/// block). The header is added back on save and stripped on load, so clearing the editor can never produce an
/// unloadable file. UserControl (<c>.ctl</c>) and PropertyPage (<c>.pag</c>) are NOT handled here — they carry
/// a <c>FormPart</c> and round-trip through <c>FormSerializer</c>.
/// </summary>
public static class ModuleFileFormat
{
    /// <summary>True for the kinds whose header this class manages (standard + class modules).</summary>
    public static bool HandlesHeader(ModuleKind kind) =>
        kind is ModuleKind.StandardModule or ModuleKind.ClassModule;

    /// <summary>The canonical VB6 file header (with trailing CRLF) for the given module kind.</summary>
    public static string Header(string name, ModuleKind kind) => kind switch
    {
        ModuleKind.ClassModule =>
            "VERSION 1.0 CLASS\r\n" +
            "BEGIN\r\n" +
            "  MultiUse = -1  'True\r\n" +
            "  Persistable = 0  'NotPersistable\r\n" +
            "  DataBindingBehavior = 0  'vbNone\r\n" +
            "  DataSourceBehavior  = 0  'vbNone\r\n" +
            "  MTSTransactionMode  = 0  'NotAnMTSObject\r\n" +
            "END\r\n" +
            $"Attribute VB_Name = \"{name}\"\r\n" +
            "Attribute VB_GlobalNameSpace = False\r\n" +
            "Attribute VB_Creatable = True\r\n" +
            "Attribute VB_PredeclaredId = False\r\n" +
            "Attribute VB_Exposed = False\r\n",
        ModuleKind.StandardModule =>
            $"Attribute VB_Name = \"{name}\"\r\n",
        _ => "",
    };

    /// <summary>Produce the on-disk file content: header + body. (Body returned as-is for unmanaged kinds.)</summary>
    public static string ToFileContent(string body, string name, ModuleKind kind) =>
        HandlesHeader(kind) ? Header(name, kind) + body : body;

    /// <summary>
    /// Strip a VB6 module header from file content, returning the code body. Idempotent — content with no
    /// recognised header (e.g. an already-stripped body) is returned unchanged.
    /// </summary>
    public static string StripHeader(string fileContent, ModuleKind kind)
    {
        if (!HandlesHeader(kind) || string.IsNullOrEmpty(fileContent))
            return fileContent;

        var lines = fileContent.Replace("\r\n", "\n").Split('\n');
        var i = 0;

        if (kind == ModuleKind.ClassModule
            && i < lines.Length
            && lines[i].TrimStart().StartsWith("VERSION", StringComparison.OrdinalIgnoreCase)
            && lines[i].IndexOf("CLASS", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            i++; // VERSION 1.0 CLASS
            if (i < lines.Length && lines[i].Trim().Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                while (i < lines.Length && !lines[i].Trim().Equals("END", StringComparison.OrdinalIgnoreCase))
                    i++;
                if (i < lines.Length) i++; // consume END
            }
        }

        // Contiguous Attribute lines (VB_Name, VB_PredeclaredId, ...)
        while (i < lines.Length && lines[i].TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase))
            i++;

        if (i == 0)
            return fileContent; // no header recognised — already a body

        return string.Join("\r\n", lines, i, lines.Length - i);
    }
}
