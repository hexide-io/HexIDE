using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.Corpus;

internal sealed record ProjectSmokeResult(string Project, bool Opened, string Save, string[] Differences, Exception? Crash)
{
    public string[] Normalisations { get; init; } = [];
    public bool Failed => Crash is not null || Differences.Length != 0;
}

internal static class ProjectSmokeHarness
{
    internal static StringComparer PathComparer(bool insensitive) => insensitive ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
    private static StringComparer HostPathComparer => PathComparer(OperatingSystem.IsWindows() || OperatingSystem.IsMacOS());

    // #2274: docs/vb6-fidelity-oracle/a-references-path-and-the-line-order-a-save-writes.md.
    // Latin1 maps bytes one for one: only the reference path field may change.
    internal static byte[] ExpectedSave(byte[] original, string directory, Func<string, string?> resolve,
        out string[] normalisations)
    {
        var changes = new List<string>();
        var text = Regex.Replace(Encoding.Latin1.GetString(original),
            @"(?im)^(Reference=\*\\G[^#\r\n]+#[^#\r\n]+#[^#\r\n]+#)([^#\r\n]*)(#[^\r\n]*)",
            match =>
            {
                var path = match.Groups[2].Value;
                var resolved = resolve(path);
                var expected = resolved is null ? path : SerializedProject.ReferencePath(resolved, directory, directory);
                if (expected != path) changes.Add("Reference path: " + path + " -> " + expected);
                return match.Groups[1].Value + expected + match.Groups[3].Value;
            });
        normalisations = changes.ToArray();
        return Encoding.Latin1.GetBytes(text);
    }

    public static string[] Discover(IEnumerable<string> roots) => roots
        .SelectMany(root => Files(root))
        .Where(path => string.Equals(Path.GetExtension(path), ".vbp", StringComparison.OrdinalIgnoreCase))
        .Select(Path.GetFullPath).Distinct(HostPathComparer).Order(StringComparer.Ordinal).ToArray();

    public static async Task<ProjectSmokeResult> Check(string root, string project,
        Func<string, Task<Func<Task<string?>>>> open, string? evidenceDirectory = null, bool? caseInsensitivePaths = null, string? referenceDirectory = null)
    {
        var comparer = caseInsensitivePaths is null ? HostPathComparer : PathComparer(caseInsensitivePaths.Value);
        var opened = false;
        var scratch = Directory.CreateTempSubdirectory("hexide-project-smoke-").FullName;
        try
        {
            root = Path.GetFullPath(root);
            var relative = Path.GetRelativePath(root, Path.GetFullPath(project));
            if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal))
                throw new ArgumentException("Project must be inside its corpus root.", nameof(project));

            foreach (var source in Files(root))
            {
                var destination = Path.Join(scratch, Path.GetRelativePath(root, source));
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Copy(source, destination);
            }
            var before = Snapshot(scratch, comparer);
            var save = await open(Path.Join(scratch, relative));
            opened = true;
            var refusal = await save();
            var after = Snapshot(scratch, comparer);
            var expected = new Dictionary<string, byte[]>(before, comparer);
            string[] normalisations = [];
            if (refusal is null)
                expected[relative] = ExpectedSave(before[relative], referenceDirectory ?? Path.GetDirectoryName(Path.Join(scratch, relative))!,
                    path => path, out normalisations);
            var differences = expected.Keys.Union(after.Keys, comparer).Where(path =>
                !expected.TryGetValue(path, out var original) || !after.TryGetValue(path, out var saved)
                || !original.SequenceEqual(saved)).Order(StringComparer.Ordinal).ToArray();
            if (evidenceDirectory is not null)
            {
                void WriteEvidence(string version, Dictionary<string, byte[]> snapshot, string relativePath)
                {
                    if (!snapshot.TryGetValue(relativePath, out var bytes)) return;
                    var destination = Path.Join(evidenceDirectory, version, relativePath);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                    File.WriteAllBytes(destination, bytes);
                }
                foreach (var difference in differences)
                {
                    WriteEvidence("original", before, difference);
                    WriteEvidence("saved", after, difference);
                }
            }
            return new(project, true, refusal ?? (differences.Length == 0 ? "identical" : "different"), differences, null) { Normalisations = normalisations };
        }
        catch (Exception exception)
        {
            return new(project, opened, "not completed", [], exception);
        }
        finally
        {
            Directory.Delete(scratch, true);
        }
    }

    private static IEnumerable<string> Files(string root)
    {
        foreach (var entry in Directory.EnumerateFileSystemEntries(root))
        {
            var attributes = File.GetAttributes(entry);
            if ((attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException("Corpus entries must not be symbolic links: " + entry);
            if ((attributes & FileAttributes.Directory) != 0)
            {
                foreach (var file in Files(entry)) yield return file;
            }
            else yield return entry;
        }
    }

    private static Dictionary<string, byte[]> Snapshot(string root, StringComparer comparer) => Directory
        .EnumerateFiles(root, "*", SearchOption.AllDirectories)
        .ToDictionary(path => Path.GetRelativePath(root, path), File.ReadAllBytes, comparer);
}
