using System.Text;
using HexIDE.Office;

namespace HexIDE.Tests.Office;

public class VbaDirectoryTests
{
    [Fact]
    public void ReadsInformationAndOptionalModuleProperties()
    {
        var directory = VbaDirectory.Read(DirectoryBytes());
        directory.Information.Should().Be(new VbaProjectInformation(3, 7, 0x409, 0x409, 1252,
            "Test", "Description", "help.chm", 42, 0, 12, 3, "Flag = 1"));
        var module = directory.Modules.Single();
        module.NameUnicode.Should().Be("Module1");
        module.StreamNameUnicode.Should().Be("Code");
        module.SourceOffset.Should().Be(100);
        module.IsReadOnly.Should().BeTrue();
        module.IsPrivate.Should().BeTrue();
        module.DocString.Should().Be("Module description");
        module.HelpContext.Should().Be(19);
        directory.SelectSourceCodePage().Should().Be(1252);
    }

    [Fact]
    public void EveryTruncatedDirectoryPrefixIsRefused()
    {
        var bytes = DirectoryBytes();
        for (var length = 0; length < bytes.Length; ++length)
        {
            var prefix = bytes[..length];
            Action read = () => VbaDirectory.Read(prefix);
            read.Should().Throw<OfficeFileException>();
        }
    }

    [Fact]
    public void RefusesNamesThatNoCandidateEncodingCanExplain()
    {
        var directory = VbaDirectory.Read(DirectoryBytes());
        var module = directory.Modules.Single() with { NameUnicode = "\u2603" };
        var inconsistent = directory with { Modules = new[] { module } };
        Action choose = () => inconsistent.SelectSourceCodePage();
        choose.Should().Throw<OfficeFileException>();
    }

    [Fact]
    public void StringsWithNoUnicodeFormUseTheCodePageTheModuleNamesShow()
    {
        // As corpus/office/m365-x64's cp932 files are: saved at code page 932, declaring 1252.
        var cp932 = CodePagesEncodingProvider.Instance.GetEncoding(932)!;
        const string module = "\u65E5\u672C\u8A9E\u30E2\u30B8\u30E5\u30FC\u30EB";
        const string project = "\u4F8B\u984C";
        var bytes = DirectoryBytes(cp932.GetBytes(project), cp932.GetBytes(module), module);

        var (directory, codePage) = VbaDirectory.ReadWithMeasuredCodePage(bytes);

        codePage.Should().Be(932);
        directory.Information.DeclaredCodePage.Should().Be(1252);
        directory.Information.Name.Should().Be(project);
        directory.Modules.Single().NameUnicode.Should().Be(module);
    }

    [Theory]
    [InlineData("GC=\"00\"")]
    [InlineData("GC=\"GG\"")]
    [InlineData("GC=\"1517CAF1D6F9D7F9D70600\"")]
    [InlineData("GC=no-quotes")]
    public void RefusesMalformedVisibility(string text)
    {
        Action read = () => VbaProjectProperties.Read(text);
        read.Should().Throw<OfficeFileException>();
    }

    [Fact]
    public void ReadsTheSpecificationsVisibleProjectExample()
    {
        // MS-OVBA 3.1.1 PROJECT example, not a VB6 runtime measurement.
        var properties = VbaProjectProperties.Read("GC=\"1517CAF1D6F9D7F9D706\"");
        properties.IsLocked.Should().BeFalse();
    }

    private static byte[] DirectoryBytes(byte[]? projectName = null, byte[]? moduleName = null, string moduleUnicode = "Module1")
    {
        using var bytes = new MemoryStream();
        using var writer = new BinaryWriter(bytes, Encoding.UTF8, leaveOpen: true);
        void Record(ushort id, byte[] value)
        {
            writer.Write(id);
            writer.Write((uint)value.Length);
            writer.Write(value);
        }
        void Number(ushort id, uint value) => Record(id, BitConverter.GetBytes(value));
        void Short(ushort id, ushort value) => Record(id, BitConverter.GetBytes(value));
        void Pair(ushort id, ushort unicodeId, string value)
        {
            Record(id, Encoding.ASCII.GetBytes(value));
            Record(unicodeId, Encoding.Unicode.GetBytes(value));
        }
        Number(1, 3);
        Number(0x4A, 7);
        Number(2, 0x409);
        Number(0x14, 0x409);
        Short(3, 1252);
        Record(4, projectName ?? "Test"u8.ToArray());
        Pair(5, 0x40, "Description");
        Record(6, "help.chm"u8.ToArray());
        Record(0x3D, "help.chm"u8.ToArray());
        Number(7, 42);
        Number(8, 0);
        Number(9, 12);
        writer.Write((ushort)3);
        Pair(0xC, 0x3C, "Flag = 1");
        Short(0xF, 1);
        Short(0x13, 0xFFFF);
        Record(0x19, moduleName ?? Encoding.ASCII.GetBytes(moduleUnicode));
        Record(0x47, Encoding.Unicode.GetBytes(moduleUnicode));
        Pair(0x1A, 0x32, "Code");
        Pair(0x1C, 0x48, "Module description");
        Number(0x31, 100);
        Number(0x1E, 19);
        Short(0x2C, 0xFFFF);
        Record(0x21, []);
        Record(0x25, []);
        Record(0x28, []);
        Record(0x2B, []);
        Record(0x10, []);
        return bytes.ToArray();
    }
}
