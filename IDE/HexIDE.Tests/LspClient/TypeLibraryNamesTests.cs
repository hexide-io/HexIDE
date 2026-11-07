using HexIDE.IDE;
using HexIDE.Lsp;
using HexIDE.Runtime.TypeLibrary;

namespace HexIDE.Tests.LspClient;

public sealed class TypeLibraryNamesTests
{
    private static readonly Guid Stdole = Guid.Parse("00020430-0000-0000-C000-000000000046");

    [Fact]
    public void ALibraryIsNamedFromItsTypeLibraryOnceASession()
    {
        var reads = new List<(string Guid, string Version, string? Path)>();
        var sut = new TypeLibraryNames
        {
            Read = (guid, version, path) =>
            {
                reads.Add((guid, version, path));
                return guid == Stdole.ToString("B") ? "stdole" : null;
            }
        };
        sut.NameOf(new("OLE Automation", Stdole, 2, 0)).Should().Be("stdole");
        sut.NameOf(new("OLE Automation", Stdole, 2, 0)).Should().Be("stdole");
        sut.NameOf(new("Unregistered", Guid.NewGuid(), 1, 0)).Should().BeNull();
        sut.NameOf(new("Recorded only")).Should().BeNull();
        reads.Should().HaveCount(2,
            "each library is read once a session, and a reference with neither GUID nor path not at all");
        reads[0].Should().Be((Stdole.ToString("B"), "2.0", (string?)null));
    }

    public static TheoryData<string> Remote => new()
    {
        @"\\host.invalid\share\remote.tlb",
        "//host.invalid/share/remote.tlb",
        @"\\?\UNC\host.invalid\share\remote.tlb",
        @"\\?\C:\remote.tlb",
        @"\\.\pipe\remote",
        "remote.tlb",
        "C:remote.tlb",
    };

    [Theory]
    [MemberData(nameof(Remote))]
    public void APathThatIsNotALocalFileIsNeverPassedOn(string path)
    {
        // Loading from a UNC path contacts its host and hands it the user's credentials, and even asking whether such a
        // file exists does. So the reader is asked by GUID alone (#2071's security review).
        var reads = new List<string?>();
        var sut = new TypeLibraryNames { Read = (_, _, p) => { reads.Add(p); return null; } };
        sut.NameOf(new("Remote", Guid.NewGuid(), 1, 0, 0, path));
        reads.Should().Equal([null]);
        TypeLibReader.IsLocalPath(path).Should().BeFalse();
    }

    [Fact]
    public void ALocalFileIsPassedOn()
    {
        var path = Path.Combine(Path.GetTempPath(), "local.tlb");
        string? read = null;
        var sut = new TypeLibraryNames { Read = (_, _, p) => { read = p; return null; } };
        sut.NameOf(new("Local", null, null, null, null, path));
        read.Should().Be(path);
    }

    public static bool OnWindows => OperatingSystem.IsWindows();

    [Fact(Skip = "A link to a share is a Windows path.", SkipUnless = nameof(OnWindows))]
    public void ALocalLinkToAShareIsNotALocalPath()
    {
        var folder = Directory.CreateTempSubdirectory("typelib-link-").FullName;
        try
        {
            var local = Path.Combine(folder, "local.tlb");
            File.WriteAllText(local, "");
            var toLocal = Path.Combine(folder, "to-local.tlb");
            File.CreateSymbolicLink(toLocal, local);
            var toShare = Path.Combine(folder, "to-share.tlb");
            File.CreateSymbolicLink(toShare, @"\\host.invalid\share\remote.tlb");
            var shareFolder = Path.Combine(folder, "share");
            Directory.CreateSymbolicLink(shareFolder, @"\\host.invalid\share");

            TypeLibReader.IsLocalPath(toLocal).Should().BeTrue();
            TypeLibReader.IsLocalPath(toShare).Should().BeFalse();
            TypeLibReader.IsLocalPath(Path.Combine(shareFolder, "remote.tlb")).Should().BeFalse();
        }
        finally { Directory.Delete(folder, true); }
    }
}
