using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace HexIDE.Runtime.Interpreter;

/// <summary>
/// What the running program knows about itself: the project identity behind <see cref="VbApp"/>.
/// </summary>
/// <param name="Title">
/// The <c>.vbp</c> <c>Title=</c> value, falling back to the project file name without its extension when the key is absent — measured, see
/// the App section of <c>docs/vb6-fidelity-oracle/the-app-object--and-the-difference-between-f5-and-a-compiled-exe.md</c>. Quotes already stripped.
/// </param>
/// <param name="ExeName">
/// The <c>.vbp</c> file's own name without its extension. Not the project <c>Name</c>, and not
/// <c>ExeName32</c>: at design time VB6 reports the project FILE, which is the one row here that a
/// reasonable guess gets wrong.
/// </param>
/// <param name="Path">The folder containing the project file, with no trailing separator (VB6 keeps none).</param>
/// <param name="ProjectName">The project Name, independently of the application Title (#1483).</param>
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
    string Comments = "",
    bool CommonDialogReferenced = false,
    IReadOnlyList<Components.InBox.RuntimeLibrary>? ControlLibraries = null,
    string CommandLine = "",
    string? ProjectName = null,
    bool HasProjectHelpFile = false)
{
    /// <summary>A program with no project behind it — the test harness and the bare interpreter.</summary>
    public static readonly AppInfo None = new();

    /// <summary>
    /// Derive what a running program reports about itself from the project it belongs to, using VB6's
    /// <b>design-time</b> rules (see the App section of <c>docs/vb6-fidelity-oracle/the-app-object--and-the-difference-between-f5-and-a-compiled-exe.md</c>).
    /// </summary>
    public static AppInfo FromProject(ProjectElements.ProjectDefinition project)
    {
        var path = project.AbsolutePath;

        return new AppInfo(
            // With no Title=, F5 uses the project file name (#2413). Keep the unsaved fallback.
            Title: PreSectionValue(project, "Title")
                ?? (path is null ? project.Name : System.IO.Path.GetFileNameWithoutExtension(path)),

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
            Comments: PreSectionValue(project, "VersionComments") ?? "",
            // The .vbp's Object= line, or a form's: VB6 writes both for a form that holds the control (#1084).
            CommonDialogReferenced: project.UnknownPreSectionLines.Any(l => CommonDialogConstants.IsReference(l.RawLine))
                || project.Forms.Any(f => f.HeaderLines.Any(CommonDialogConstants.IsReference)),
            // The in-box control libraries the project references, whose constants then resolve (#1082).
            ControlLibraries: Components.InBox.RuntimeLibraries.ReferencedBy(project),
            // Project Properties' "Command Line Arguments", which is what Command gives under F5, and HexIDE runs as F5 runs.
            CommandLine: PreSectionValue(project, "Command32") ?? "",
            ProjectName: project.Name,
            HasProjectHelpFile: !string.IsNullOrEmpty(PreSectionValue(project, "HelpFile")));
    }

    /// <summary>
    /// Read a <c>key=value</c> line the project loader kept verbatim.
    /// </summary>
    /// <remarks>
    /// These keys are deliberately read from the preserved lines rather than promoted into the project
    /// model. Modelling a key means the writer owns emitting it, and the <c>.vbp</c> currently round-trips
    /// byte-for-byte against VB6's own files — not worth risking that gate to read a caption.
    /// </remarks>
    internal static string? PreSectionValue(ProjectElements.ProjectDefinition project, string key)
    {
        foreach (var (_, raw) in project.UnknownPreSectionLines)
        {
            var line = raw.TrimStart();
            if (!line.StartsWith(key + "=", System.StringComparison.OrdinalIgnoreCase)) continue;

            var value = line[(key.Length + 1)..].Trim();

            // Quoted titles read back without either quote under F5 and compiled p-code (#2413).
            if (value.Length >= 2 && value[0] == '"' && value[^1] == '"')
                value = value[1..^1];
            else if (value.Length >= 1 && value[0] == '"')
                value = value[1..];

            return value;
        }
        return null;
    }

    private static int? PreSectionInt(ProjectElements.ProjectDefinition project, string key)
        => int.TryParse(PreSectionValue(project, key), System.Globalization.NumberStyles.Integer,
            System.Globalization.CultureInfo.InvariantCulture, out var n) ? n : null;
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
/// <para>Title, HelpFile and TaskVisible are writable at run time. StartMode refuses assignment, and LogPath
/// rejects writes as measured in #2552; writes to the remaining properties retain the existing member-write
/// behaviour.</para>
/// </summary>
public class VbApp(AppInfo info) : ICSharpProxy, ICSharpPropertyBag
{
    private readonly VbFiles loggingFiles = new() { ProjectFolder = () => info.Path };
    private long logMode = 1;
    private string logPath = "";
    private bool loggingStarted;

    internal VbApp(AppInfo info, VbFiles files) : this(info) => loggingFiles = files;

    // Measured native and p-code, #2552. NT logging is deliberately a no-op: registering an event source
    // would change the developer's machine and require administrator rights (docs/interpreter-gaps.md).
    public void Call(string method, List<Vb6Value> args)
    {
        switch (method.ToLowerInvariant())
        {
            case "startlogging":
                if (args.Count != 2 || args[0].Type != Vb6Value.ValueType.String)
                    throw new NotImplementedException("App.StartLogging argument conversion or omitted mode is unmeasured (#2552)");
                var mode = LoggingNumber(args[1]);
                if (mode is not (1 or 2 or 3 or 18))
                    throw new NotImplementedException("App.StartLogging mode is unmeasured (#2552)");
                var path = (string)args[0].Value!;
                if (mode == 3 && path.Length != 0)
                    throw new NotImplementedException("App.StartLogging NT with a nonempty path is unmeasured (#2552)");
                logMode = mode == 18 ? 1 : mode;
                if (mode == 2) logPath = path.Length == 0 ? "vbevents.log" : path;
                else if (mode == 3) logPath = "";
                loggingStarted = true;
                return;
            case "logevent":
                LogEvent(args);
                return;
            default:
                throw new VBRunTimeException(VBStandardError.MethodOrDataMemberNotFound, "App." + method);
        }
    }

    private static long LoggingNumber(Vb6Value value)
    {
        if (value.Type != Vb6Value.ValueType.Integer && value.Type != Vb6Value.ValueType.Long)
            throw new NotImplementedException("App logging argument conversion is unmeasured (#2552)");
        return Convert.ToInt64(value.Value);
    }

    private void LogEvent(List<Vb6Value> args)
    {
        if (args.Count is < 1 or > 2)
            throw new NotImplementedException("App.LogEvent argument count is unmeasured (#2552)");
        var type = args.Count == 1 ? 4 : LoggingNumber(args[1]);
        var word = type switch
        {
            4 => "Information ",
            2 => "Warning     ",
            1 => "Error       ",
            9 => throw new VBRunTimeException(VBStandardError.InvalidPropertyValue),
            _ => throw new NotImplementedException("App.LogEvent type is unmeasured (#2552)"),
        };
        var message = args[0].Type == Vb6Value.ValueType.String
            ? (string)args[0].Value!
            : LoggingNumber(args[0]).ToString(System.Globalization.CultureInfo.InvariantCulture);
        if (!loggingStarted)
        {
            logMode = 3;
            loggingStarted = true;
        }
        if (logMode != 2) return;
        var line = $"{word}Application {logPath}: Thread ID: {Environment.CurrentManagedThreadId} ,Logged: {message}\r\n";
        foreach (var character in line)
            if (character > 127)
                throw new NotImplementedException("App.LogEvent non-ASCII file encoding is unmeasured (#2552)");
        try
        {
            File.AppendAllText(loggingFiles.FullPath(logPath), line, Encoding.ASCII);
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or ArgumentException)
        {
            throw new NotImplementedException("App.LogEvent file failure semantics are unmeasured (#2552)", error);
        }
    }

    private string title = info.Title;
    private string? helpFile = info.HasProjectHelpFile ? null : "";
    private bool taskVisible = true;

    /// <summary>The project's folder, empty for an unsaved project: where a relative file path is resolved (#1145).</summary>
    public string Path => info.Path;

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
            // Measured in #2551; a project's nonempty HelpFile= has not been measured.
            case "helpfile": value = helpFile ?? throw new NotImplementedException("App.HelpFile from a project is not implemented (#2551)"); return true;
            case "startmode": value = 0; return true;
            case "taskvisible": value = taskVisible; return true;
            case "exename": value = info.ExeName; return true;
            case "path": value = info.Path; return true;
            case "logmode": value = new Vb6Value(logMode); return true;
            case "logpath": value = logPath; return true;

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
        switch (name.ToLowerInvariant())
        {
            case "title": title = VbDisplay.ToText(value); return true;
            case "helpfile":
                if (value.Type != Vb6Value.ValueType.String)
                    throw new NotImplementedException("App.HelpFile conversion is not implemented (#2551)");
                helpFile = (string)value.Value!;
                return true;
            case "taskvisible":
                if (value.Type != Vb6Value.ValueType.Boolean)
                    throw new NotImplementedException("App.TaskVisible conversion is not implemented (#2551)");
                taskVisible = (bool)value.Value!;
                return true;
            case "startmode": throw new VBCompileErrorException("Can't assign to read-only property");
            case "logpath": throw new VBCompileErrorException("Can't assign to read-only property");
            default: return false;
        }
    }
}
