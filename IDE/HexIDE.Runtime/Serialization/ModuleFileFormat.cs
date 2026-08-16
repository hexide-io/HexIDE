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

    /// <summary>
    /// Produce the on-disk file content: header + body. (Body returned as-is for unmanaged kinds.)
    ///
    /// <paramref name="preservedHeader"/> is the header exactly as read from disk. When supplied it is
    /// re-emitted verbatim — only <c>Attribute VB_Name</c> is retargeted, so a rename still works. That
    /// matters because <see cref="Header"/> is a fixed literal: regenerating from it resets VB_Exposed,
    /// VB_Creatable, MultiUse, DataBindingBehavior and DataSourceBehavior, which are how VB6 encodes a
    /// class's Instancing. Rewriting them changes what the class *is* to every consumer.
    ///
    /// Verbatim rather than a parsed model on purpose: it also preserves each line's comment text
    /// (<c>0  'NotPersistable</c>), the key order, the *absence* of keys VB6 omitted, and any attribute
    /// beyond the five in the literal — <c>VB_Description</c> among them.
    ///
    /// Pass null only for a module HexIDE is creating, which has no original to preserve.
    /// </summary>
    public static string ToFileContent(string body, string name, ModuleKind kind, string? preservedHeader = null)
    {
        if (!HandlesHeader(kind))
            return body;

        return string.IsNullOrEmpty(preservedHeader)
            ? Header(name, kind) + body
            : RetargetVbName(preservedHeader, name) + body;
    }

    /// <summary>Rewrites the <c>Attribute VB_Name</c> line so a renamed module still round-trips.</summary>
    private static string RetargetVbName(string header, string name)
    {
        var lines = header.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            if (!lines[i].TrimStart().StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase))
                continue;
            lines[i] = $"Attribute VB_Name = \"{name}\"";
            break;
        }
        return string.Join("\r\n", lines);
    }

    /// <summary>
    /// Strip a VB6 module header from file content, returning the code body. Idempotent — content with no
    /// recognised header (e.g. an already-stripped body) is returned unchanged.
    /// </summary>
    public static string StripHeader(string fileContent, ModuleKind kind) =>
        SplitHeader(fileContent, kind).Body;

    /// <summary>
    /// Split file content into its verbatim header and its body. <c>Header</c> is empty when no header was
    /// recognised, which is also the signal that there is nothing to preserve.
    /// </summary>
    public static (string Header, string Body) SplitHeader(string fileContent, ModuleKind kind)
    {
        if (!HandlesHeader(kind) || string.IsNullOrEmpty(fileContent))
            return ("", fileContent);

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
            return ("", fileContent); // no header recognised — already a body

        return (string.Join("\r\n", lines, 0, i) + "\r\n",
                string.Join("\r\n", lines, i, lines.Length - i));
    }
}
