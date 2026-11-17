using System.IO;
using System.Text;
using System.Text.Json;
using AwesomeAssertions.Execution;

namespace HexIDE.Tests;

public class ShippedJsonUniquenessTests
{
    [Fact]
    public void ShippedJson_HasNoDuplicateProperties()
    {
        var ide = IdeDirectory();
        var files = JsonFiles(ide).OrderBy(path => path, StringComparer.Ordinal).ToArray();
        files.Should().NotBeEmpty();
        var duplicates = files.SelectMany(file => JsonDuplicateProperties.Find(File.ReadAllBytes(file))
            .Select(duplicate => $"{Path.GetRelativePath(ide, file)}: {duplicate}"));
        var diagnostics = string.Join(Environment.NewLine, duplicates);
        using var scope = new AssertionScope();
        scope.FormattingOptions.MaxLines = int.MaxValue;
        scope.FormattingOptions.StringPrintLength = diagnostics.Length + 1;
        diagnostics.Should().BeEmpty("shipped JSON must not silently replace an earlier property");
    }

    [Theory]
    [InlineData("\r\n", true)]
    [InlineData("\n", false)]
    public void Helper_ReportsEveryRepeatWithPathAndLines(string newline, bool bom)
    {
        var json = string.Join(newline, "{", "  \"strings\": {", "    \"é\": 1,",
            "    \"\\u00e9\": 2,", "    \"é\": 3", "  },", "  \"items\": [{",
            "    \"key\": 1,", "    \"key\": 2", "  }]", "}");
        var bytes = Encoding.UTF8.GetBytes((bom ? "\uFEFF" : "") + json);
        JsonDuplicateProperties.Find(bytes).Should().Equal(
            new JsonDuplicateProperty("é", 3, 4, "$[\"strings\"]"),
            new JsonDuplicateProperty("é", 3, 5, "$[\"strings\"]"),
            new JsonDuplicateProperty("key", 8, 9, "$[\"items\"][0]"));
    }

    [Fact]
    public void Helper_AllowsSameKeyInDifferentObjects()
    {
        JsonDuplicateProperties.Find(Encoding.UTF8.GetBytes(
            "{\"key\":0,\"child\":{\"key\":1},\"items\":[{\"key\":2},{\"key\":3}]}"))
            .Should().BeEmpty();
    }

    private static IEnumerable<string> JsonFiles(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.json"))
            yield return file;
        foreach (var child in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(child);
            // Test projects are excluded: the tracked JSON fixtures live in
            // HexIDE.Tests/Localization/Packs and HexIDE.TwinBasic.Tests/Fixtures.
            // bin/obj and node_modules are generated output/dependencies.
            // Corpus contains test data, not shipped configuration.
            if (name.EndsWith(".Tests", StringComparison.Ordinal) ||
                name is "bin" or "obj" or "node_modules" or "Corpus")
                continue;
            foreach (var file in JsonFiles(child))
                yield return file;
        }
    }

    private static string IdeDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var ide = Path.Combine(dir.FullName, "IDE");
            if (Directory.Exists(Path.Combine(ide, "HexIDE", "Localization", "Packs"))) return ide;
        }
        throw new DirectoryNotFoundException("Could not locate IDE/HexIDE/Localization/Packs from " +
                                             AppContext.BaseDirectory);
    }
}

internal record JsonDuplicateProperty(string Key, int FirstLine, int RepeatedLine, string ObjectPath)
{
    public override string ToString() =>
        $"{ObjectPath}: key '{Key}' repeated on lines {FirstLine} and {RepeatedLine}";
}

internal static class JsonDuplicateProperties
{
    public static IReadOnlyList<JsonDuplicateProperty> Find(byte[] bytes)
    {
        var offset = bytes.AsSpan().StartsWith(new byte[] { 0xEF, 0xBB, 0xBF }) ? 3 : 0;
        var reader = new Utf8JsonReader(bytes.AsSpan(offset), new JsonReaderOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip
        });
        var duplicates = new List<JsonDuplicateProperty>();
        reader.Read();
        var newlines = Enumerable.Range(0, bytes.Length).Where(i => bytes[i] == (byte)'\n').ToArray();
        ReadValue(ref reader, "$", newlines, offset, duplicates);
        return duplicates;
    }

    private static void ReadValue(ref Utf8JsonReader reader, string path, int[] newlines, int offset,
        List<JsonDuplicateProperty> duplicates)
    {
        if (reader.TokenType == JsonTokenType.StartObject)
        {
            var properties = new Dictionary<string, int>(StringComparer.Ordinal);
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                var key = reader.GetString()!;
                var position = Array.BinarySearch(newlines, checked(offset + (int)reader.TokenStartIndex));
                var line = 1 + (position < 0 ? ~position : position);
                if (!properties.TryAdd(key, line))
                    duplicates.Add(new JsonDuplicateProperty(key, properties[key], line, path));
                reader.Read();
                ReadValue(ref reader, path + "[" + JsonSerializer.Serialize(key) + "]", newlines, offset, duplicates);
            }
        }
        else if (reader.TokenType == JsonTokenType.StartArray)
        {
            var index = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndArray)
                ReadValue(ref reader, $"{path}[{index++}]", newlines, offset, duplicates);
        }
    }
}
