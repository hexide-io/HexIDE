using System.Diagnostics;
using System.IO;
using System.Text.Json;
using HexIDE.IDE.Profiles;

namespace HexIDE.Tests.Profiles;

public class ProfileContainmentTests
{
    public static bool SupportsContainedReads => OperatingSystem.IsLinux() || OperatingSystem.IsWindows();

    [Theory]
    [InlineData("../x")]
    [InlineData("..\\x")]
    [InlineData("nested/../x")]
    [InlineData("/tmp/x")]
    [InlineData("\\x")]
    [InlineData("C:\\x")]
    [InlineData("C:x")]
    [InlineData("\\\\server\\share\\x")]
    [InlineData("//server/share/x")]
    [InlineData("x:stream")]
    [InlineData("./x")]
    [InlineData("nested//x")]
    [InlineData("nested/")]
    [InlineData("x?y")]
    [InlineData("NUL.json")]
    [InlineData("COM1")]
    [InlineData("Lpt9.json")]
    [InlineData("CONIN$")]
    [InlineData("COM¹.json")]
    [InlineData("nested./x")]
    [InlineData("nested /x")]
    public void UnsafeReferenceIsRefusedInEveryManifestField(string reference)
    {
        foreach (var field in new[] { "keymap", "theme", "settingsDefaults", "languageOverrides.en" })
        {
            var encoded = JsonSerializer.Serialize(reference);
            var member = field == "languageOverrides.en"
                ? "\"languageOverrides\":{\"en\":" + encoded + "}"
                : JsonSerializer.Serialize(field) + ":" + encoded;
            using var document = JsonDocument.Parse("{\"schemaVersion\":1,\"id\":\"example\"," + member + "}");
            Action parse = () => ProfileCatalog.ParseManifest(document.RootElement, "bundle/profile.json", "example");
            var error = parse.Should().Throw<InvalidDataException>().Which;
            error.Data["file"].Should().Be("bundle/profile.json");
            error.Data["field"].Should().Be(field);
            error.Data["code"].Should().Be("UnsafePath");
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SymlinkEscapeIsRefused(bool directoryLink)
    {
        WithBundle((root, outside) =>
        {
            var target = Path.Combine(outside, "payload.json");
            File.WriteAllText(target, "{}");
            var link = Path.Combine(root, "link");
            if (directoryLink)
                Directory.CreateSymbolicLink(link, outside);
            else
                File.CreateSymbolicLink(link, target);
            Refuse(root, directoryLink ? @"link\payload.json" : "link");
        });
    }

    [Fact]
    public void LinkWithinBundleIsAlsoRefused()
    {
        WithBundle((root, _) =>
        {
            File.WriteAllText(Path.Combine(root, "payload.json"), "{}");
            File.CreateSymbolicLink(Path.Combine(root, "link"), "payload.json");
            Refuse(root, "link");
        });
    }

    [Fact]
    public void DanglingLinkIsRefused()
    {
        WithBundle((root, outside) =>
        {
            File.CreateSymbolicLink(Path.Combine(root, "link"), Path.Combine(outside, "missing.json"));
            Refuse(root, "link");
        });
    }

    [WindowsOnlyFact]
    public void JunctionEscapeIsRefused()
    {
        WithBundle((root, outside) =>
        {
            var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
            foreach (var argument in new[] { "/c", "mklink", "/J", Path.Combine(root, "link"), outside })
                start.ArgumentList.Add(argument);
            using var process = Process.Start(start)!;
            process.WaitForExit();
            process.ExitCode.Should().Be(0);
            Refuse(root, "link/payload.json");
        });
    }

    [Theory(Skip = "Contained reads are implemented on Linux and Windows.", SkipUnless = nameof(SupportsContainedReads))]
    [InlineData(false)]
    [InlineData(true)]
    public void LinkSwappedInAfterPreflightIsRefusedDuringRead(bool directoryLink)
    {
        WithBundle((root, outside) =>
        {
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "payload.json"), "inside");
            File.WriteAllText(Path.Combine(outside, "payload.json"), "outside");
            Action read = () => ProfileFileReader.Read(root, "nested/payload.json", beforeOpen: () =>
            {
                if (directoryLink)
                {
                    Directory.Delete(nested, true);
                    Directory.CreateSymbolicLink(nested, outside);
                }
                else
                {
                    File.Delete(Path.Combine(nested, "payload.json"));
                    File.CreateSymbolicLink(Path.Combine(nested, "payload.json"),
                        Path.Combine(outside, "payload.json"));
                }
            });
            read.Should().Throw<InvalidDataException>();
        });
    }

    [Fact(Skip = "Contained reads are implemented on Linux and Windows.", SkipUnless = nameof(SupportsContainedReads))]
    public void BundleRootSwappedForLinkIsRefusedDuringRead()
    {
        WithBundle((root, outside) =>
        {
            File.WriteAllText(Path.Combine(root, "payload.json"), "inside");
            File.WriteAllText(Path.Combine(outside, "payload.json"), "outside");
            Action read = () => ProfileFileReader.Read(root, "payload.json", beforeOpen: () =>
            {
                Directory.Move(root, root + "-old");
                Directory.CreateSymbolicLink(root, outside);
            });
            read.Should().Throw<InvalidDataException>();
        });
    }

    [WindowsOnlyFact]
    public void JunctionSwappedInAfterPreflightIsRefusedDuringRead()
    {
        WithBundle((root, outside) =>
        {
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            File.WriteAllText(Path.Combine(nested, "payload.json"), "inside");
            File.WriteAllText(Path.Combine(outside, "payload.json"), "outside");
            Action read = () => ProfileFileReader.Read(root, "nested/payload.json", beforeOpen: () =>
            {
                Directory.Delete(nested, true);
                var start = new ProcessStartInfo("cmd.exe") { UseShellExecute = false, CreateNoWindow = true };
                foreach (var argument in new[] { "/c", "mklink", "/J", nested, outside })
                    start.ArgumentList.Add(argument);
                using var process = Process.Start(start)!;
                process.WaitForExit();
                process.ExitCode.Should().Be(0);
            });
            read.Should().Throw<InvalidDataException>();
        });
    }

    [Fact(Skip = "Contained reads are implemented on Linux and Windows.", SkipUnless = nameof(SupportsContainedReads))]
    public void ContainedFileIsReadThroughItsOpenedHandle()
    {
        WithBundle((root, _) =>
        {
            File.WriteAllBytes(Path.Combine(root, "payload.json"), [1, 2, 3]);
            ProfileFileReader.Read(root, "payload.json").Should().Equal(1, 2, 3);
        });
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CleanupUnlinksWithoutTouchingEscapeTarget(bool directoryLink)
    {
        WithBundle((root, outside) =>
        {
            var target = Path.Combine(outside, "payload.json");
            File.WriteAllText(target, "outside");
            var nested = Path.Combine(root, "nested");
            Directory.CreateDirectory(nested);
            var link = Path.Combine(nested, "link");
            if (directoryLink)
                Directory.CreateSymbolicLink(link, outside);
            else
                File.CreateSymbolicLink(link, target);

            RemoveLinks(root);

            Directory.EnumerateFileSystemEntries(nested).Should().BeEmpty();
            Directory.Exists(outside).Should().BeTrue();
            File.ReadAllText(target).Should().Be("outside");
        });
    }

    private static void RemoveLinks(string directory)
    {
        foreach (var entry in new DirectoryInfo(directory).EnumerateFileSystemInfos())
        {
            if ((entry.Attributes & FileAttributes.ReparsePoint) != 0)
                entry.Delete();
            else if (entry is DirectoryInfo child)
                RemoveLinks(child.FullName);
        }
    }

    private static void Refuse(string root, string reference)
    {
        using var document = JsonDocument.Parse("{\"schemaVersion\":1,\"id\":\"example\",\"theme\":"
            + JsonSerializer.Serialize(reference) + "}");
        var source = Path.Combine(root, "profile.json");
        Action parse = () => ProfileCatalog.ParseDirectoryManifest(document.RootElement, root, "example");
        var error = parse.Should().Throw<InvalidDataException>().Which;
        error.Data["file"].Should().Be(source);
        error.Data["field"].Should().Be("theme");
        error.Data["code"].Should().Be("UnsafePath");
    }

    private static void WithBundle(Action<string, string> action)
    {
        var temporary = Path.Combine(Path.GetTempPath(), "hexide-containment-" + Guid.NewGuid());
        var root = Path.Combine(temporary, "example");
        var outside = temporary + "-outside";
        Directory.CreateDirectory(root);
        Directory.CreateDirectory(outside);
        var sentinel = Path.Combine(outside, "sentinel.txt");
        File.WriteAllText(sentinel, "escape target");
        try { action(root, outside); }
        finally
        {
            try
            {
                RemoveLinks(temporary);
                Directory.Delete(temporary, true);
                Directory.Exists(outside).Should().BeTrue();
                File.ReadAllText(sentinel).Should().Be("escape target");
            }
            finally { Directory.Delete(outside, true); }
        }
    }
}
