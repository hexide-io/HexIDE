namespace HexIDE.Runtime.ProjectElements;

/// <summary>
/// A control library a project or a form names on an <c>Object=</c> line (#43, #1082): its type library's GUID, the
/// version the line asks for, and its file. Read from the line, which is kept as written; nothing writes one from this.
/// </summary>
/// <remarks>
/// The two files spell it differently. A <c>.vbp</c> writes <c>Object={831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0; MSCOMCTL.OCX</c>
/// and a form's header <c>Object = "{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0"; "MSCOMCTL.OCX"</c>. A UserControl in another
/// project of the group is named by its project instead (<c>Object=*\A..\Ctl\Ctl.vbp</c>), and is not one of these.
/// </remarks>
public readonly record struct ObjectReference(string Guid, string Version, string File)
{
    /// <summary>The reference on <paramref name="line"/>, which is a <c>.vbp</c> line or a form's header line.</summary>
    public static bool TryParse(string line, out ObjectReference reference)
    {
        reference = default;
        var text = line.Trim();
        if (!text.StartsWith("Object", StringComparison.OrdinalIgnoreCase))
            return false;
        text = text["Object".Length..].TrimStart();
        if (!text.StartsWith('='))
            return false;
        var semicolon = text.IndexOf(';');
        if (semicolon < 0)
            return false;
        var identity = text[1..semicolon].Trim().Trim('"');
        var file = text[(semicolon + 1)..].Trim().Trim('"');
        if (!identity.StartsWith('{'))
            return false;
        var close = identity.IndexOf('}');
        var hash = identity.IndexOf('#', Math.Max(close, 0));
        if (close < 0 || hash < 0)
            return false;
        var version = identity[(hash + 1)..];
        var lcid = version.IndexOf('#');
        reference = new ObjectReference(identity[..(close + 1)], lcid < 0 ? version : version[..lcid], file);
        return true;
    }

    /// <summary>Whether this names the type library <paramref name="guid"/>, written with its braces or without.</summary>
    public bool Names(string guid) =>
        string.Equals(Guid.Trim('{', '}'), guid.Trim('{', '}'), StringComparison.OrdinalIgnoreCase);
}
