using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using HexIDE.IDE;

namespace HexIDE.Runtime.Serialization;

/// <summary>The trailing project section that disambiguates BOM-less UTF-8 after its signature is edited away.</summary>
internal static class ProjectEncodingRecords
{
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

    public static string Write(string? tail, IReadOnlyList<string> paths)
    {
        // Keep other tools' sections verbatim, and unknown keys even in our own section.
        var result = new StringBuilder();
        var inSection = false;
        var sectionBody = new StringBuilder();
        foreach (var line in LinesWithEndings(tail ?? ""))
        {
            var text = line.TrimEnd('\r', '\n');
            if (text.StartsWith('['))
                inSection = text.Equals("[HexIDE]", StringComparison.OrdinalIgnoreCase);
            if (!inSection)
                result.Append(line);
            else if (!text.StartsWith('[') && !TryRecord(text, out _) && text.Length > 0)
                sectionBody.Append(line);
        }
        if (paths.Count > 0 || sectionBody.Length > 0)
        {
            if (result.Length > 0 && result[^1] != '\n') result.Append("\r\n");
            result.Append("[HexIDE]\r\n").Append(sectionBody);
            if (sectionBody.Length > 0 && result[^1] != '\n') result.Append("\r\n");
            foreach (var path in paths)
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
