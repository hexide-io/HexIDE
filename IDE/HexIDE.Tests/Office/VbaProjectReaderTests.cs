using System.Buffers.Binary;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using HexIDE.Office;

namespace HexIDE.Tests.Office;

// Expectations belong to the corpus makers and oletools-expected.json (#4330), not this reader.
public class VbaProjectReaderTests
{
    private static string Corpus => Path.GetFullPath(Path.Combine(CompoundFileTests.Corpus, "..", "m365-x64"));
    public static IEnumerable<object[]> Files => Directory.GetFiles(Corpus).Where(p =>
        new[] { ".xlsm", ".xlsb", ".xls", ".docm", ".doc", ".pptm" }.Contains(Path.GetExtension(p)))
        .Select(p => new object[] { Path.GetFileName(p) });

    [Theory]
    [MemberData(nameof(Files))]
    public void MatchesIndependentReaderAndCorpusMakers(string file)
    {
        using var located = VbaProjectLocator.Locate(Path.Combine(Corpus, file));
        located.Status.Should().Be(VbaProjectStatus.Found);
        var storage = located.Project!;
        var project = VbaProjectReader.Read(storage);
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Corpus, "oletools-expected.json")));
        var expected = json.RootElement.GetProperty(file);
        project.Name.Should().Be(expected.GetProperty("projectName").GetString());
        project.SystemKind.Should().Be(expected.GetProperty("sysKind").GetUInt32());
        project.DeclaredCodePage.Should().Be(expected.GetProperty("declaredCodePage").GetInt32());
        project.CodePageUsed.Should().Be(file.StartsWith("cp932", StringComparison.Ordinal) ? 932 : 1252);
        project.IsLocked.Should().Be(file == "locked.xlsm");
        project.Modules.Should().HaveCount(expected.GetProperty("modules").GetArrayLength());
        foreach (var item in expected.GetProperty("modules").EnumerateArray())
        {
            var module = project.Modules.Single(m => m.Name == item.GetProperty("nameUnicode").GetString());
            module.StreamName.Should().Be(item.GetProperty("streamNameUnicode").GetString());
            module.ModuleType.Should().Be(item.GetProperty("moduleType").GetUInt16());
            module.SourceOffset.Should().Be(item.GetProperty("textOffset").GetUInt32());
            // The public model withholds locked source. Test the decompressor separately against its oracle bytes.
            var bytes = storage.FindChild("VBA")!.FindChild(module.StreamName)!.ReadBytes();
            var source = VbaCompression.Decompress(bytes.AsSpan((int)module.SourceOffset));
            source.Length.Should().Be(item.GetProperty("sourceBytes").GetInt32());
            Convert.ToHexString(SHA256.HashData(source)).ToLowerInvariant().Should().Be(item.GetProperty("sourceSha256").GetString());
            if (project.IsLocked)
                module.Source.Should().BeNull();
            else
                module.Source.Should().Be(CodePagesEncodingProvider.Instance.GetEncoding(project.CodePageUsed)!.GetString(source));
            var kind = module.Name switch
            {
                "Module1" or "BigModule" or "OtherModule" => VbaModuleKind.Standard,
                "Class1" => VbaModuleKind.Class,
                "UserForm1" => VbaModuleKind.UserForm,
                "ThisWorkbook" or "Sheet1" or "ThisDocument" => VbaModuleKind.Document,
                _ => VbaModuleKind.Standard
            };
            module.Kind.Should().Be(kind);
            if (!project.IsLocked && module.Name == "Module1")
                module.Source.Should().Contain("Public Const Greeting As String = \"Hello from the corpus\"");
            if (module.Name == "BigModule")
                for (var i = 1; i <= 120; ++i)
                    module.Source.Should().Contain($"Public Const Line{i:D3} As String =");
            if (module.Name == "OtherModule")
                module.Source.Should().Contain("Twice = n * 2");
            if (module.Name == "ThisDocument")
                module.Source.Should().Contain("Document_Open");
            if (module.Name == "ThisWorkbook" && (file.StartsWith("basic.xls", StringComparison.Ordinal) || file == "userform.xlsm"))
                module.Source.Should().Contain("Workbook_Open");
            if (module.Name == "Class1")
                module.Source.Should().Contain("Property Get Value").And.Contain("Property Let Value");
            if (module.Name.StartsWith("\u65e5", StringComparison.Ordinal))
            {
                module.Name.Should().Be("\u65e5\u672c\u8a9e\u30e2\u30b8\u30e5\u30fc\u30eb");
                module.Source.Should().Contain("Greeting As String = \"\u3053\u3093\u306b\u3061\u306f\"");
            }
        }
        if (file.StartsWith("basic.xls", StringComparison.Ordinal))
        {
            project.References.Should().Contain(r => r.Kind == VbaReferenceKind.Registered && r.Libid.Contains("scrrun.dll", StringComparison.OrdinalIgnoreCase));
            project.References.Should().Contain(r => r.Kind == VbaReferenceKind.Project && r.Libid.Contains(@"C:\corpus\other.xlsm", StringComparison.OrdinalIgnoreCase));
        }
        if (file == "userform.xlsm")
        {
            project.References.Should().Contain(r => r.Kind == VbaReferenceKind.Control && r.Name == "MSForms" && r.OriginalLibid != null && r.ExtendedLibid != null && r.ExtendedLibid.Contains(@"\VBE\MSForms.exd", StringComparison.OrdinalIgnoreCase));
            var form = project.Modules.Single(m => m.Kind == VbaModuleKind.UserForm);
            form.DesignerStorage.Should().NotBeNull();
            form.DesignerStorage!.IsStorage.Should().BeTrue();
            form.DesignerStorage.Children.Should().NotBeEmpty();
            form.Source.Should().Contain("CommandButton1_Click");
        }
    }

    [Fact]
    public void ThreeContainersGiveTheSameProject()
    {
        using var xlsm = VbaProjectLocator.Locate(Path.Combine(Corpus, "basic.xlsm"));
        using var xlsb = VbaProjectLocator.Locate(Path.Combine(Corpus, "basic.xlsb"));
        using var xls = VbaProjectLocator.Locate(Path.Combine(Corpus, "basic.xls"));
        var expected = VbaProjectReader.Read(xlsm.Project!).Modules.Select(m => (m.Name, m.Kind, m.Source)).ToArray();
        VbaProjectReader.Read(xlsb.Project!).Modules.Select(m => (m.Name, m.Kind, m.Source)).Should().Equal(expected);
        VbaProjectReader.Read(xls.Project!).Modules.Select(m => (m.Name, m.Kind, m.Source)).Should().Equal(expected);
    }

    [Theory]
    [InlineData("signature")]
    [InlineData("chunk-end")]
    [InlineData("chunk-signature")]
    [InlineData("copy-before-start")]
    [InlineData("copy-truncated")]
    [InlineData("copy-chunk-overflow")]
    [InlineData("header-truncated")]
    [InlineData("raw-size")]
    [InlineData("output-limit")]
    [InlineData("record-end")]
    [InlineData("module-offset")]
    public void MalformedInputIsRefusedPromptly(string fault)
    {
        Action read = fault switch
        {
            "signature" => () => VbaCompression.Decompress([0]),
            "chunk-end" => () => VbaCompression.Decompress([1, 3, 0xB0, 0]),
            "chunk-signature" => () => VbaCompression.Decompress([1, 0, 0, 0]),
            "copy-before-start" => () => VbaCompression.Decompress([1, 2, 0xB0, 1, 0, 0]),
            "copy-truncated" => () => VbaCompression.Decompress([1, 1, 0xB0, 1, 0]),
            "copy-chunk-overflow" => () => VbaCompression.Decompress([1, 3, 0xB0, 2, 65, 255, 15]),
            "header-truncated" => () => VbaCompression.Decompress([1, 0]),
            "raw-size" => () => VbaCompression.Decompress([1, 0, 0x30, 0]),
            "output-limit" => () => VbaCompression.Decompress([1, 1, 0xB0, 0, 65], 0),
            "record-end" => () => VbaDirectory.Read([1, 0, 4, 0, 0, 0]),
            _ => () => VbaProjectReader.ReadModuleSource([1], 2)
        };
        var timer = Stopwatch.StartNew();
        read.Should().Throw<OfficeFileException>().Which.Reason.Should().NotBeNullOrWhiteSpace();
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void EnforcesTheDefaultSixteenMiBLimitAcrossChunks()
    {
        var chunks = VbaCompression.MaximumOutputBytes / 4096;
        var bytes = new byte[1 + (chunks + 1) * 4098];
        bytes[0] = 1;
        for (var i = 0; i <= chunks; ++i)
            BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(1 + i * 4098), 0x3FFF);
        VbaCompression.Decompress(bytes.AsSpan(0, 1 + chunks * 4098)).Should().HaveCount(VbaCompression.MaximumOutputBytes);
        Action read = () => VbaCompression.Decompress(bytes);
        var timer = Stopwatch.StartNew();
        read.Should().Throw<OfficeFileException>().Which.Reason.Should().Contain("limit");
        timer.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public void RawChunksAndOverlappingCopiesDecode()
    {
        var raw = new byte[4099];
        raw[0] = 1;
        BinaryPrimitives.WriteUInt16LittleEndian(raw.AsSpan(1), 0x3FFF);
        raw.AsSpan(3).Fill(42);
        VbaCompression.Decompress(raw).Should().Equal(Enumerable.Repeat((byte)42, 4096));
        VbaCompression.Decompress([1, 3, 0xB0, 2, 65, 2, 0]).Should().Equal(Enumerable.Repeat((byte)65, 6));
    }
}
