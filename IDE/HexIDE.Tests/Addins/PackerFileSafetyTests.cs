using HexIDE.AddinPacker;

namespace HexIDE.Tests.Addins;

public sealed class PackerFileSafetyTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "hexide-packer-safety-" + Guid.NewGuid().ToString("N"));

    public PackerFileSafetyTests() => Directory.CreateDirectory(_root);
    public void Dispose() => Directory.Delete(_root, true);

    [Theory]
    [InlineData("intermediate.json")]
    [InlineData("intermediate.sig")]
    [InlineData("publisher.json")]
    [InlineData("publisher.sig")]
    [InlineData("hexide-root.pub")]
    [InlineData("hexide-firstparty.pub")]
    [InlineData("firstparty.key")]
    [InlineData("root.key")]
    [InlineData("intermediate.key")]
    public void Generating_trust_preserves_existing_output(string name)
    {
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(output);
        var existing = Path.Combine(output, name);
        File.WriteAllText(existing, "user data");

        var generate = () => PackageSigner.GenerateDevTrust(output);

        generate.Should().Throw<IOException>();
        File.ReadAllText(existing).Should().Be("user data");
        Directory.GetFiles(output).Should().Equal(existing);
    }

    [Theory]
    [InlineData("revocations.json")]
    [InlineData("revocations.sig")]
    public void Signing_revocations_preserves_existing_output(string name)
    {
        var trust = Path.Combine(_root, "trust");
        PackageSigner.GenerateDevTrust(trust);
        var input = Path.Combine(_root, "input.json");
        File.WriteAllText(input, "{}");
        var output = Path.Combine(_root, "output");
        Directory.CreateDirectory(output);
        var existing = Path.Combine(output, name);
        File.WriteAllText(existing, "user data");

        var sign = () => PackageSigner.SignRevocations(input, Path.Combine(trust, "root.key"), output);

        sign.Should().Throw<IOException>();
        File.ReadAllText(existing).Should().Be("user data");
        Directory.GetFiles(output).Should().Equal(existing);
    }
    private PackRequest Request()
    {
        var source = Path.Combine(_root, "source");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "entry.dll"), "first package");
        var trust = Path.Combine(_root, "trust");
        PackageSigner.GenerateDevTrust(trust);
        return new PackRequest { SrcDir = source, OutDir = Path.Combine(_root, "package") + Path.DirectorySeparatorChar,
            TrustDir = trust, Entry = "entry.dll", Id = "test.package", Title = "Test", Version = "1.0" };
    }

    [Fact]
    public void Packing_refuses_unrelated_output_without_changes()
    {
        var request = Request();
        Directory.CreateDirectory(request.OutDir);
        var userFile = Path.Combine(request.OutDir, "notes.txt");
        File.WriteAllText(userFile, "user data");
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<IOException>().WithMessage("*" + Path.TrimEndingDirectorySeparator(request.OutDir) + "*is not unchanged output of this tool*Nothing was changed*Choose another folder or move this one aside*");
        Directory.GetFiles(request.OutDir).Should().Equal(userFile);
        File.ReadAllText(userFile).Should().Be("user data");
    }

    [Fact]
    public void Packing_replaces_its_previous_package()
    {
        var request = Request();
        Directory.CreateDirectory(Path.Combine(request.SrcDir, "nested", "dependency"));
        File.WriteAllText(Path.Combine(request.SrcDir, "nested", "dependency", "old.dll"), "old dependency");
        request.Include.Add("nested/dependency/old.dll");
        PackageSigner.Pack(request);
        request.Include.Clear();
        File.WriteAllText(Path.Combine(request.SrcDir, request.Entry), "second package");
        PackageSigner.Pack(request);
        File.ReadAllText(Path.Combine(request.OutDir, request.Entry)).Should().Be("second package");
        Directory.Exists(Path.Combine(request.OutDir, "nested")).Should().BeFalse();
        var signature = File.ReadAllBytes(Path.Combine(request.OutDir, "addin.sig"));
        request.Include.Add("missing.dll");
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<IOException>();
        File.ReadAllText(Path.Combine(request.OutDir, request.Entry)).Should().Be("second package");
        File.ReadAllBytes(Path.Combine(request.OutDir, "addin.sig")).Should().Equal(signature);
    }

    [Fact]
    public void Failed_pack_preserves_previous_package()
    {
        var request = Request();
        PackageSigner.Pack(request);
        var before = Directory.GetFiles(request.OutDir).ToDictionary(p => Path.GetFileName(p)!, File.ReadAllBytes);
        File.WriteAllText(Path.Combine(request.SrcDir, request.Entry), "changed source");
        request.Include.Add("missing.dll");
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<IOException>();
        foreach (var file in before)
            File.ReadAllBytes(Path.Combine(request.OutDir, file.Key!)).Should().Equal(file.Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Packing_refuses_added_or_edited_user_files(bool edit)
    {
        var request = Request();
        PackageSigner.Pack(request);
        var userFile = Path.Combine(request.OutDir, edit ? request.Entry : "notes.txt");
        File.WriteAllText(userFile, "user data");
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<IOException>();
        File.ReadAllText(userFile).Should().Be("user data");
    }

    [Theory]
    [InlineData("addin.json")]
    [InlineData("addin.sig")]
    [InlineData("publisher.json")]
    [InlineData("publisher.sig")]
    [InlineData("intermediate.json")]
    [InlineData("intermediate.sig")]
    public void Packing_refuses_edited_envelope(string name)
    {
        var request = Request();
        PackageSigner.Pack(request);
        var userFile = Path.Combine(request.OutDir, name);
        File.WriteAllText(userFile, "user data");
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<IOException>();
        File.ReadAllText(userFile).Should().Be("user data");
    }

    [Fact]
    public void Packing_accepts_empty_directory()
    {
        var request = Request();
        Directory.CreateDirectory(request.OutDir);
        var pack = () => PackageSigner.Pack(request);
        pack.Should().NotThrow();
        File.ReadAllText(Path.Combine(request.OutDir, request.Entry)).Should().Be("first package");
    }

    public static bool CanCreateSymbolicLinks => !OperatingSystem.IsWindows();

    [Theory(Skip = "Creating symbolic links on Windows requires developer mode or elevation", SkipUnless = nameof(CanCreateSymbolicLinks))]
    [InlineData(false)]
    [InlineData(true)]
    public void Packing_refuses_linked_output_but_accepts_linked_ancestor(bool ancestor)
    {
        var request = Request();
        var target = Path.Combine(_root, "target");
        Directory.CreateDirectory(target);
        Directory.CreateSymbolicLink(Path.TrimEndingDirectorySeparator(request.OutDir), target);
        var linkedRequest = new PackRequest { SrcDir = request.SrcDir,
            OutDir = ancestor ? Path.Combine(request.OutDir, "nested") : Path.TrimEndingDirectorySeparator(request.OutDir),
            TrustDir = request.TrustDir, Entry = request.Entry, Id = request.Id,
            Title = request.Title, Version = request.Version };
        var pack = () => PackageSigner.Pack(linkedRequest);
        if (ancestor)
        {
            pack.Should().NotThrow();
            File.ReadAllText(Path.Combine(target, "nested", request.Entry)).Should().Be("first package");
            File.WriteAllText(Path.Combine(request.SrcDir, request.Entry), "replacement");
            PackageSigner.Pack(linkedRequest);
            File.ReadAllText(Path.Combine(target, "nested", request.Entry)).Should().Be("replacement");
        }
        else
        {
            pack.Should().Throw<IOException>().WithMessage("*" + linkedRequest.OutDir + "*is or contains a link*Nothing was changed*Choose another folder or move this one aside*");
            Directory.EnumerateFileSystemEntries(target).Should().BeEmpty();
        }
    }

    [Theory]
    [InlineData("../escape.dll")]
    [InlineData("nested/../../escape.dll")]
    [InlineData("rooted")]
    public void Packing_refuses_payload_paths_that_escape_staging(string relative)
    {
        var request = Request();
        var outside = Path.Combine(_root, "escape.dll");
        File.WriteAllText(outside, "user data");
        var invalid = new PackRequest { SrcDir = request.SrcDir, OutDir = request.OutDir,
            TrustDir = request.TrustDir, Entry = relative == "rooted" ? outside : relative,
            Id = request.Id, Title = request.Title, Version = request.Version };
        var pack = () => PackageSigner.Pack(invalid);
        pack.Should().Throw<IOException>();
        File.ReadAllText(outside).Should().Be("user data");
        Directory.Exists(request.OutDir).Should().BeFalse();
    }

    [Theory(Skip = "Creating symbolic links on Windows requires developer mode or elevation", SkipUnless = nameof(CanCreateSymbolicLinks))]
    [InlineData("file")]
    [InlineData("directory")]
    [InlineData("dangling")]
    public void Packing_refuses_links_inside_previous_package(string kind)
    {
        var request = Request();
        PackageSigner.Pack(request);
        var target = Path.Combine(_root, "user-data");
        Directory.CreateDirectory(target);
        File.WriteAllText(Path.Combine(target, "notes.txt"), "user data");
        var link = Path.Combine(request.OutDir, "link");
        if (kind == "directory") Directory.CreateSymbolicLink(link, target);
        else File.CreateSymbolicLink(link, Path.Combine(target, kind == "file" ? "notes.txt" : "missing"));
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<IOException>().WithMessage("*contains a link*Nothing was changed*Choose another folder or move this one aside*");
        File.GetAttributes(link).Should().HaveFlag(FileAttributes.ReparsePoint);
        File.ReadAllText(Path.Combine(target, "notes.txt")).Should().Be("user data");
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Packing_refuses_missing_payload_or_extra_empty_directory(bool missing)
    {
        var request = Request();
        PackageSigner.Pack(request);
        if (missing) File.Delete(Path.Combine(request.OutDir, request.Entry));
        var empty = Path.Combine(request.OutDir, "user-directory");
        if (!missing) Directory.CreateDirectory(empty);
        var pack = () => PackageSigner.Pack(request);
        pack.Should().Throw<IOException>();
        File.Exists(Path.Combine(request.OutDir, request.Entry)).Should().Be(!missing);
        Directory.Exists(empty).Should().Be(!missing);
    }

}
