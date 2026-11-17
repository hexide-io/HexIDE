using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.Office;

namespace HexIDE.Tests.Office;

public class CompoundFileTests
{
    internal static string Corpus
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory != null && !Directory.Exists(Path.Combine(directory.FullName, "corpus", "office")))
                directory = directory.Parent;
            return Path.Combine(directory!.FullName, "corpus", "office", "compound-files");
        }
    }

    internal static byte[] Fixture(string name)
    {
        using var file = File.OpenRead(Path.Combine(Corpus, name));
        using var output = new MemoryStream();
        if (name.EndsWith(".gz", StringComparison.Ordinal))
        {
            using var gzip = new GZipStream(file, CompressionMode.Decompress);
            gzip.CopyTo(output);
        }
        else
            file.CopyTo(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData("v3-mixed.cfb")]
    [InlineData("v3-partial-last-sector.cfb")]
    [InlineData("v4-mixed.cfb")]
    [InlineData("v3-difat.cfb.gz")]
    [InlineData("xls-shaped.cfb")]
    [InlineData("doc-shaped.cfb")]
    [InlineData("no-vba.cfb")]
    [InlineData("xlsm-shaped.zip")]
    [InlineData("xlsx-no-vba.zip")]
    public void EveryFixtureMatchesIndependentManifest(string name)
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Path.Combine(Corpus, "manifest.json")));
        var expected = manifest.RootElement.GetProperty(name);
        using var source = new MemoryStream(Fixture(name));
        if (name.EndsWith(".zip", StringComparison.Ordinal))
        {
            using var zip = new ZipArchive(source);
            var parts = zip.Entries.Where(e => e.FullName.EndsWith("vbaProject.bin", StringComparison.Ordinal)).ToArray();
            parts.Select(e => e.FullName).Should().BeEquivalentTo(expected.EnumerateObject().Select(e => e.Name));
            foreach (var part in parts)
            {
                using var bytes = new MemoryStream();
                using var input = part.Open();
                input.CopyTo(bytes);
                CheckBytes(bytes.ToArray(), expected.GetProperty(part.FullName));
                using var projectSource = new MemoryStream(bytes.ToArray());
                using var project = CompoundFile.Open(projectSource);
                var excel = manifest.RootElement.GetProperty("xls-shaped.cfb");
                var actual = Flatten(project.Root).ToDictionary(e => e.Path, e => e.Entry);
                var prefix = "_VBA_PROJECT_CUR/";
                var projectEntries = excel.EnumerateObject().Where(e => e.Name.StartsWith(prefix, StringComparison.Ordinal) && e.Name != prefix).ToArray();
                actual.Keys.Should().BeEquivalentTo(projectEntries.Select(e => e.Name[prefix.Length..]));
                foreach (var entry in projectEntries.Where(e => e.Value.ValueKind == JsonValueKind.Object))
                    CheckBytes(actual[entry.Name[prefix.Length..]].ReadBytes(), entry.Value);
            }
            return;
        }
        using var cfb = CompoundFile.Open(source);
        var entries = Flatten(cfb.Root).ToDictionary(e => e.Path, e => e.Entry);
        entries.Keys.Should().BeEquivalentTo(expected.EnumerateObject().Select(e => e.Name));
        foreach (var entry in expected.EnumerateObject())
        {
            if (entry.Value.ValueKind == JsonValueKind.String)
                entries[entry.Name].IsStorage.Should().BeTrue();
            else
            {
                entries[entry.Name].IsStorage.Should().BeFalse();
                entries[entry.Name].Length.Should().Be(entry.Value.GetProperty("length").GetUInt64());
                CheckBytes(entries[entry.Name].ReadBytes(), entry.Value);
            }
        }
    }

    private static void CheckBytes(byte[] bytes, JsonElement expected)
    {
        bytes.LongLength.Should().Be(expected.GetProperty("length").GetInt64());
        Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be(expected.GetProperty("sha256").GetString());
    }

    private static IEnumerable<(string Path, CompoundEntry Entry)> Flatten(CompoundEntry root, string prefix = "")
    {
        foreach (var entry in root.Children)
        {
            var path = prefix + entry.Name + (entry.IsStorage ? "/" : "");
            yield return (path, entry);
            foreach (var child in Flatten(entry, path))
                yield return child;
        }
    }
}
