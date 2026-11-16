using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using HexIDE.Office;

namespace HexIDE.Tests.Office;

public class VbaProjectLocatorTests
{
    private static void WithFile(byte[] bytes, string extension, Action<string> check)
    {
        var path = Path.Combine(Path.GetTempPath(), Guid.NewGuid() + extension);
        try
        {
            File.WriteAllBytes(path, bytes);
            check(path);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Theory]
    [InlineData("xls-shaped.cfb")]
    [InlineData("doc-shaped.cfb")]
    [InlineData("xlsm-shaped.zip")]
    public void FindsProjectFromContentDespiteWrongExtension(string fixture)
    {
        WithFile(CompoundFileTests.Fixture(fixture), ".xlsx", path =>
        {
            using var result = VbaProjectLocator.Locate(path);
            result.Status.Should().Be(VbaProjectStatus.Found);
            result.Error.Should().BeNull();
            var bytes = result.Project!.FindChild("vba")!.FindChild("dir")!.ReadBytes();
            bytes.Length.Should().Be(600);
            Convert.ToHexStringLower(SHA256.HashData(bytes)).Should().Be("1e6805229f5d08f12cdcf61a258483b8f86dbea0508a9ef6af9b503339eaec2b");
        });
    }

    [Theory]
    [InlineData("no-vba.cfb")]
    [InlineData("xlsx-no-vba.zip")]
    public void NoProjectIsDistinctFromUnreadable(string fixture)
    {
        WithFile(CompoundFileTests.Fixture(fixture), ".xlsm", path =>
        {
            using var result = VbaProjectLocator.Locate(path);
            result.Status.Should().Be(VbaProjectStatus.NoProject);
            result.Project.Should().BeNull();
            result.Error.Should().BeNull();
        });
    }

    [Fact]
    public void UnknownContentIsUnreadable()
    {
        WithFile([1, 2, 3, 4], ".doc", path =>
        {
            using var result = VbaProjectLocator.Locate(path);
            result.Status.Should().Be(VbaProjectStatus.Unreadable);
            result.Error!.Reason.Should().Contain("neither");
        });
    }

    [Fact(Timeout = 5000)]
    public void OversizedZipPartIsRefusedWithLimit()
    {
        var bytes = CompoundFileTests.Fixture("xlsm-shaped.zip");
        // Patch the existing central-directory uncompressed length, without allocating its claim.
        var name = Encoding.UTF8.GetBytes("xl/vbaProject.bin");
        for (var offset = 0; offset < bytes.Length - 46 - name.Length; ++offset)
            if (BitConverter.ToUInt32(bytes, offset) == 0x02014B50 && bytes.AsSpan(offset + 46, name.Length).SequenceEqual(name))
            {
                OfficeMalformedTests.Write(bytes, offset + 24, VbaProjectLocator.MaximumZipPartBytes + 1);
                break;
            }
        WithFile(bytes, ".xlsm", path =>
        {
            using var result = VbaProjectLocator.Locate(path);
            result.Status.Should().Be(VbaProjectStatus.Unreadable);
            result.Error!.Reason.Should().Contain("64 MiB");
        });
    }

    [Theory]
    [InlineData("relocated", VbaProjectStatus.Found, "")]
    [InlineData("case", VbaProjectStatus.Found, "")]
    [InlineData("duplicate-case", VbaProjectStatus.Unreadable, "duplicate")]
    [InlineData("missing", VbaProjectStatus.Unreadable, "missing")]
    [InlineData("duplicate", VbaProjectStatus.Unreadable, "duplicate")]
    [InlineData("xml", VbaProjectStatus.Unreadable, "Cannot read")]
    [InlineData("dtd", VbaProjectStatus.Unreadable, "DTD")]
    [InlineData("default", VbaProjectStatus.Found, "")]
    [InlineData("default-with-other-bin-overridden", VbaProjectStatus.Found, "")]
    [InlineData("default-with-two-bin-parts", VbaProjectStatus.Unreadable, "more than one")]
    public void ZipOverrideAndMalformedPartsAreReadThroughLocator(string change, VbaProjectStatus status, string reason)
    {
        using var source = new MemoryStream(CompoundFileTests.Fixture("xlsm-shaped.zip"));
        using var original = new ZipArchive(source);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entry in original.Entries)
            {
                if (entry.FullName == "xl/vbaProject.bin" && change == "missing")
                    continue;
                var name = change == "relocated" && entry.FullName == "xl/vbaProject.bin" ? "custom/data.bin" : entry.FullName;
                var copy = zip.CreateEntry(name);
                using var input = entry.Open();
                using var target = copy.Open();
                if (entry.FullName == "[Content_Types].xml")
                {
                    using var reader = new StreamReader(input);
                    var xml = reader.ReadToEnd();
                    if (change == "relocated")
                        xml = xml.Replace("/xl/vbaProject.bin", "/custom/data.bin", StringComparison.Ordinal);
                    if (change == "case")
                        xml = xml.Replace("/xl/vbaProject.bin", "/XL/VBAPROJECT.BIN", StringComparison.Ordinal);
                    if (change == "xml")
                        xml = "<broken>";
                    if (change == "dtd")
                        xml = "<!DOCTYPE Types [<!ENTITY x 'test'>]>" + xml[(xml.IndexOf("?>", StringComparison.Ordinal) + 2)..];
                    // As .xlsm, .docm and .pptm declare it: a Default for "bin", with no Override.
                    if (change.StartsWith("default", StringComparison.Ordinal))
                        xml = xml.Replace("<Override PartName=\"/xl/vbaProject.bin\" ContentType=\"application/vnd.ms-office.vbaProject\"/>", "",
                                StringComparison.Ordinal)
                            .Replace("<Default Extension=\"xml\"",
                                "<Default Extension=\"BIN\" ContentType=\"application/vnd.ms-office.vbaProject\"/><Default Extension=\"xml\"",
                                StringComparison.Ordinal);
                    // As .xlsb does: another .bin part with an Override of its own type.
                    if (change == "default-with-other-bin-overridden")
                        xml = xml.Replace("</Types>",
                            "<Override PartName=\"/xl/workbook.bin\" ContentType=\"application/vnd.ms-excel.sheet.binary.macroEnabled.main\"/></Types>",
                            StringComparison.Ordinal);
                    target.Write(Encoding.UTF8.GetBytes(xml));
                }
                else
                    input.CopyTo(target);
            }
            if (change is "default-with-other-bin-overridden" or "default-with-two-bin-parts")
            {
                using var other = zip.CreateEntry("xl/workbook.bin").Open();
                other.WriteByte(0);
            }
            if (change is "duplicate" or "duplicate-case")
            {
                using var duplicate = zip.CreateEntry(change == "duplicate" ? "xl/vbaProject.bin" : "XL/VBAPROJECT.BIN").Open();
                duplicate.WriteByte(0);
            }
        }
        WithFile(output.ToArray(), ".doc", path =>
        {
            using var result = VbaProjectLocator.Locate(path);
            result.Status.Should().Be(status);
            if (status == VbaProjectStatus.Found)
                result.Project!.FindChild("VBA")!.FindChild("dir")!.ReadBytes().Length.Should().Be(600);
            else
                result.Error!.Reason.Should().Contain(reason);
        });
    }

    // Real files from Microsoft 365 (corpus/office/m365-x64): the same project in .xlsm, .xlsb and .xls, a Word project in
    // .docm and .doc, a PowerPoint one, another project and a UserForm.
    [Theory]
    [InlineData("basic.xlsm", "BigModule")]
    [InlineData("basic.xlsb", "BigModule")]
    [InlineData("basic.xls", "BigModule")]
    [InlineData("other.xlsm", "OtherModule")]
    [InlineData("userform.xlsm", "UserForm1")]
    [InlineData("basic.docm", "ThisDocument")]
    [InlineData("basic.doc", "ThisDocument")]
    [InlineData("basic.pptm", "Class1")]
    [InlineData("locked.xlsm", "Module1")]
    [InlineData("cp932.xlsm", "\u65E5\u672C\u8A9E\u30E2\u30B8\u30E5\u30FC\u30EB")]
    [InlineData("cp932.xls", "\u65E5\u672C\u8A9E\u30E2\u30B8\u30E5\u30FC\u30EB")]
    public void FindsTheProjectInRealOfficeFiles(string file, string module)
    {
        using var result = VbaProjectLocator.Locate(Path.Combine(CompoundFileTests.Corpus, "..", "m365-x64", file));

        result.Status.Should().Be(VbaProjectStatus.Found, result.Error?.Reason);
        result.Project!.FindChild("PROJECT")!.ReadBytes().Should().NotBeEmpty();
        var vba = result.Project.FindChild("VBA")!;
        vba.FindChild("dir")!.ReadBytes().Should().NotBeEmpty();
        vba.FindChild("_VBA_PROJECT")!.ReadBytes().Should().NotBeEmpty();
        vba.FindChild(module)!.ReadBytes().Should().NotBeEmpty();
    }

    [Fact]
    public void LegacyPowerPointHasSpecificRefusal()
    {
        var bytes = CompoundFileTests.Fixture("no-vba.cfb");
        var entry = OfficeMalformedTests.DirectoryEntry(bytes, "WordDocument");
        var name = Encoding.Unicode.GetBytes("PowerPoint Document\0");
        bytes.AsSpan(entry, 64).Clear();
        name.CopyTo(bytes, entry);
        bytes[entry + 64] = (byte)name.Length;
        WithFile(bytes, ".bin", path =>
        {
            using var result = VbaProjectLocator.Locate(path);
            result.Status.Should().Be(VbaProjectStatus.Unreadable);
            result.Error!.Reason.Should().Contain("PowerPoint Document").And.Contain("MS-PPT");
        });
    }
}
