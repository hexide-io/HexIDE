using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Serialization;

/// <summary>The trailing project section's source encoding records and targets (#4245, #4821).</summary>
internal static class ProjectEncodingRecords
{
    public static VbaBitness ReadBitness(string? tail)
    {
        var inSection = false;
        using var reader = new StringReader(tail ?? "");
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('['))
                inSection = line.Equals("[HexIDE]", StringComparison.OrdinalIgnoreCase);
            else if (inSection && KnownBitness(line) is { } bitness)
                return bitness;
        }
        return VbaBitness.None;
    }

    // A value HexIDE doesn't know, from a later version or a hand edit, reads as no target and is written back as it was.
    private static VbaBitness? KnownBitness(string line)
    {
        var separator = line.IndexOf('=');
        if (separator < 0 || !line[..separator].Trim().Equals("VbaBitness", StringComparison.OrdinalIgnoreCase))
            return null;
        return line[(separator + 1)..].Trim() switch
        {
            "32" => VbaBitness.Bit32,
            "64" => VbaBitness.Bit64,
            _ => null,
        };
    }

    public static ProjectTarget ReadTarget(string? tail)
    {
        var inSection = false;
        using var reader = new StringReader(tail ?? "");
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('['))
                inSection = line.Equals("[HexIDE]", StringComparison.OrdinalIgnoreCase);
            else if (inSection && KnownTarget(line) is { } target)
                return target;
        }
        return ProjectTarget.None;
    }

    // `Targets` is plural so that a project naming several targets later needs no second key, which is how MSBuild's
    // TargetFramework and TargetFrameworks came to differ. Its value is a list separated by `;`, as TargetFrameworks'
    // is. One target is taken for now: a list of more than one, or a name not known here, reads as no target and is
    // written back as it was (#4821).
    private static ProjectTarget? KnownTarget(string line)
    {
        var separator = line.IndexOf('=');
        if (separator < 0 || !line[..separator].Trim().Equals(TargetsKey, StringComparison.OrdinalIgnoreCase))
            return null;
        var targets = line[(separator + 1)..].Split(';', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return targets is [var only] && only.Equals("RDCore", StringComparison.OrdinalIgnoreCase) ? ProjectTarget.RDCore : null;
    }

    private const string TargetsKey = "Targets";
    private const string Win64OnlyKey = "Win64OnlySetOnWindows";

    public static bool ReadWin64OnlySetOnWindows(string? tail)
    {
        var inSection = false;
        using var reader = new StringReader(tail ?? "");
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('['))
                inSection = line.Equals("[HexIDE]", StringComparison.OrdinalIgnoreCase);
            else if (inSection && KnownWin64Only(line) is { } enabled)
                return enabled;
        }
        return false;
    }

    private static bool? KnownWin64Only(string line)
    {
        var separator = line.IndexOf('=');
        if (separator < 0 || !line[..separator].Trim().Equals(Win64OnlyKey, StringComparison.OrdinalIgnoreCase))
            return null;
        return line[(separator + 1)..].Trim() switch { "1" => true, "0" => false, _ => null };
    }

    public static Vb6TextEncoding? Read(string? tail, string memberPath)
    {
        var inSection = false;
        using var reader = new StringReader(tail ?? "");
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('['))
                inSection = line.Equals("[HexIDE]", StringComparison.OrdinalIgnoreCase);
            else if (inSection && TryRecord(line, out var path) &&
                     path.Equals(memberPath, StringComparison.OrdinalIgnoreCase))
                return Vb6TextEncoding.Utf8;
        }
        return null;
    }

    private static bool TryRecord(string line, out string path)
    {
        var separator = line.LastIndexOf('=');
        path = separator < 0 ? "" : line[..separator].Trim();
        return path.Length > 0 && line[(separator + 1)..].Trim().Equals("utf-8", StringComparison.OrdinalIgnoreCase);
    }

    public static string Write(string? tail, IReadOnlyList<string> paths, VbaBitness bitness, ProjectTarget target, bool win64OnlySetOnWindows)
    {
        // Keep other tools' sections verbatim, and unknown keys even in our own section.
        var result = new StringBuilder();
        var inSection = false;
        var sectionBody = new StringBuilder();
        var wroteWin64Only = false;
        var wroteBitness = false;
        var wroteTarget = false;
        var keptRecords = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in LinesWithEndings(tail ?? ""))
        {
            var text = line.TrimEnd('\r', '\n');
            if (text.StartsWith('['))
                inSection = text.Equals("[HexIDE]", StringComparison.OrdinalIgnoreCase);
            if (!inSection)
                result.Append(line);
            else if (KnownWin64Only(text) is { } enabled)
            {
                // Keep the option where it was, including spelling and whitespace when unchanged (#4890).
                sectionBody.Append(enabled == win64OnlySetOnWindows ? line :
                    text[..(text.IndexOf('=') + 1)] + (win64OnlySetOnWindows ? "1" : "0") + line[text.Length..]);
                wroteWin64Only = true;
            }
            else if (KnownBitness(text) is not null)
            {
                if (bitness != VbaBitness.None)
                    sectionBody.Append("VbaBitness=").Append((int)bitness).Append(line[text.Length..]);
                wroteBitness = true;
            }
            else if (KnownTarget(text) is not null)
            {
                if (target == ProjectTarget.RDCore)
                    sectionBody.Append(TargetsKey).Append("=RDCore").Append(line[text.Length..]);
                wroteTarget = true;
            }
            else if (!text.StartsWith('['))
            {
                var record = TryRecord(text, out var path);
                // Unknown option values remain verbatim, even when they also name a member's encoding record.
                if (record && !text[..text.IndexOf('=')].Trim().Equals(TargetsKey, StringComparison.OrdinalIgnoreCase) &&
                    !text[..text.IndexOf('=')].Trim().Equals(Win64OnlyKey, StringComparison.OrdinalIgnoreCase) &&
                    !paths.Contains(path, StringComparer.OrdinalIgnoreCase))
                    continue;
                sectionBody.Append(line);
                if (record) keptRecords.Add(path);
            }
        }
        if (paths.Count > 0 || sectionBody.Length > 0 || bitness != VbaBitness.None || target != ProjectTarget.None || win64OnlySetOnWindows)
        {
            if (result.Length > 0 && result[^1] != '\n') result.Append("\r\n");
            result.Append("[HexIDE]\r\n").Append(sectionBody);
            if (sectionBody.Length > 0 && result[^1] != '\n') result.Append("\r\n");
            if (bitness != VbaBitness.None && !wroteBitness)
                result.Append("VbaBitness=").Append((int)bitness).Append("\r\n");
            if (target == ProjectTarget.RDCore && !wroteTarget)
                result.Append(TargetsKey).Append("=RDCore\r\n");
            if (win64OnlySetOnWindows && !wroteWin64Only)
                result.Append(Win64OnlyKey).Append("=1\r\n");
            foreach (var path in paths)
                if (!keptRecords.Contains(path))
                    result.Append(path).Append("=utf-8\r\n");
        }
        return result.ToString();
    }

    private static IEnumerable<string> LinesWithEndings(string text)
    {
        var start = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\n')) continue;
            if (text[i] == '\r' && i + 1 < text.Length && text[i + 1] == '\n') i++;
            yield return text[start..(i + 1)];
            start = i + 1;
        }
        if (start < text.Length) yield return text[start..];
    }

    public static bool NeedsRecord(Vb6TextEncoding encoding, string text) =>
        encoding == Vb6TextEncoding.Utf8 && !text.Any(c => c > '\u00FF');
}
