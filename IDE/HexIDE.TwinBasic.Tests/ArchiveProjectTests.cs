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
        Assert.True(project.IsReadOnly);
        Assert.Equal(project.Container.Root.Name, project.Name);
        Assert.Equal(project.Container.Root.Children.Select(x => x.Name), project.Root.Children.Select(x => x.Name));
        Assert.Equal(17, project.EntryCount); // Root, five directories and eleven files.
        var sources = Assert.IsType<ArchiveProjectFolder>(project.Root.Children.Single(x => x.Name == "Sources"));
        var source = Assert.IsType<ArchiveProjectDocument>(sources.Children.First());
        Assert.True(source.IsReadOnly);
        Assert.Equal(ArchiveTextStatus.Available, source.ReadText().Status);
        Assert.Equal(new[] { "Sources", source.Name }, source.Identity.Segments);
        Assert.True(project.TryGetEntry(source.Identity, out var found));
        Assert.Same(source, found);
        Assert.True(stream.CanRead);
    }

    [Fact]
    public void Identity_is_project_qualified_and_stable_across_new_snapshots()
    {
        var bytes = Container(FileNode("Main.twin", "one"));
        var first = Read(bytes, "a.twinproj");
        var again = Read(bytes, "a.twinproj");
        var other = Read(bytes, "b.twinproj");
        var id = Document(first).Identity;
        Assert.Equal(id, Document(again).Identity);
        Assert.Equal(id.GetHashCode(), Document(again).Identity.GetHashCode());
        Assert.NotEqual(id, Document(other).Identity);
        Assert.False(other.TryGetEntry(id, out _));
        Assert.True(again.TryGetEntry(id, out _));
    }

    [Fact]
    public void Origin_paths_are_normalized_without_resolving_member_names()
    {
        var bytes = Container(FileNode("../Main.twin", "one"));
        var first = Read(bytes, "a.twinproj");
        var again = Read(bytes, Path.Combine("not-created", "..", "a.twinproj"));
        Assert.Equal(Document(first).Identity, Document(again).Identity);
        Assert.Equal(new[] { "../Main.twin" }, Document(first).Identity.Segments);
        Assert.False(Directory.Exists(Path.Combine(directory, "not-created")));
    }

    [Fact]
    public void Origin_case_follows_the_host_policy_but_member_case_is_always_ordinal()
    {
        var bytes = Container(FileNode("a.twin", "one"), FileNode("A.twin", "two"));
        var first = Read(bytes, "p.twinproj");
        var other = Read(bytes, "P.twinproj");
        Assert.Equal(OperatingSystem.IsWindows(), first.Root.Identity.Equals(other.Root.Identity));
        Assert.NotEqual(first.Root.Children[0].Identity, first.Root.Children[1].Identity);
    }

    [Fact]
    public void Identity_uses_segments_not_a_joined_path_or_uri()
    {
        var project = Read(Container(
            FileNode("a/b.twin", "flat"), Folder("a", FileNode("b.twin", "nested")),
            FileNode("", "empty"), FileNode(".", "dot"), FileNode("..", "parent"),
            FileNode("a\\b.twin", "backslash"), FileNode("%2F?#.twin", "punctuation")));
        var nested = Assert.IsType<ArchiveProjectFolder>(project.Root.Children[1]).Children[0];
        Assert.NotEqual(project.Root.Children[0].Identity, nested.Identity);
        Assert.NotEqual(project.Root.Identity, project.Root.Children[2].Identity);
        Assert.Equal(9, project.EntryCount);
        Assert.Equal("%2F?#.twin", project.Root.Children[6].Identity.Segments.Single());
    }

    [Fact]
    public void Duplicate_siblings_are_refused_without_losing_the_raw_readers_inspection_support()
    {
        var bytes = Container(FileNode("same", "one"), Folder("same"));
        using var stream = new MemoryStream(bytes);
        Assert.Equal(2, TwinBasicContainerReader.Read(stream, cancellationToken: Token).Root.Children.Length);
        var error = Assert.Throws<ArchiveProjectAmbiguityException>(() => Read(bytes));
        Assert.Equal("same", error.EntryName);
        Assert.Empty(error.Parent.Segments);
    }

    [Fact]
    public void Equal_leaf_names_in_different_folders_are_not_ambiguous()
    {
        var project = Read(Container(Folder("one", FileNode("main.twin", "1")), Folder("two", FileNode("main.twin", "2"))));
        var one = Assert.IsType<ArchiveProjectFolder>(project.Root.Children[0]).Children[0];
        var two = Assert.IsType<ArchiveProjectFolder>(project.Root.Children[1]).Children[0];
        Assert.NotEqual(one.Identity, two.Identity);
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
        Assert.Equal(isText ? ArchiveTextStatus.Available : ArchiveTextStatus.UnsupportedType, text.Status);
        Assert.Equal(isText ? "not necessarily valid JSON" : null, text.Text);
    }

    [Fact]
    public void Text_preserves_line_endings_and_unicode_while_raw_content_preserves_the_bom()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("é\r\n空\n")];
        var file = Document(Read(Container(FileNode("a.twin", bytes))));
        Assert.Equal("é\r\n空\n", file.ReadText().Text);
        Assert.Equal(bytes, file.File.Content);
    }

    [Fact]
    public void Invalid_utf8_and_oversized_text_remain_available_as_raw_bytes()
    {
        var invalid = Document(Read(Container(FileNode("a.twin", new byte[] { 0xFF }))));
        Assert.Equal(new ArchiveText(ArchiveTextStatus.InvalidUtf8, null), invalid.ReadText());
        Assert.Equal(new byte[] { 0xFF }, invalid.File.Content);
        var valid = Document(Read(Container(FileNode("a.twin", "1234"))));
        Assert.Equal(new ArchiveText(ArchiveTextStatus.TooLarge, null), valid.ReadText(3));
        Assert.Equal("1234", valid.ReadText(4).Text);
        Assert.Throws<ArgumentOutOfRangeException>(() => valid.ReadText(-1));
    }

    [Fact]
    public void Empty_text_is_available_and_not_confused_with_an_unavailable_preview()
    {
        var file = Document(Read(Container(FileNode("a.twin", ""))));
        Assert.Equal(new ArchiveText(ArchiveTextStatus.Available, ""), file.ReadText(0));
    }

    [Fact]
    public void Metadata_and_empty_folders_survive_projection()
    {
        var project = Read(Container(Folder("empty"), FileNode("source.twin", "source", 254, uint.MaxValue)));
        Assert.Empty(Assert.IsType<ArchiveProjectFolder>(project.Root.Children[0]).Children);
        var file = Assert.IsType<ArchiveProjectDocument>(project.Root.Children[1]);
        Assert.Equal(254, file.Source.Category);
        Assert.Equal(uint.MaxValue, file.Source.Flags);
        Assert.Equal(ulong.MaxValue, file.Source.Revision);
        Assert.Equal(new uint[] { 23 }, file.File.Revisions);
    }

    [Fact]
    public void Open_is_idempotent_until_explicit_reload_and_releases_the_file()
    {
        var path = Write("project.twinproj", Container(FileNode("main.twin", "old")));
        var workspace = new ArchiveProjectWorkspace();
        var first = workspace.Open(path, cancellationToken: Token);
        File.WriteAllBytes(path, Container(FileNode("main.twin", "new")));
        Assert.Same(first, workspace.Open(path, cancellationToken: Token));
        Assert.Single(workspace.Projects);
        Assert.Equal("old", Document(first).ReadText().Text);
        var replacement = workspace.Reload(path, cancellationToken: Token);
        Assert.NotSame(first, replacement);
        Assert.Equal("new", Document(replacement).ReadText().Text);
        Assert.Equal("old", Document(first).ReadText().Text);
        Assert.Equal(Document(first).Identity, Document(replacement).Identity);
        Assert.Same(replacement, workspace.Projects.Single());
    }

    [Fact]
    public void Reload_changes_the_whole_tree_and_root_metadata_together()
    {
        var path = Write("project.twinproj", Container(FileNode("removed.twin", "old")));
        var workspace = new ArchiveProjectWorkspace();
        var original = workspace.Open(path, cancellationToken: Token);
        File.WriteAllBytes(path, Container("Renamed", Folder("new-folder", FileNode("new.twin", "new"))));
        var replacement = workspace.Reload(path, cancellationToken: Token);
        Assert.Equal("Renamed", replacement.Name);
        Assert.Equal(original.Root.Identity, replacement.Root.Identity);
        Assert.False(replacement.TryGetEntry(Document(original).Identity, out _));
        Assert.Equal("new-folder", replacement.Root.Children.Single().Name);
        Assert.Equal("Project", original.Name);
    }

    [Fact]
    public void Failed_open_never_registers_a_partial_project()
    {
        var workspace = new ArchiveProjectWorkspace();
        var path = Write("bad.twinproj", [1, 2, 3]);
        Assert.Throws<TwinBasicFormatException>(() => workspace.Open(path, cancellationToken: Token));
        File.WriteAllBytes(path, Container(FileNode("same", "1"), FileNode("same", "2")));
        Assert.Throws<ArchiveProjectAmbiguityException>(() => workspace.Open(path, cancellationToken: Token));
        Assert.Empty(workspace.Projects);
    }

    [Fact]
    public void Failed_reload_keeps_the_previous_snapshot_for_corrupt_ambiguous_and_missing_files()
    {
        var workspace = new ArchiveProjectWorkspace();
        var path = Write("project.twinproj", Container(FileNode("main.twin", "old")));
        var original = workspace.Open(path, cancellationToken: Token);
        File.WriteAllBytes(path, [1, 2, 3]);
        Assert.Throws<TwinBasicFormatException>(() => workspace.Reload(path, cancellationToken: Token));
        Assert.Same(original, workspace.Projects.Single());
        File.WriteAllBytes(path, Container(Folder("d", FileNode("same", "1"), FileNode("same", "2"))));
        Assert.Throws<ArchiveProjectAmbiguityException>(() => workspace.Reload(path, cancellationToken: Token));
        Assert.Same(original, workspace.Projects.Single());
        File.Delete(path);
        Assert.Throws<FileNotFoundException>(() => workspace.Reload(path, cancellationToken: Token));
        Assert.Same(original, workspace.Projects.Single());
        Assert.Equal("old", Document(original).ReadText().Text);
    }

    [Fact]
    public void Cancellation_and_limits_do_not_publish_or_replace_projects()
    {
        var workspace = new ArchiveProjectWorkspace();
        var path = Write("project.twinproj", Container(FileNode("main.twin", "old")));
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Assert.Throws<OperationCanceledException>(() => workspace.Open(path, cancellationToken: cancelled.Token));
        Assert.Throws<TwinBasicFormatException>(() => workspace.Open(path, new(MaxEntries: 1), Token));
        Assert.Empty(workspace.Projects);
        var original = workspace.Open(path, cancellationToken: Token);
        Assert.Throws<OperationCanceledException>(() => workspace.Reload(path, cancellationToken: cancelled.Token));
        Assert.Throws<TwinBasicFormatException>(() => workspace.Reload(path, new(MaxEntries: 1), Token));
        Assert.Same(original, workspace.Projects.Single());
    }

    [Fact]
    public void Close_and_reopen_preserve_identity_without_writing_sidecars_or_extracting_files()
    {
        var bytes = Container(FileNode("main.twin", "source"));
        var path = Write("project.twinproj", bytes);
        var before = Directory.GetFileSystemEntries(directory);
        var workspace = new ArchiveProjectWorkspace();
        var original = workspace.Open(path, cancellationToken: Token);
        Assert.True(workspace.Close(path));
        Assert.False(workspace.Close(path));
        Assert.Throws<InvalidOperationException>(() => workspace.Reload(path, cancellationToken: Token));
        Assert.Equal(original.Root.Identity, workspace.Open(path, cancellationToken: Token).Root.Identity);
        workspace.CloseAll();
        Assert.Empty(workspace.Projects);
        Assert.Equal(before, Directory.GetFileSystemEntries(directory));
        Assert.Equal(bytes, File.ReadAllBytes(path));
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
        Assert.IsType<ArchiveProjectDocument>(snapshot.Root.Children.Single());

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
