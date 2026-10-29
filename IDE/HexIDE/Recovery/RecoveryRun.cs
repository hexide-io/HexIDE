using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace HexIDE.Recovery;

/// <summary>
/// One IDE run's recovery folder: made at start, rewritten with each capture of unsaved work, and deleted after a clean
/// exit (hexide-io/HexIDE#499).
/// </summary>
/// <remarks>
/// <code>
/// Recovery/
///   20261028T134512345Z-4242/          one folder per run, named by its process's start time (UTC) and id
///     run.json                         the run, and the manifest of its latest capture
///     s7/                              that capture's files; older ones go once run.json names a newer one
///       p1/Project1.vbp                a project file, when a save would change it
///       p1/d2/Form1.frm, Form1.frx     a document under the file name a save would give it, with its companion
/// </code>
/// <para>
/// <b>run.json is written last, and is the commit.</b> A capture's files go into a new folder first, through the
/// atomic writer, and only then does run.json name it. An exit part-way through leaves run.json naming the previous
/// capture, whole, beside a folder it doesn't name, which is ignored.
/// </para>
/// <para>
/// <b>Text is kept as UTF-8,</b> never in the document's code page. A save refuses a character its code page can't
/// hold, and a backup must never refuse. The code page is recorded, so a restore writes the file as a save would.
/// </para>
/// <para>
/// <b>Each document has a folder of its own,</b> so two files of one name in different folders of a project stay
/// apart, and a form's companion keeps the name its text cites.
/// </para>
/// </remarks>
public sealed class RecoveryRun
{
    /// <summary>The version of run.json's layout, raised when a reader would misread a newer one.</summary>
    public const int Format = 1;

    private const string RecordName = "run.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly int processId;
    private readonly DateTime processStart;
    private int captures;

    private RecoveryRun(string folder, int processId, DateTime processStart)
    {
        Folder = folder;
        this.processId = processId;
        this.processStart = processStart;
    }

    /// <summary>The run's folder.</summary>
    public string Folder { get; }

    /// <summary>
    /// Makes the folder for a run of process <paramref name="processId"/>, started at <paramref name="processStart"/>,
    /// under <paramref name="root"/>, and records the run with nothing captured yet.
    /// </summary>
    /// <remarks>
    /// The start time is kept to the tick, beside the id, because an id alone is reused: a later process given the
    /// same id must not pass for the run still going.
    /// </remarks>
    public static RecoveryRun Begin(string root, int processId, DateTime processStart)
    {
        var start = processStart.ToUniversalTime();
        var folder = Path.Combine(root,
            start.ToString("yyyyMMdd'T'HHmmssfff'Z'", CultureInfo.InvariantCulture) + "-"
            + processId.ToString(CultureInfo.InvariantCulture));
        Directory.CreateDirectory(folder);
        var run = new RecoveryRun(folder, processId, start);
        run.WriteRecord(null, null, []);
        return run;
    }

    /// <summary>
    /// Replaces the run's backup with <paramref name="projects"/>. With nothing unsaved, the record says so and the
    /// earlier captures go.
    /// </summary>
    public void Write(IReadOnlyList<UnsavedProject> projects, DateTime savedAt)
    {
        string? capture = null;
        var records = new List<ProjectRecord>();
        if (projects.Count > 0)
        {
            capture = "s" + (++captures).ToString(CultureInfo.InvariantCulture);
            for (var p = 0; p < projects.Count; p++)
            {
                var project = projects[p];
                var projectFolder = capture + "/p" + (p + 1).ToString(CultureInfo.InvariantCulture);
                var projectFile = project.ProjectFile is { } file
                    ? WriteText(projectFolder, SafeName(file.FileName, "project.vbp"), file.Text)
                    : null;

                var documents = new List<DocumentRecord>();
                for (var d = 0; d < project.Documents.Count; d++)
                {
                    var document = project.Documents[d];
                    string? text = null, companion = null;
                    if (document.Content is { } content)
                    {
                        var documentFolder = projectFolder + "/d" + (d + 1).ToString(CultureInfo.InvariantCulture);
                        text = WriteText(documentFolder, SafeName(content.FileName, "document"), content.Text);
                        if (content is { CompanionFileName: { } companionName, Companion: { } bytes })
                            companion = WriteBytes(documentFolder, SafeName(companionName, "companion"), bytes);
                    }
                    documents.Add(new(document.Name, document.Kind, document.Path,
                        document.Encoding.CodePage, document.Encoding.Utf8Bom, text, companion, document.DiskHash));
                }

                records.Add(new(project.Name, project.Path, project.ProjectFileHash,
                    project.Encoding.CodePage, project.Encoding.Utf8Bom, projectFile, documents, project.RenderedAt,
                    project.Scratch));
            }
        }

        WriteRecord(capture, savedAt.ToUniversalTime(), records);

        // Only once run.json names the new capture: until then the old one is the backup.
        foreach (var old in Directory.EnumerateDirectories(Folder, "s*"))
        {
            if (string.Equals(Path.GetFileName(old), capture, StringComparison.Ordinal))
                continue;
            try { Directory.Delete(old, recursive: true); }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // Left for the run's own deletion: run.json no longer names it, so nothing reads it.
            }
        }
    }

    /// <summary>Deletes the run's folder and everything in it.</summary>
    public void Delete()
    {
        if (Directory.Exists(Folder))
            Directory.Delete(Folder, recursive: true);
    }

    /// <summary>Reads the record of the run in <paramref name="folder"/>.</summary>
    internal static RunRecord? Read(string folder) =>
        JsonSerializer.Deserialize<RunRecord>(File.ReadAllText(Path.Combine(folder, RecordName)), JsonOptions);

    /// <summary>
    /// A hash of everything a capture holds, so a capture identical to the last one written is not written again.
    /// </summary>
    internal static string Fingerprint(IReadOnlyList<UnsavedProject> projects)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        void Add(string? value)
        {
            hash.AppendData(BitConverter.GetBytes(value?.Length ?? -1));
            if (value is not null)
                hash.AppendData(Encoding.UTF8.GetBytes(value));
        }
        void AddFile(UnsavedFile? file)
        {
            Add(file?.FileName);
            Add(file?.Text);
            Add(file?.CompanionFileName);
            hash.AppendData(BitConverter.GetBytes(file?.Companion?.Length ?? -1));
            if (file?.Companion is { } bytes)
                hash.AppendData(bytes);
        }

        hash.AppendData(BitConverter.GetBytes(projects.Count));
        foreach (var project in projects)
        {
            Add(project.Name);
            Add(project.Path);
            Add(project.ProjectFileHash);
            Add(project.Encoding.ToString());
            Add(project.RenderedAt);
            Add(project.Scratch.ToString());
            AddFile(project.ProjectFile);
            hash.AppendData(BitConverter.GetBytes(project.Documents.Count));
            foreach (var document in project.Documents)
            {
                Add(document.Name);
                Add(document.Kind);
                Add(document.Path);
                Add(document.Encoding.ToString());
                Add(document.DiskHash);
                AddFile(document.Content);
            }
        }
        return Convert.ToHexString(hash.GetHashAndReset());
    }

    private void WriteRecord(string? capture, DateTime? savedAt, IReadOnlyList<ProjectRecord> projects) =>
        WriteRecord(Folder, new RunRecord(Format, processId, processStart, savedAt, capture, projects));

    /// <summary>Writes <paramref name="record"/> as the run.json of the run in <paramref name="folder"/>.</summary>
    internal static void WriteRecord(string folder, RunRecord record) =>
        AtomicFileWriter.WriteAllText(Path.Combine(folder, RecordName), JsonSerializer.Serialize(record, JsonOptions));

    private string WriteText(string relativeFolder, string fileName, string text)
    {
        var relative = relativeFolder + "/" + fileName;
        AtomicFileWriter.WriteAllText(Inside(relative), text);
        return relative;
    }

    private string WriteBytes(string relativeFolder, string fileName, byte[] content)
    {
        var relative = relativeFolder + "/" + fileName;
        AtomicFileWriter.WriteAllBytes(Inside(relative), content);
        return relative;
    }

    /// <summary>
    /// The full path of <paramref name="relative"/> within the run's folder, refusing one that would land outside it.
    /// </summary>
    /// <remarks>
    /// A file name here comes from a document's name or path, and a name comes from a file HexIDE read. A name that
    /// climbs out of the folder must not write somewhere else on the machine.
    /// </remarks>
    private string Inside(string relative)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(Folder)) + Path.DirectorySeparatorChar;
        var full = Path.GetFullPath(Path.Combine(Folder, relative.Replace('/', Path.DirectorySeparatorChar)));
        return full.StartsWith(root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? full
            : throw new InvalidOperationException($"A backup file named '{relative}' would be written outside its run's folder.");
    }

    /// <summary>
    /// <paramref name="candidate"/>'s last segment where it is a usable file name on this host, and
    /// <paramref name="fallback"/> where it is not.
    /// </summary>
    private static string SafeName(string candidate, string fallback)
    {
        var name = Path.GetFileName(candidate);
        return name is "" or "." or ".." || name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0 ? fallback : name;
    }

    /// <summary>run.json: the run, and the manifest of its latest capture.</summary>
    /// <param name="Format">The layout's version, <see cref="RecoveryRun.Format"/>.</param>
    /// <param name="ProcessId">The run's process id.</param>
    /// <param name="ProcessStart">When that process started, in UTC: with the id, what tells the run from a later process given the same id.</param>
    /// <param name="SavedAt">When the latest capture was written, or null when nothing has been.</param>
    /// <param name="Capture">The folder holding the latest capture's files, or null when there was nothing unsaved.</param>
    /// <param name="Projects">The projects with unsaved work in that capture.</param>
    internal sealed record RunRecord(
        int Format, int ProcessId, DateTime ProcessStart, DateTime? SavedAt, string? Capture,
        IReadOnlyList<ProjectRecord> Projects);

    /// <summary>A project in <see cref="RunRecord"/>. File paths are relative to the run's folder, with <c>/</c> between segments.</summary>
    internal sealed record ProjectRecord(
        string Name, string? Path, string? ProjectFileHash, int CodePage, bool Utf8Bom, string? ProjectFile,
        IReadOnlyList<DocumentRecord> Documents, string RenderedAt = "", bool Scratch = false);

    /// <summary>A form or module in <see cref="ProjectRecord"/>; <see cref="File"/> is null when it had nothing unsaved.</summary>
    internal sealed record DocumentRecord(
        string Name, string Kind, string? Path, int CodePage, bool Utf8Bom, string? File, string? Companion,
        string? DiskHash = null);
}
