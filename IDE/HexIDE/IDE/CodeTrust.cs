using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HexIDE.IO;
using HexIDE.Localization;

namespace HexIDE.IDE;

/// <summary>Where a file of code came from, when that isn't this computer (#1121).</summary>
public enum CodeOrigin
{
    /// <summary>The operating system marks the file as downloaded; see <see cref="DownloadMark"/>.</summary>
    Downloaded,

    /// <summary>The project was read from an Office document, whose macros Office itself would not run unasked.</summary>
    OfficeDocument,
}

/// <summary>A file whose code came from outside this computer.</summary>
public sealed record UntrustedFile(string Path, CodeOrigin Origin);

/// <summary>
/// Asks before running code that came from outside this computer: a file the operating system marks as downloaded, or a
/// project read from an Office document (#1121). HexIDE's interpreter runs code with the developer's own rights and none
/// of Office's macro security, so a workbook or a project someone sent must not run unasked.
/// </summary>
/// <remarks>
/// <para>
/// Opening a project never runs its code: no <c>Form_Load</c>, no <c>Sub Main</c>, no <c>UserControl_Initialize</c>, and
/// nothing like Office's <c>Workbook_Open</c> or <c>AutoOpen</c>. This is the question every route that starts the code
/// asks first.
/// </para>
/// <para>
/// A file's mark is read when HexIDE reads the file (<see cref="Observe"/>), and again before a run. A mark seen once
/// stands for the session: saving writes a new file, which carries no mark, but its code is still the code that came
/// with one. A later session reads the saved file, which the developer wrote, and doesn't ask.
/// </para>
/// <para>
/// A Yes holds for those files for the rest of the session, in every project that includes them; nothing is remembered
/// past it. A file whose mark appears later is asked about on its own.
/// </para>
/// </remarks>
public sealed class CodeTrust
{
    private readonly IWindowManager windows;
    private readonly ILocalizationService localization;
    private readonly Func<string, bool> isDownloaded;
    private readonly HashSet<string> marked = new(HostFilePath.Comparer);
    private readonly HashSet<string> approved = new(HostFilePath.Comparer);
    // A file's mark is recorded from the loader's read callback, which can run off the UI thread.
    private readonly Lock gate = new();

    public CodeTrust(IWindowManager windows, ILocalizationService localization, Func<string, bool>? isDownloaded = null)
    {
        this.windows = windows;
        this.localization = localization;
        this.isDownloaded = isDownloaded ?? DownloadMark.IsDownloaded;
    }

    /// <summary>The files being asked about while the question is up, for automation to report; null otherwise.</summary>
    public IReadOnlyList<UntrustedFile>? Asking { get; private set; }

    /// <summary>Records the mark of a file HexIDE has just read.</summary>
    public void Observe(string path) => IsMarked(path);

    /// <summary>
    /// Records that a file's code was saved under a new name. A Save As writes a new file, which carries no mark, so the
    /// mark, and any Yes given, follows the code there for the rest of the session.
    /// </summary>
    public void Moved(string from, string to)
    {
        if (HostFilePath.Equals(Full(from), Full(to)))
            return;
        if (IsMarked(from))
            lock (gate)
                marked.Add(Full(to));
        lock (gate)
            if (approved.Contains(Full(from)))
                approved.Add(Full(to));
    }

    /// <summary>The files of <paramref name="project"/>'s code that came from outside this computer and haven't been allowed to run.</summary>
    public IReadOnlyList<UntrustedFile> Unapproved(ProjectDefinition project)
    {
        var found = new List<UntrustedFile>();
        if (project.AbsolutePath is { } own && IsOfficeDocument(own))
            found.Add(new UntrustedFile(Full(own), CodeOrigin.OfficeDocument));
        foreach (var path in CodeFiles(project))
            if (!found.Any(file => HostFilePath.Equals(file.Path, path)) && IsMarked(path))
                found.Add(new UntrustedFile(path, CodeOrigin.Downloaded));
        lock (gate)
            return found.Where(file => !approved.Contains(file.Path)).ToList();
    }

    /// <summary>A single file to be run, such as a debug launch file, if it came from outside this computer and hasn't been allowed.</summary>
    public UntrustedFile? Unapproved(string path)
    {
        var full = Full(path);
        if (!IsMarked(full))
            return null;
        lock (gate)
            return approved.Contains(full) ? null : new UntrustedFile(full, CodeOrigin.Downloaded);
    }

    /// <summary>
    /// Asks whether <paramref name="name"/>'s <paramref name="files"/> may run, and answers true when they may. No is the
    /// default button. False without asking while another question is up, so a second start doesn't stack a second box.
    /// </summary>
    public async Task<bool> AskAsync(string name, IReadOnlyList<UntrustedFile> files)
    {
        if (Asking is not null)
            return false;
        Asking = files;
        try
        {
            var list = string.Join("\n", files.Select(file => "  • " + string.Format(
                localization.GetString(file.Origin == CodeOrigin.OfficeDocument ? "Str.CodeTrust.Office" : "Str.CodeTrust.Downloaded"),
                Shown(Path.GetFileName(file.Path)))));
            // HexIDE's own sentence comes first, and the name only after it, quoted on a line of its own: a project names
            // itself, so a name must never be able to read as the question's opening (#1121 review).
            var answer = await windows.MessageBox(string.Format(localization.GetString("Str.CodeTrust.Prompt"), Shown(name, MostOfAName), list),
                localization.GetString("Str.CodeTrust.Caption"), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
                layout: MessageBoxLayout.SecondButtonDefault);
            if (answer != MessageBoxResult.Yes)
            {
                Serilog.Log.Information("Not run: running {Name}, whose code came from outside this computer, was declined", name);
                return false;
            }
            lock (gate)
                foreach (var file in files)
                    approved.Add(file.Path);
            return true;
        }
        finally
        {
            Asking = null;
        }
    }

    private bool IsMarked(string path)
    {
        var full = Full(path);
        lock (gate)
            if (marked.Contains(full))
                return true;
        // Read outside the lock: it touches the disk.
        if (!isDownloaded(full))
            return false;
        lock (gate)
            marked.Add(full);
        return true;
    }

    // The files whose code runs: the project's own and its members'. A file it only carries, such as a text file, is never run.
    private static IEnumerable<string> CodeFiles(ProjectDefinition project)
    {
        if (project.AbsolutePath is { } own)
            yield return Full(own);
        foreach (var form in project.Forms)
            if (form.AbsolutePath is { } path)
                yield return Full(path);
        foreach (var module in project.Modules)
            if (module.AbsolutePath is { } path)
                yield return Full(path);
    }

    private static string Full(string path) => Path.GetFullPath(path);

    // Office documents that can hold a VBA project, or name one: Word, Excel, PowerPoint, Access and Visio.
    private static readonly HashSet<string> OfficeExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".doc", ".docm", ".dot", ".dotm",
        ".xls", ".xlsm", ".xlsb", ".xla", ".xlam", ".xlt", ".xltm",
        ".ppt", ".pptm", ".pot", ".potm", ".ppa", ".ppam", ".pps", ".ppsm",
        ".mdb", ".mde", ".accdb", ".accde",
        ".vsd", ".vsdm", ".vss", ".vssm", ".vst", ".vstm",
    };

    internal static bool IsOfficeDocument(string path) => OfficeExtensions.Contains(Path.GetExtension(path));

    /// <summary>The most of a project's or launch file's name the question shows.</summary>
    internal const int MostOfAName = 60;

    /// <summary>
    /// A name read from a file, fit to show on one line: a newline or other control character reads as a space, so a name
    /// can't start a line of its own; formatting characters, which can reverse or hide part of a name, are dropped; and a
    /// long name is cut, with an ellipsis.
    /// </summary>
    internal static string Shown(string text, int most = 80)
    {
        var shown = new StringBuilder();
        foreach (var c in text)
        {
            if (char.IsControl(c) || char.IsWhiteSpace(c))
            {
                if (shown.Length > 0 && shown[^1] != ' ')
                    shown.Append(' ');
            }
            else if (CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.Format)
                shown.Append(c);
        }
        var line = shown.ToString().TrimEnd();
        return line.Length <= most ? line : line[..(most - 1)] + "…";
    }
}
