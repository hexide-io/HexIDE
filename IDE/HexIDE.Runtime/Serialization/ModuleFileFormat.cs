using System;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

/// <summary>
/// The VB6 module-file header for standard (<c>.bas</c>) and class (<c>.cls</c>) modules.
/// </summary>
/// <remarks>
/// <para>
/// <b>The model keeps the header OUT of <see cref="ModuleDefinition.Code"/></b>, as <c>FormSerializer</c>
/// keeps a form's designer block out of <c>FormDefinition.Code</c>: <see cref="SplitHeader"/> takes it off at
/// load into <see cref="ModuleDefinition.OriginalHeader"/>, and <see cref="ToFileContent"/> puts it back in
/// front on save.
/// </para>
/// <para>
/// <b>The code window does not hide it.</b> It shows the whole file: <see cref="BufferHeader"/> composes the
/// header back in front of the body (<c>FormCodeText.Prefix</c>), greyed and read-only, and folded when it is
/// more than one line; the flush splits it off again. So nothing typed in the window reaches it, and clearing
/// the window cannot produce an unloadable file. This used to say the editor showed only the body, as VB6's
/// code window did. Since hexide-io/HexIDE#273 it shows the header, so that a line number means the same thing
/// to the code window, a language server, the interpreter and the debugger; that divergence from VB6 is
/// deliberate.
/// </para>
/// <para>
/// UserControl (<c>.ctl</c>) and PropertyPage (<c>.pag</c>) are NOT handled here — they carry a
/// <c>FormPart</c> and round-trip through <c>FormSerializer</c>.
/// </para>
/// </remarks>
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
    ///
    /// The header is all this touches. A member's <c>Attribute</c> lines are in <paramref name="body"/>, and a
    /// save that VB6 would also make re-qualifies them with their members' names before it gets here
    /// (<c>ProjectService</c>'s <c>RewrittenQualifiers</c>, hexide-io/HexIDE#273 task 4.4).
    /// </summary>
    public static string ToFileContent(string body, string name, ModuleKind kind, string? preservedHeader = null)
    {
        if (!HandlesHeader(kind))
            return body;

        if (!string.IsNullOrEmpty(preservedHeader))
            return RetargetVbName(preservedHeader, name) + body;

        // Nothing split off. A body that already carries its own VB_Name is the whole file, in a shape the reader did not
        // take as a header, and gets nothing in front: the literal would give it a second header, which is #472. One
        // with none is a module HexIDE is creating, or a file written without a header, and VB6 writes one for both.
        return CarriesVbName(body) ? body : Header(name, kind) + body;
    }

    /// <summary>Whether any line of <paramref name="text"/> is an <c>Attribute VB_Name</c> line.</summary>
    private static bool CarriesVbName(string text)
    {
        foreach (var line in text.Split('\n'))
            if (line.TrimStart().StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase))
                return true;
        return false;
    }

    /// <summary>
    /// The header the code window puts in front of this module's body, which is <b>not</b> always the one
    /// <see cref="ToFileContent"/> writes.
    /// </summary>
    /// <remarks>
    /// <b>Null and empty mean different things here.</b> <c>""</c> means the reader split nothing off, so
    /// <c>Code</c> holds the whole file. <c>ToFileContent</c> decides by the body instead. It writes the canonical
    /// literal in front of a body with no <c>VB_Name</c> of its own, as VB6 gives a headerless file one when it saves
    /// it, and nothing in front of a body that carries one (hexide-io/HexIDE#472). So the two differ only for a
    /// file with no <c>VB_Name</c> at all, by the line its next save adds.
    ///
    /// <list type="bullet">
    /// <item><description><c>null</c> — never read from disk. A module HexIDE created, which has no
    /// original to preserve, so the canonical literal is what its file will say.</description></item>
    /// <item><description><c>""</c> — read, and nothing was split off. The body already IS the file, so
    /// the buffer must add nothing in front of it or the developer sees the header twice.</description></item>
    /// <item><description>anything else — the preserved header, with <c>VB_Name</c> retargeted so a rename
    /// still round-trips.</description></item>
    /// </list>
    /// </remarks>
    public static string BufferHeader(string name, ModuleKind kind, string? preservedHeader) =>
        !HandlesHeader(kind) ? ""
        : preservedHeader is null ? Header(name, kind)
        : preservedHeader.Length == 0 ? ""
        : RetargetVbName(preservedHeader, name);

    /// <summary>
    /// Rewrites the <c>Attribute VB_Name</c> line to say <paramref name="name"/>, so the header follows the
    /// module's name rather than the one it was read with. No gesture renames a module yet
    /// (hexide-io/HexIDE#493).
    /// </summary>
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

        // Blank lines above the header belong to it, so the header is found below them and written back with them
        // (#472). Recognition used to test line 0 alone, so one blank line left the whole file in the body, and the
        // next save put a second header in front of it.
        var start = 0;
        while (start < lines.Length && lines[start].Trim().Length == 0)
            start++;
        var i = start;

        if (kind == ModuleKind.ClassModule
            && i < lines.Length
            && lines[i].TrimStart().StartsWith("VERSION", StringComparison.OrdinalIgnoreCase)
            && lines[i].IndexOf("CLASS", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            i++; // VERSION 1.0 CLASS
            if (i < lines.Length && lines[i].Trim().Equals("BEGIN", StringComparison.OrdinalIgnoreCase))
            {
                i++;
                // A BEGIN with no END ends where the attributes begin, rather than at the end of the file: run to EOF,
                // it took the whole class for its header and left the code window empty (#472).
                while (i < lines.Length && !lines[i].Trim().Equals("END", StringComparison.OrdinalIgnoreCase)
                       && !lines[i].TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase))
                    i++;
                if (i < lines.Length && lines[i].Trim().Equals("END", StringComparison.OrdinalIgnoreCase)) i++; // consume END
            }
        }

        // Contiguous Attribute lines (VB_Name, VB_PredeclaredId, ...)
        while (i < lines.Length && lines[i].TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase))
            i++;

        if (i == start)
            return ("", fileContent); // no header recognised — already a body, blank lines and all

        // A header that runs to the end of the file already ends with the file's own last line break, if it has one, so
        // nothing is added after it: adding one grew the file by a blank line on every save.
        return (string.Join("\r\n", lines, 0, i) + (i < lines.Length ? "\r\n" : ""),
                string.Join("\r\n", lines, i, lines.Length - i));
    }
}
