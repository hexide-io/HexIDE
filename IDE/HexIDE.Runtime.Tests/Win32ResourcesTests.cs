using System.Text;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.Tests;

// Fixtures authored byte by byte from Microsoft's RESOURCEHEADER description.
// VB6 acceptance and lookup provenance: hexide-io/HexIDE#2625, #2624 and #2628.
public class Win32ResourcesTests : BaseVBTestFixture
{
    private static byte[] Entry(object type, object name, byte[] data, ushort language = 0x409)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        writer.Write((uint)data.Length);
        writer.Write(0u);
        void Identifier(object value)
        {
            if (value is ushort ordinal) { writer.Write((ushort)0xffff); writer.Write(ordinal); }
            else { writer.Write(Encoding.Unicode.GetBytes((string)value)); writer.Write((ushort)0); }
        }
        void Pad() { while (stream.Position % 4 != 0) writer.Write((byte)0); }
        Identifier(type);
        Identifier(name);
        Pad();
        writer.Write(0u);
        writer.Write((ushort)0x30);
        writer.Write(language);
        writer.Write(0u);
        writer.Write(0u);
        var headerSize = (uint)stream.Position;
        writer.Write(data);
        Pad();
        stream.Position = 4;
        writer.Write(headerSize);
        return stream.ToArray();
    }

    private static byte[] FileBytes(params byte[][] entries) =>
        Entry((ushort)0, (ushort)0, []).Concat(entries.SelectMany(e => e)).ToArray();

    [Fact]
    public void EmptyLeadingEntryIsIgnored()
    {
        var resources = Win32Resources.Read(FileBytes(Entry((ushort)10, (ushort)201, [1])));
        resources.Find((ushort)0, (ushort)0).Should().BeNull();
        resources.Find((ushort)10, (ushort)201).Should().Equal(1);
    }

    [Theory]
    [InlineData((ushort)10)]
    [InlineData((ushort)2)]
    public void OrdinalResourcesReturnExactBytes(ushort type)
    {
        var resources = Win32Resources.Read(FileBytes(Entry(type, (ushort)201, [0, 255, 7])));
        resources.Find(type, (ushort)201).Should().Equal(0, 255, 7);
        resources.Find(type, "201").Should().BeNull();
    }

    [Fact]
    public void NamesAndTypesAreCaseInsensitiveAndBothAlignmentsAreConsumed()
    {
        var resources = Win32Resources.Read(FileBytes(
            Entry("CUSTOM", "NAMED", [1]), Entry("TYPE", (ushort)202, [2, 3]),
            Entry((ushort)10, "AB", [4, 5, 6])));
        resources.Find("custom", "named").Should().Equal(1);
        resources.Find("type", (ushort)202).Should().Equal(2, 3);
        resources.Find((ushort)10, "ab").Should().Equal(4, 5, 6);
    }

    [Fact]
    public void CountedStringsUseBlockAndSlotAndEmptyMeansAbsent()
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.Unicode);
        for (var i = 0; i < 16; i++)
        {
            var text = i == 5 ? "Hello\0世界" : i == 8 ? "Last" : "";
            writer.Write((ushort)text.Length);
            writer.Write(Encoding.Unicode.GetBytes(text));
        }
        var resources = Win32Resources.Read(FileBytes(Entry((ushort)6, (ushort)7, stream.ToArray())));
        resources.FindString(101).Should().Be("Hello\0世界");
        resources.FindString(104).Should().Be("Last");
        resources.FindString(103).Should().BeNull();
        resources.FindString(117).Should().BeNull();
    }

    [Fact]
    public void MultipleLanguagesAreRefusedOnlyForMatchingLookup()
    {
        var resources = Win32Resources.Read(FileBytes(Entry((ushort)10, (ushort)201, [1]),
            Entry((ushort)10, (ushort)201, [2], 0x407), Entry((ushort)10, (ushort)202, [3])));
        var lookup = () => resources.Find((ushort)10, (ushort)201);
        lookup.Should().Throw<NotImplementedException>();
        resources.Find((ushort)10, (ushort)202).Should().Equal(3);
    }

    [Fact]
    public void TruncatedFileDiscardsEarlierResources()
    {
        var bytes = FileBytes(Entry((ushort)10, (ushort)201, [1]), Entry("CUSTOM", "NAMED", [2, 3, 4, 5]));
        Win32Resources.Read(bytes).Find("CUSTOM", "NAMED").Should().Equal(2, 3, 4, 5);
        for (var length = 33; length < bytes.Length - 4; length++)
        {
            // A complete first entry alone is valid; partial subsequent headers and data are not.
            if (length == 68) continue;
            Win32Resources.Read(bytes[..length]).Find("CUSTOM", "NAMED").Should().BeNull();
        }
        Win32Resources.Read(bytes[..^4]).Find((ushort)10, (ushort)201).Should().BeNull();
    }

    [Theory]
    [InlineData(0, uint.MaxValue)]
    [InlineData(4, uint.MaxValue)]
    [InlineData(4, 12u)]
    [InlineData(4, 33u)]
    public void InvalidEntrySizesDiscardTheFile(int offset, uint value)
    {
        var bytes = FileBytes(Entry((ushort)10, (ushort)201, [1]));
        Win32Resources.Read(bytes).Find((ushort)10, (ushort)201).Should().Equal(1);
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(32 + offset), value);
        Win32Resources.Read(bytes).Find((ushort)10, (ushort)201).Should().BeNull();
    }

    [Fact]
    public void TruncatedStringBlockIsAbsent()
    {
        var block = new byte[34];
        block[0] = 1;
        block[2] = 65;
        var resources = Win32Resources.Read(FileBytes(Entry((ushort)6, (ushort)7, block)));
        resources.FindString(96).Should().Be("A");
        Win32Resources.Read(FileBytes(Entry((ushort)6, (ushort)7, [1, 0, 65])))
            .FindString(96).Should().BeNull();
    }

    [Fact]
    public void ProjectPathUsesProjectFolderAndWindowsSeparators()
    {
        var project = new ProjectDefinition(VBProjectType.EXE, "Probe")
        { AbsolutePath = Path.Combine(Path.GetTempPath(), "project", "Probe.vbp") };
        project.UnknownPreSectionLines.Add((0, "ResFile32=\"res\\probe.res\""));
        AppInfo.FromProject(project).ResourceFilePath.Should().Be(
            Path.Combine(Path.GetTempPath(), "project", "res", "probe.res"));
    }

    [Fact]
    public void InterpreterLoadsProjectResourcesWhenRunIsConfigured()
    {
        var dir = Path.Combine(Directory.GetCurrentDirectory(), "artifacts", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Path.Combine(dir, "res"));
        try
        {
            var path = Path.Combine(dir, "res", "probe.res");
            File.WriteAllBytes(path, FileBytes(Entry((ushort)10, (ushort)201, [7, 8, 9])));
            var project = new ProjectDefinition(VBProjectType.EXE, "Probe")
            { AbsolutePath = Path.Combine(dir, "Probe.vbp") };
            project.UnknownPreSectionLines.Add((0, "ResFile32=\"res\\probe.res\""));
            var interpreter = new BasicInterpreter(new MockStdLib(debug), context, rootEnv, "");
            interpreter.SetAppInfo(AppInfo.FromProject(project));
            interpreter.Resources.Find((ushort)10, (ushort)201).Should().Equal(7, 8, 9);
            // The run owns a snapshot; it neither writes nor keeps the file open.
            File.ReadAllBytes(path).Should().Equal(FileBytes(Entry((ushort)10, (ushort)201, [7, 8, 9])));
            File.Delete(path);
            interpreter.Resources.Find((ushort)10, (ushort)201).Should().Equal(7, 8, 9);
            interpreter.SetAppInfo(AppInfo.FromProject(project));
            interpreter.Resources.Find((ushort)10, (ushort)201).Should().BeNull();
            File.WriteAllBytes(path, [1, 2, 3]);
            interpreter.SetAppInfo(AppInfo.FromProject(project));
            interpreter.Resources.Find((ushort)10, (ushort)201).Should().BeNull();
            interpreter.SetAppInfo(new AppInfo(ResourceFilePath: dir));
            interpreter.Resources.Find((ushort)10, (ushort)201).Should().BeNull();
        }
        finally { Directory.Delete(dir, true); }
    }

}
