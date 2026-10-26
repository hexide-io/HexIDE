using System.Text;

namespace HexIDE.TwinBasic.Tests;

public sealed class ArchiveProjectTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-archive-" + Guid.NewGuid().ToString("N"));
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ArchiveProjectTests() => Directory.CreateDirectory(directory);
    public void Dispose() => Directory.Delete(directory, recursive: true);

    [Fact]
    public void Published_project_exposes_an_ordered_complete_read_only_tree()
    {
        using var stream = File.OpenRead(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"));
        var project = ArchiveProjectSnapshot.Read(stream, Path.Combine(directory, "impexp.twinproj"), cancellationToken: Token);
        project.IsReadOnly.Should().BeTrue();
        project.Name.Should().Be(project.Container.Root.Name);
        project.Root.Children.Select(x => x.Name).Should().Equal(project.Container.Root.Children.Select(x => x.Name));
        project.EntryCount.Should().Be(17); // Root, five directories and eleven files.
        var sources = project.Root.Children.Single(x => x.Name == "Sources").Should().BeOfType<ArchiveProjectFolder>().Which;
        var source = sources.Children.First().Should().BeOfType<ArchiveProjectDocument>().Which;
        source.IsReadOnly.Should().BeTrue();
        source.ReadText().Status.Should().Be(ArchiveTextStatus.Available);
        source.Identity.Segments.Should().Equal(new[] { "Sources", source.Name });
        project.TryGetEntry(source.Identity, out var found).Should().BeTrue();
        found.Should().BeSameAs(source);
        stream.CanRead.Should().BeTrue();
    }

    [Theory]
    [InlineData("{\"project.buildType\": \"ActiveX DLL\"}", "ActiveX DLL")]
    [InlineData("{\n\t// a comment, as the IDE writes them\n\t\"project.buildType\": \"Standard EXE\",\n}", "Standard EXE")]
    [InlineData("{\"project.appTitle\": \"x\"}", null)]
    [InlineData("{\"project.buildType\": 3}", null)]
    [InlineData("not json", null)]
    public void A_projects_build_type_comes_from_its_Settings_or_is_unknown(string settings, string? expected)
    {
        var project = Read(Container(FileNode("Settings", settings), FileNode("Main.twin", "one")));

        TwinBasicCompilationConstants.BuildTypeOf(project.Container).Should().Be(expected);
    }

    [Fact]
    public void A_project_with_no_Settings_has_no_known_build_type()
    {
        TwinBasicCompilationConstants.BuildTypeOf(Read(Container(FileNode("Main.twin", "one"))).Container).Should().BeNull();
    }

    [Fact]
    public void The_built_ins_are_the_ones_measured_on_beta_1024()
    {
        var at32 = TwinBasicCompilationConstants.BuiltIns(win64: false, build: null, buildType: "Standard EXE");
        at32.Should().BeEquivalentTo(new Dictionary<string, object?>
        {
            ["Win16"] = 0, ["Win32"] = 1, ["Win64"] = 0, ["VBA6"] = 1, ["VBA7"] = 1, ["MAC"] = 0, ["TWINBASIC"] = 1,
            ["TWINBASIC_BUILD"] = null, ["TWINBASIC_BUILD_TYPE"] = "Standard EXE",
        });
        var at64 = TwinBasicCompilationConstants.BuiltIns(win64: true, build: 1024, buildType: null);
        at64["Win64"].Should().Be(1);
        at64["Win32"].Should().Be(1, "Win32 stays 1 in a 64-bit build");
        at64["TWINBASIC_BUILD"].Should().Be(1024);
        at64["twinbasic_build_type"].Should().BeNull("names are found without regard to case, and an unknown one is null");
    }

    [Fact]
    public void Identity_is_project_qualified_and_stable_across_new_snapshots()
    {
        var bytes = Container(FileNode("Main.twin", "one"));
        var first = Read(bytes, "a.twinproj");
        var again = Read(bytes, "a.twinproj");
        var other = Read(bytes, "b.twinproj");
        var id = Document(first).Identity;
        Document(again).Identity.Should().Be(id);
        Document(again).Identity.GetHashCode().Should().Be(id.GetHashCode());
        Document(other).Identity.Should().NotBe(id);
        other.TryGetEntry(id, out _).Should().BeFalse();
        again.TryGetEntry(id, out _).Should().BeTrue();
    }

    [Fact]
    public void Origin_paths_are_normalized_without_resolving_member_names()
    {
        var bytes = Container(FileNode("../Main.twin", "one"));
        var first = Read(bytes, "a.twinproj");
        var again = Read(bytes, Path.Combine("not-created", "..", "a.twinproj"));
        Document(again).Identity.Should().Be(Document(first).Identity);
        Document(first).Identity.Segments.Should().Equal(new[] { "../Main.twin" });
        Directory.Exists(Path.Combine(directory, "not-created")).Should().BeFalse();
    }

    [Fact]
    public void Origin_case_follows_the_host_policy_but_member_case_is_always_ordinal()
    {
        var bytes = Container(FileNode("a.twin", "one"), FileNode("A.twin", "two"));
        var first = Read(bytes, "p.twinproj");
        var other = Read(bytes, "P.twinproj");
        first.Root.Identity.Equals(other.Root.Identity).Should().Be(OperatingSystem.IsWindows());
        first.Root.Children[1].Identity.Should().NotBe(first.Root.Children[0].Identity);
    }

    [Fact]
    public void Identity_uses_segments_not_a_joined_path_or_uri()
    {
        var project = Read(Container(
            FileNode("a/b.twin", "flat"), Folder("a", FileNode("b.twin", "nested")),
            FileNode("", "empty"), FileNode(".", "dot"), FileNode("..", "parent"),
            FileNode("a\\b.twin", "backslash"), FileNode("%2F?#.twin", "punctuation")));
        var nested = project.Root.Children[1].Should().BeOfType<ArchiveProjectFolder>().Which.Children[0];
        nested.Identity.Should().NotBe(project.Root.Children[0].Identity);
        project.Root.Children[2].Identity.Should().NotBe(project.Root.Identity);
        project.EntryCount.Should().Be(9);
        project.Root.Children[6].Identity.Segments.Single().Should().Be("%2F?#.twin");
    }

    [Fact]
    public void Duplicate_siblings_are_refused_without_losing_the_raw_readers_inspection_support()
    {
        var bytes = Container(FileNode("same", "one"), Folder("same"));
        using var stream = new MemoryStream(bytes);
        TwinBasicContainerReader.Read(stream, cancellationToken: Token).Root.Children.Length.Should().Be(2);
        var error = FluentActions.Invoking(() => Read(bytes)).Should().ThrowExactly<ArchiveProjectAmbiguityException>().Which;
        error.EntryName.Should().Be("same");
        error.Parent.Segments.Should().BeEmpty();
    }

    [Fact]
    public void Equal_leaf_names_in_different_folders_are_not_ambiguous()
    {
        var project = Read(Container(Folder("one", FileNode("main.twin", "1")), Folder("two", FileNode("main.twin", "2"))));
        var one = project.Root.Children[0].Should().BeOfType<ArchiveProjectFolder>().Which.Children[0];
        var two = project.Root.Children[1].Should().BeOfType<ArchiveProjectFolder>().Which.Children[0];
        two.Identity.Should().NotBe(one.Identity);
    }

    [Theory]
    [InlineData("Main.twin", 0, true)]
    [InlineData("Main.TWIN", 0, true)]
    [InlineData("Settings", 4, true)]
    [InlineData(".meta", 0, true)]
    [InlineData("notes.txt", 0, true)]
    [InlineData("README.md", 0, true)]
    [InlineData("settings.json", 0, true)]
    [InlineData("manifest.xml", 0, true)]
    [InlineData("Form.tbform", 0, false)]
    [InlineData("image.png", 0, false)]
    [InlineData("unknown", 255, false)]
    public void Preview_only_decodes_established_text_types(string name, byte category, bool isText)
    {
        var file = Document(Read(Container(FileNode(name, "not necessarily valid JSON", category))));
        var text = file.ReadText();
        text.Status.Should().Be(isText ? ArchiveTextStatus.Available : ArchiveTextStatus.UnsupportedType);
        text.Text.Should().Be(isText ? "not necessarily valid JSON" : null);
    }

    [Fact]
    public void Text_preserves_line_endings_and_unicode_while_raw_content_preserves_the_bom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é\r\n空\n")];
        var file = Document(Read(Container(FileNode("a.twin", bytes))));
        file.ReadText().Text.Should().Be("é\r\n空\n");
        file.File.Content.Should().Equal(bytes);
    }

    [Fact]
    public void Invalid_utf8_and_oversized_text_remain_available_as_raw_bytes()
    {
        var invalid = Document(Read(Container(FileNode("a.twin", new byte[] { 0xFF }))));
        invalid.ReadText().Should().Be(new ArchiveText(ArchiveTextStatus.InvalidUtf8, null));
        invalid.File.Content.Should().Equal(new byte[] { 0xFF });
        var valid = Document(Read(Container(FileNode("a.twin", "1234"))));
        valid.ReadText(3).Should().Be(new ArchiveText(ArchiveTextStatus.TooLarge, null));
        valid.ReadText(4).Text.Should().Be("1234");
        FluentActions.Invoking(() => valid.ReadText(-1)).Should().ThrowExactly<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Empty_text_is_available_and_not_confused_with_an_unavailable_preview()
    {
        var file = Document(Read(Container(FileNode("a.twin", ""))));
        file.ReadText(0).Should().Be(new ArchiveText(ArchiveTextStatus.Available, ""));
    }

    [Fact]
    public void Metadata_and_empty_folders_survive_projection()
    {
        var project = Read(Container(Folder("empty"), FileNode("source.twin", "source", 254, uint.MaxValue)));
        project.Root.Children[0].Should().BeOfType<ArchiveProjectFolder>().Which.Children.Should().BeEmpty();
        var file = project.Root.Children[1].Should().BeOfType<ArchiveProjectDocument>().Which;
        file.Source.Category.Should().Be(254);
        file.Source.Flags.Should().Be(uint.MaxValue);
        file.Source.Revision.Should().Be(ulong.MaxValue);
        file.File.Revisions.Should().Equal(new uint[] { 23 });
    }

    [Fact]
    public void Open_is_idempotent_until_explicit_reload_and_releases_the_file()
    {
        var path = Write("project.twinproj", Container(FileNode("main.twin", "old")));
        var workspace = new ArchiveProjectWorkspace();
        var first = workspace.Open(path, cancellationToken: Token);
        File.WriteAllBytes(path, Container(FileNode("main.twin", "new")));
        workspace.Open(path, cancellationToken: Token).Should().BeSameAs(first);
        workspace.Projects.Should().ContainSingle();
        Document(first).ReadText().Text.Should().Be("old");
        var replacement = workspace.Reload(path, cancellationToken: Token);
        replacement.Should().NotBeSameAs(first);
        Document(replacement).ReadText().Text.Should().Be("new");
        Document(first).ReadText().Text.Should().Be("old");
        Document(replacement).Identity.Should().Be(Document(first).Identity);
        workspace.Projects.Single().Should().BeSameAs(replacement);
    }

    [Fact]
    public void Reload_changes_the_whole_tree_and_root_metadata_together()
    {
        var path = Write("project.twinproj", Container(FileNode("removed.twin", "old")));
        var workspace = new ArchiveProjectWorkspace();
        var original = workspace.Open(path, cancellationToken: Token);
        File.WriteAllBytes(path, Container("Renamed", Folder("new-folder", FileNode("new.twin", "new"))));
        var replacement = workspace.Reload(path, cancellationToken: Token);
        replacement.Name.Should().Be("Renamed");
        replacement.Root.Identity.Should().Be(original.Root.Identity);
        replacement.TryGetEntry(Document(original).Identity, out _).Should().BeFalse();
        replacement.Root.Children.Single().Name.Should().Be("new-folder");
        original.Name.Should().Be("Project");
    }

    [Fact]
    public void Failed_open_never_registers_a_partial_project()
    {
        var workspace = new ArchiveProjectWorkspace();
        var path = Write("bad.twinproj", [1, 2, 3]);
        FluentActions.Invoking(() => workspace.Open(path, cancellationToken: Token)).Should().ThrowExactly<TwinBasicFormatException>();
        File.WriteAllBytes(path, Container(FileNode("same", "1"), FileNode("same", "2")));
        FluentActions.Invoking(() => workspace.Open(path, cancellationToken: Token)).Should().ThrowExactly<ArchiveProjectAmbiguityException>();
        workspace.Projects.Should().BeEmpty();
    }

    [Fact]
    public void Failed_reload_keeps_the_previous_snapshot_for_corrupt_ambiguous_and_missing_files()
    {
        var workspace = new ArchiveProjectWorkspace();
        var path = Write("project.twinproj", Container(FileNode("main.twin", "old")));
        var original = workspace.Open(path, cancellationToken: Token);
        File.WriteAllBytes(path, [1, 2, 3]);
        FluentActions.Invoking(() => workspace.Reload(path, cancellationToken: Token)).Should().ThrowExactly<TwinBasicFormatException>();
        workspace.Projects.Single().Should().BeSameAs(original);
        File.WriteAllBytes(path, Container(Folder("d", FileNode("same", "1"), FileNode("same", "2"))));
        FluentActions.Invoking(() => workspace.Reload(path, cancellationToken: Token)).Should().ThrowExactly<ArchiveProjectAmbiguityException>();
        workspace.Projects.Single().Should().BeSameAs(original);
        File.Delete(path);
        FluentActions.Invoking(() => workspace.Reload(path, cancellationToken: Token)).Should().ThrowExactly<FileNotFoundException>();
        workspace.Projects.Single().Should().BeSameAs(original);
        Document(original).ReadText().Text.Should().Be("old");
    }

    [Fact]
    public void Cancellation_and_limits_do_not_publish_or_replace_projects()
    {
        var workspace = new ArchiveProjectWorkspace();
        var path = Write("project.twinproj", Container(FileNode("main.twin", "old")));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        FluentActions.Invoking(() => workspace.Open(path, cancellationToken: cancelled.Token)).Should().ThrowExactly<OperationCanceledException>();
        FluentActions.Invoking(() => workspace.Open(path, new(MaxEntries: 1), Token)).Should().ThrowExactly<TwinBasicFormatException>();
        workspace.Projects.Should().BeEmpty();
        var original = workspace.Open(path, cancellationToken: Token);
        FluentActions.Invoking(() => workspace.Reload(path, cancellationToken: cancelled.Token)).Should().ThrowExactly<OperationCanceledException>();
        FluentActions.Invoking(() => workspace.Reload(path, new(MaxEntries: 1), Token)).Should().ThrowExactly<TwinBasicFormatException>();
        workspace.Projects.Single().Should().BeSameAs(original);
    }

    [Fact]
    public void Close_and_reopen_preserve_identity_without_writing_sidecars_or_extracting_files()
    {
        var bytes = Container(FileNode("main.twin", "source"));
        var path = Write("project.twinproj", bytes);
        var before = Directory.GetFileSystemEntries(directory);
        var workspace = new ArchiveProjectWorkspace();
        var original = workspace.Open(path, cancellationToken: Token);
        workspace.Close(path).Should().BeTrue();
        workspace.Close(path).Should().BeFalse();
        FluentActions.Invoking(() => workspace.Reload(path, cancellationToken: Token)).Should().ThrowExactly<InvalidOperationException>();
        workspace.Open(path, cancellationToken: Token).Root.Identity.Should().Be(original.Root.Identity);
        workspace.CloseAll();
        workspace.Projects.Should().BeEmpty();
        Directory.GetFileSystemEntries(directory).Should().Equal(before);
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    private ArchiveProjectSnapshot Read(byte[] bytes, string name = "project.twinproj")
    {
        using var stream = new MemoryStream(bytes);
        return ArchiveProjectSnapshot.Read(stream, Path.Combine(directory, name), cancellationToken: Token);
    }

    private string Write(string name, byte[] bytes)
    {
        var path = Path.Combine(directory, name);
        File.WriteAllBytes(path, bytes);
        return path;
    }

    private static ArchiveProjectDocument Document(ArchiveProjectSnapshot snapshot) =>
        snapshot.Root.Children.Single().Should().BeOfType<ArchiveProjectDocument>().Which;

    private static byte[] Container(params Action<BinaryWriter>[] children) => Container("Project", children);
    private static byte[] Container(string name, params Action<BinaryWriter>[] children)
    {
        using var stream = new MemoryStream();
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);
        writer.Write(0xEA0BA51Cu);
        Header(writer, 1, name);
        writer.Write((uint)children.Length);
        foreach (var child in children) child(writer);
        return stream.ToArray();
    }

    private static Action<BinaryWriter> Folder(string name, params Action<BinaryWriter>[] children) => writer =>
    {
        Header(writer, 2, name); writer.Write((uint)children.Length);
        foreach (var child in children) child(writer);
    };

    private static Action<BinaryWriter> FileNode(string name, string text, byte category = 0, uint flags = 0) =>
        FileNode(name, Encoding.UTF8.GetBytes(text), category, flags);

    private static Action<BinaryWriter> FileNode(string name, byte[] bytes, byte category = 0, uint flags = 0) => writer =>
    {
        Header(writer, 1, name, category, flags);
        writer.Write((uint)bytes.Length); writer.Write(bytes); writer.Write(1u); writer.Write(23u);
    };

    private static void Header(BinaryWriter writer, short kind, string name, byte category = 0, uint flags = 0)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        writer.Write(kind); writer.Write((uint)bytes.Length); writer.Write(bytes);
        writer.Write(ulong.MaxValue); writer.Write(flags); writer.Write(category);
    }
}
