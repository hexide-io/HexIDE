namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What the running program knows about itself: the project identity behind <see cref="VbApp"/>.
/// </summary>
/// <param name="Title">
/// The <c>.vbp</c> <c>Title=</c> value, falling back to <c>Name=</c> when the key is absent — measured, see
/// the App section of <c>docs/vb6-fidelity-oracle.md</c>. Quotes already stripped.
/// </param>
/// <param name="ExeName">
/// The <c>.vbp</c> file's own name without its extension. Not the project <c>Name</c>, and not
/// <c>ExeName32</c>: at design time VB6 reports the project FILE, which is the one row here that a
/// reasonable guess gets wrong.
/// </param>
/// <param name="Path">The folder containing the project file, with no trailing separator (VB6 keeps none).</param>
public record AppInfo(
    string Title = "",
    string ExeName = "",
    string Path = "",
    int Major = 1,
    int Minor = 0,
    int Revision = 0,
    string CompanyName = "",
    string FileDescription = "",
    string LegalCopyright = "",
    string LegalTrademarks = "",
    string Comments = "")
{
    /// <summary>A program with no project behind it — the test harness and the bare interpreter.</summary>
    public static readonly AppInfo None = new();

    /// <summary>
    /// Derive what a running program reports about itself from the project it belongs to, using VB6's
    /// <b>design-time</b> rules (see the App section of <c>docs/vb6-fidelity-oracle.md</c>).
    /// </summary>
    public static AppInfo FromProject(ProjectElements.ProjectDefinition project)
    {
        var path = project.AbsolutePath;

        return new AppInfo(
            // Title= if the project has one, else the project Name — measured against a compiled probe
            // with no Title key, which reported Name.
            Title: PreSectionValue(project, "Title") ?? project.Name,

            // The PROJECT FILE's name, not project.Name and not ExeName32. Measured under F5 with all
            // three set to different strings; VB6 returned the .vbp's own filename.
            ExeName: path is null ? "" : System.IO.Path.GetFileNameWithoutExtension(path),

            Path: path is null ? "" : System.IO.Path.GetDirectoryName(path) ?? "",

            Major: PreSectionInt(project, "MajorVer") ?? 1,
            Minor: PreSectionInt(project, "MinorVer") ?? 0,
            Revision: PreSectionInt(project, "RevisionVer") ?? 0,
            CompanyName: PreSectionValue(project, "VersionCompanyName") ?? "",
            FileDescription: PreSectionValue(project, "VersionFileDescription") ?? "",
            LegalCopyright: PreSectionValue(project, "VersionLegalCopyright") ?? "",
            LegalTrademarks: PreSectionValue(project, "VersionLegalTrademarks") ?? "",
            Comments: PreSectionValue(project, "VersionComments") ?? "");
    }

    /// <summary>
    /// Read a <c>key=value</c> line the project loader kept verbatim.
    /// </summary>
    /// <remarks>
    /// These keys are deliberately read from the preserved lines rather than promoted into the project
    /// model. Modelling a key means the writer owns emitting it, and the <c>.vbp</c> currently round-trips
    /// byte-for-byte against VB6's own files — not worth risking that gate to read a caption.
    /// </remarks>
    private static string? PreSectionValue(ProjectElements.ProjectDefinition project, string key)
    {
        foreach (var (_, raw) in project.UnknownPreSectionLines)
        {
            var line = raw.TrimStart();
            if (!line.StartsWith(key + "=", System.StringComparison.OrdinalIgnoreCase)) continue;

            var value = line[(key.Length + 1)..].Trim();

            // VB6 writes this key quoted — `Title="Project1"` appears in its own templates — and its own
            // reader then strips the LEADING quote only, so a real VB6 app carries a stray `"` at the end
            // of App.Title. That is a defect rather than a design, so both quotes come off here. Recorded
            // as a deliberate divergence in docs/vb6-fidelity-oracle.md.
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            else if (value.Length >= 1 && value[0] == '"')
                value = value[1..];

            return value;
        }
        return null;
    }

    private static int? PreSectionInt(ProjectElements.ProjectDefinition project, string key)
        => int.TryParse(PreSectionValue(project, key), out var n) ? n : null;
}

/// <summary>
/// The global <c>App</c> object.
///
/// <para>Every value here is HexIDE's permanent situation — <b>design time</b>. VB6's own answers differ
/// between F5 and a compiled exe, and HexIDE's interpreter is always in F5's position: there is no built
/// executable to read a version resource from. So <c>EXEName</c> is the project file's name rather than an
/// exe's, and <c>ProductName</c> is empty rather than following <c>Title</c>, because that is precisely
/// what VB6 reports when no exe exists. Both were measured rather than assumed.</para>
///
/// <para><c>Title</c> is writable at run time, as in VB6; everything else is read-only and a write is
/// ignored rather than throwing, matching how the member-write path treats an unknown property.</para>
/// </summary>
public class VbApp(AppInfo info) : ICSharpProxy, ICSharpPropertyBag
{
    /// <summary>App has methods in VB6 (LogEvent, StartLogging); none are implemented, so every call is a
    /// clean "member not found" rather than a silent no-op.</summary>
    public void Call(string method, System.Collections.Generic.List<Vb6Value> args)
        => throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "App." + method);

    private string title = info.Title;

    /// <summary>
    /// The caption <c>MsgBox</c> / <c>InputBox</c> use when the caller omits a Title — null when there is
    /// no project behind the program, so "omitted" stays omitted rather than becoming a blank caption.
    /// </summary>
    public string? TitleOrNull => string.IsNullOrEmpty(title) ? null : title;

    public bool TryGetProperty(string name, out Vb6Value value)
    {
        switch (name.ToLowerInvariant())
        {
            case "title": value = title; return true;
            case "exename": value = info.ExeName; return true;
            case "path": value = info.Path; return true;

            // Empty by design, not by omission: the version resource lives in a compiled exe, and there
            // isn't one. VB6 under F5 returns empty here too.
            case "productname": value = ""; return true;

            case "major": value = info.Major; return true;
            case "minor": value = info.Minor; return true;
            case "revision": value = info.Revision; return true;
            case "companyname": value = info.CompanyName; return true;
            case "filedescription": value = info.FileDescription; return true;
            case "legalcopyright": value = info.LegalCopyright; return true;
            case "legaltrademarks": value = info.LegalTrademarks; return true;
            case "comments": value = info.Comments; return true;

            // False under F5 in VB6: the IDE runs one instance of your program. Measured.
            case "previnstance": value = false; return true;

            default: value = default; return false;
        }
    }

    public bool TrySetProperty(string name, Vb6Value value)
    {
        if (!string.Equals(name, "Title", System.StringComparison.OrdinalIgnoreCase))
            return false;
        title = value.Value?.ToString() ?? "";
        return true;
    }
}
