using System.Diagnostics;
using System.Text;
using System.Text.Json.Nodes;

namespace HexIDE.TwinBasic.Tests;

public sealed class ArchiveSourceFolderTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-folder-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(directory, "project.twinproj");
    private string Target => Path.Combine(directory, ".hexide", ".twinproj", "project.twinproj");
    private string Manifest => Path.Combine(directory, ".hexide", ".twinproj", ".manifests", "project.twinproj.json");
    private string Control => Path.Combine(directory, ".hexide", ".twinproj-sync", "project.twinproj");
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    public ArchiveSourceFolderTests() => Directory.CreateDirectory(directory);
    public void Dispose()
    {
        // Git object files are read-only on Windows. All paths are inside this test's private directory.
        if (OperatingSystem.IsWindows())
            foreach (var file in Directory.GetFiles(directory, "*", SearchOption.AllDirectories))
                File.SetAttributes(file, File.GetAttributes(file) & ~FileAttributes.ReadOnly);
        Directory.Delete(directory, recursive: true);
    }
    private ArchiveFolderResult Sync() => new ArchiveSourceFolder().Synchronize(Source, Token);

    [Fact]
    public void Published_fixture_preserves_every_file_and_all_entry_metadata()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), Source);
        var original = File.ReadAllBytes(Source);
        var result = Sync();
        result.Changed.Should().BeTrue();
        result.Directory.Should().EndWith(Path.Combine(".hexide", ".twinproj", "project.twinproj"));
        Directory.Exists(Target).Should().BeTrue();
        using var input = new MemoryStream(original);
        var project = ArchiveProjectSnapshot.Read(input, Source, cancellationToken: Token);
        var entries = JsonNode.Parse(File.ReadAllText(Manifest))!["Entries"]!.AsArray();
        var index = 0;
        void Verify(ArchiveProjectEntry entry)
        {
            var metadata = entries[index++]!;
            metadata["Path"]!.AsArray().Select(x => x!.GetValue<string>()).Should().Equal(entry.Identity.Segments);
            metadata["Name"]!.GetValue<string>().Should().Be(entry.Name);
            metadata["Category"]!.GetValue<byte>().Should().Be(entry.Source.Category);
            metadata["Flags"]!.GetValue<uint>().Should().Be(entry.Source.Flags);
            metadata["Revision"]!.GetValue<ulong>().Should().Be(entry.Source.Revision);
            var path = Path.Combine([Target, .. entry.Identity.Segments]);
            if (entry is ArchiveProjectFolder folder)
            {
                Directory.Exists(path).Should().BeTrue();
                foreach (var child in folder.Children) Verify(child);
            }
            else
            {
                var file = (ArchiveProjectDocument)entry;
                File.ReadAllBytes(path).Should().Equal(file.File.Content.ToArray());
                metadata["Revisions"]!.AsArray().Select(x => x!.GetValue<uint>()).Should().Equal(file.File.Revisions);
            }
        }
        Verify(project.Root);
        index.Should().Be(project.EntryCount);
        File.ReadAllBytes(Source).Should().Equal(original);
        File.ReadAllText(Manifest).Should().NotContain(directory);
    }

    [Fact]
    public void Opaque_binary_and_mixed_text_bytes_survive_without_decoding()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0, 0xFF, 0x80, 13, 10, 10, 13];
        Save(Folder("Resources", FileNode("payload.bin", bytes)));
        Sync();
        File.ReadAllBytes(Path.Combine(Target, "Resources", "payload.bin")).Should().Equal(bytes);
    }

    [Fact]
    public void No_op_preserves_timestamps_and_second_project_is_independent()
    {
        Save(FileNode("main.twin", "one"), Folder("empty"), FileNode(".meta", "state"));
        Sync();
        var manifest = File.ReadAllBytes(Manifest);
        var oldTime = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        var member = Path.Combine(Target, "main.twin");
        File.SetLastWriteTimeUtc(member, oldTime);
        File.SetLastWriteTimeUtc(Manifest, oldTime);
        Sync().Changed.Should().BeFalse();
        File.GetLastWriteTimeUtc(member).Should().Be(oldTime);
        File.GetLastWriteTimeUtc(Manifest).Should().Be(oldTime);
        File.ReadAllText(Path.Combine(Target, ".meta")).Should().Be("state");
        var second = Path.Combine(directory, "second.twinproj");
        File.Copy(Source, second);
        var other = new ArchiveSourceFolder().Synchronize(second, Token);
        other.Directory.Should().NotBe(Target);
        File.ReadAllBytes(Manifest).Should().Equal(manifest);
    }

    [Fact]
    public void Refresh_handles_archive_additions_and_deletions_and_retains_previous_bytes()
    {
        Save(FileNode("old.twin", "old"));
        Sync();
        var sibling = Path.Combine(directory, ".hexide", "unrelated");
        File.WriteAllText(sibling, "keep");
        Directory.CreateDirectory(Path.Combine(directory, ".git"));
        File.WriteAllText(Path.Combine(directory, ".git", "sentinel"), "git");
        Save(Folder("Sources", FileNode("new.twin", "new")));
        Sync().Changed.Should().BeTrue();
        File.Exists(Path.Combine(Target, "old.twin")).Should().BeFalse();
        File.ReadAllText(Path.Combine(Target, "Sources", "new.twin")).Should().Be("new");
        var backup = Directory.GetFiles(Control, "old.twin", SearchOption.AllDirectories).Should().ContainSingle().Which;
        File.ReadAllText(backup).Should().Be("old");
        File.ReadAllText(sibling).Should().Be("keep");
        File.ReadAllText(Path.Combine(directory, ".git", "sentinel")).Should().Be("git");
        File.Exists(Path.Combine(Control, "pending.json")).Should().BeFalse();
    }

    [Theory]
    [InlineData("edit")]
    [InlineData("add")]
    [InlineData("delete")]
    [InlineData("rename")]
    [InlineData("empty-folder")]
    [InlineData("manifest")]
    [InlineData("missing-manifest")]
    public void Local_divergence_stops_refresh_without_changing_user_data(string change)
    {
        Save(FileNode("main.twin", "old"));
        Sync();
        var member = Path.Combine(Target, "main.twin");
        switch (change)
        {
            case "edit": File.WriteAllText(member, "edited"); break;
            case "add": File.WriteAllText(Path.Combine(Target, "new"), "added"); break;
            case "delete": File.Delete(member); break;
            case "rename": File.Move(member, member + ".renamed"); break;
            case "empty-folder": Directory.CreateDirectory(Path.Combine(Target, "new")); break;
            case "manifest": File.AppendAllText(Manifest, " "); break;
            case "missing-manifest": File.Delete(Manifest); break;
        }
        var before = Inventory(Target);
        var metadata = File.Exists(Manifest) ? File.ReadAllBytes(Manifest) : null;
        Save(FileNode("main.twin", "updated archive"));
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        Inventory(Target).Should().Equal(before);
        if (metadata is null)
            (File.Exists(Manifest) ? File.ReadAllBytes(Manifest) : null).Should().BeNull();
        else
            (File.Exists(Manifest) ? File.ReadAllBytes(Manifest) : null).Should().Equal(metadata);
    }

    [Theory]
    [InlineData("staged", false)]
    [InlineData("journaled", false)]
    [InlineData("old-moved", false)]
    [InlineData("new-moved", false)]
    [InlineData("manifest-written", false)]
    [InlineData("staged", true)]
    [InlineData("journaled", true)]
    [InlineData("old-moved", true)]
    [InlineData("new-moved", true)]
    [InlineData("manifest-written", true)]
    public void Interrupted_creation_and_refresh_recover_before_next_sync(string checkpoint, bool existing)
    {
        if (existing) { Save(FileNode("old", "original")); Sync(); }
        Save(FileNode("new", "replacement"));
        var sync = new ArchiveSourceFolder { Checkpoint = point => { if (point == checkpoint) throw new InjectedFailure(); } };
        FluentActions.Invoking(() => sync.Synchronize(Source, Token)).Should().ThrowExactly<InjectedFailure>();
        Sync();
        File.ReadAllText(Path.Combine(Target, "new")).Should().Be("replacement");
        File.Exists(Path.Combine(Target, "old")).Should().BeFalse();
        File.Exists(Path.Combine(Control, "pending.json")).Should().BeFalse();
        Sync().Changed.Should().BeFalse();
        if (existing)
            Directory.GetFiles(Control, "old", SearchOption.AllDirectories).Should().Contain(p => File.ReadAllText(p) == "original");
    }

    [Fact]
    public void Late_edit_to_moved_old_tree_is_retained_and_blocks_commit_and_recovery()
    {
        Save(FileNode("main", "old")); Sync();
        Save(FileNode("main", "new"));
        var sync = new ArchiveSourceFolder
        {
            Checkpoint = point =>
            {
                if (point == "old-moved")
                    File.WriteAllText(Directory.GetFiles(Control, "main", SearchOption.AllDirectories).Should().ContainSingle(p => Path.GetFileName(Path.GetDirectoryName(p)) == "previous").Which, "late edit");
            }
        };
        FluentActions.Invoking(() => sync.Synchronize(Source, Token)).Should().ThrowExactly<IOException>();
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("late edit");
    }

    [Fact]
    public void Source_change_and_cancellation_before_commit_leave_previous_tree_intact()
    {
        Save(FileNode("main", "old")); Sync();
        Save(FileNode("main", "new"));
        var sync = new ArchiveSourceFolder { Checkpoint = _ => Save(FileNode("main", "changed again")) };
        FluentActions.Invoking(() => sync.Synchronize(Source, Token)).Should().ThrowExactly<IOException>();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("old");
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        sync.Checkpoint = _ => cancellation.Cancel();
        FluentActions.Invoking(() => sync.Synchronize(Source, cancellation.Token)).Should().ThrowExactly<OperationCanceledException>();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("old");
        File.Exists(Path.Combine(Control, "pending.json")).Should().BeFalse();
    }

    [Fact]
    public void Cloned_tree_is_adopted_only_when_it_matches_the_current_archive()
    {
        Save(FileNode("main", "old")); Sync();
        Directory.Delete(Control, recursive: true);
        Sync().Changed.Should().BeFalse();
        Directory.Delete(Control, recursive: true);
        Save(FileNode("main", "new"));
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("old");
        Directory.Exists(Control).Should().BeFalse();
    }

    [Theory]
    [InlineData("../outside")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("CON.txt")]
    [InlineData("COM¹")]
    [InlineData("dot.")]
    [InlineData("space ")]
    [InlineData("colon:name")]
    [InlineData(".git")]
    [InlineData(".GITATTRIBUTES")]
    [InlineData(".gitignore")]
    public void Unrepresentable_names_are_rejected_before_creating_any_output(string name)
    {
        Save(FileNode(name, "data"));
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        Directory.Exists(Path.Combine(directory, ".hexide")).Should().BeFalse();
    }

    [Theory]
    [InlineData("Main", "main")]
    [InlineData("é", "e\u0301")]
    public void Portable_collisions_are_rejected_on_every_host(string one, string two)
    {
        Save(FileNode(one, "1"), FileNode(two, "2"));
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        Directory.Exists(Path.Combine(directory, ".hexide")).Should().BeFalse();
    }

    [Fact]
    public void Unowned_control_directory_and_unrelated_destination_are_refused()
    {
        Save(FileNode("main", "data"));
        Directory.CreateDirectory(Control);
        File.WriteAllText(Path.Combine(Control, "keep"), "original");
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        Directory.GetFiles(Control).Select(Path.GetFileName).Should().Equal(new[] { "keep" });
        Directory.Delete(Control, recursive: true);
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "keep"), "original");
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        Directory.Exists(Control).Should().BeFalse();
    }

    [Fact]
    public void Concurrent_sync_cannot_acquire_the_active_lease()
    {
        Save(FileNode("main", "data"));
        var sync = new ArchiveSourceFolder { Checkpoint = point => { if (point == "staged") FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>(); } };
        sync.Synchronize(Source, Token);
        Sync().Changed.Should().BeFalse();
    }

    [Fact]
    public void Unix_links_in_members_or_lock_are_refused_without_touching_targets()
    {
        // Windows symlink creation needs privileges; Linux CI exercises links unconditionally.
        if (OperatingSystem.IsWindows()) return;
        Save(FileNode("main", "data")); Sync();
        var outside = Path.Combine(directory, "outside");
        File.WriteAllText(outside, "data");
        var member = Path.Combine(Target, "main");
        File.Delete(member);
        File.CreateSymbolicLink(member, outside);
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        File.Delete(member);
        File.WriteAllText(member, "data");
        var lease = Path.Combine(Control, "lock");
        File.Delete(lease);
        File.CreateSymbolicLink(lease, outside);
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        File.ReadAllText(outside).Should().Be("data");
    }

    [Fact]
    public void Unix_project_directory_alias_uses_the_same_physical_projection()
    {
        if (OperatingSystem.IsWindows()) return;
        Save(FileNode("main", "data"));
        var first = Sync();
        var alias = Path.Combine(directory, "alias");
        Directory.CreateSymbolicLink(alias, directory);
        try
        {
            var throughAlias = new ArchiveSourceFolder().Synchronize(Path.Combine(alias, "project.twinproj"), Token);
            throughAlias.Directory.Should().Be(first.Directory);
            throughAlias.Changed.Should().BeFalse();
            // Resolving parent aliases must not permit a link as the archive file itself.
            var linkedSource = Path.Combine(directory, "linked.twinproj");
            File.CreateSymbolicLink(linkedSource, Source);
            FluentActions.Invoking(() => new ArchiveSourceFolder().Synchronize(linkedSource, Token)).Should().ThrowExactly<IOException>();
        }
        finally { Directory.Delete(alias); }
    }

    [Fact]
    public void Unix_directory_link_target_with_linked_ancestors_is_fully_resolved()
    {
        if (OperatingSystem.IsWindows()) return;
        Save(FileNode("main", "data"));
        var nested = Path.Combine(directory, "nested");
        Directory.CreateDirectory(nested);
        File.Copy(Source, Path.Combine(nested, "project.twinproj"));
        var first = new ArchiveSourceFolder().Synchronize(Path.Combine(nested, "project.twinproj"), Token);
        var parentAlias = Path.Combine(directory, "parent-alias");
        var alias = Path.Combine(directory, "nested-alias");
        Directory.CreateSymbolicLink(parentAlias, directory);
        Directory.CreateSymbolicLink(alias, Path.Combine(parentAlias, "nested"));
        try
        {
            var result = new ArchiveSourceFolder().Synchronize(Path.Combine(alias, "project.twinproj"), Token);
            result.Directory.Should().Be(first.Directory);
            result.Changed.Should().BeFalse();
        }
        finally
        {
            Directory.Delete(alias);
            Directory.Delete(parentAlias);
        }
    }

    [Fact]
    public void Git_tracks_projection_and_manifest_but_ignores_local_recovery_state()
    {
        Save(FileNode("main.twin", "line one\r\nline two\n")); Sync();
        Git("init", "--quiet");
        var files = Git("ls-files", "--others", "--exclude-standard");
        files.Should().Contain(".hexide/.twinproj/project.twinproj/main.twin");
        files.Should().Contain(".hexide/.twinproj/.manifests/project.twinproj.json");
        files.Should().NotContain(".twinproj-sync");
        Git("-c", "core.autocrlf=true", "add", ".");
        Git("show", ":.hexide/.twinproj/project.twinproj/main.twin").Should().Be("line one\r\nline two\n");
    }

    [Fact]
    public void Malformed_baseline_is_a_controlled_error_and_does_not_change_the_tree()
    {
        Save(FileNode("main", "data")); Sync();
        File.WriteAllText(Path.Combine(Control, "baseline.json"), "{ broken");
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("data");
    }

    [Fact]
    public void Changed_projection_after_interrupted_install_is_never_overwritten()
    {
        Save(FileNode("main", "data"));
        var sync = new ArchiveSourceFolder { Checkpoint = point => { if (point == "new-moved") throw new InjectedFailure(); } };
        FluentActions.Invoking(() => sync.Synchronize(Source, Token)).Should().ThrowExactly<InjectedFailure>();
        File.WriteAllText(Path.Combine(Target, "main"), "user edit");
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("user edit");
        File.Exists(Path.Combine(Control, "pending.json")).Should().BeTrue();
    }

    [Theory]
    [InlineData("null-entry")]
    [InlineData("file-root")]
    [InlineData("missing-parent")]
    [InlineData("invalid-hash")]
    public void Invalid_manifest_shapes_cannot_drive_filesystem_operations(string shape)
    {
        Save(FileNode("main", "data")); Sync();
        var json = JsonNode.Parse(File.ReadAllText(Manifest))!;
        var entries = json["Entries"]!.AsArray();
        switch (shape)
        {
            case "null-entry": entries[0] = null; break;
            case "file-root": entries[0]!["Directory"] = false; break;
            case "missing-parent": entries[1]!["Path"] = new JsonArray("missing", "main"); break;
            case "invalid-hash": entries[1]!["Hash"] = "invalid"; break;
        }
        File.WriteAllText(Manifest, json.ToJsonString());
        File.WriteAllText(Path.Combine(Control, "baseline.json"), json.ToJsonString());
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("data");
    }

    private string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(10_000).Should().BeTrue("Git timed out.");
        (process.ExitCode == 0).Should().BeTrue(error);
        return output;
    }

    private static string[] Inventory(string root) => Directory.GetFileSystemEntries(root, "*", SearchOption.AllDirectories)
        .Order(StringComparer.Ordinal).Select(p => Path.GetRelativePath(root, p) + ":" +
            (Directory.Exists(p) ? "directory" : Convert.ToHexString(File.ReadAllBytes(p)))).ToArray();

    private void Save(params Action<BinaryWriter>[] children)
    {
        using var stream = File.Create(Source);
        using var writer = new BinaryWriter(stream, Encoding.UTF8);
        writer.Write(0xEA0BA51Cu);
        Header(writer, 1, "Project");
        writer.Write((uint)children.Length);
        foreach (var child in children) child(writer);
    }
    private static Action<BinaryWriter> Folder(string name, params Action<BinaryWriter>[] children) => writer =>
    {
        Header(writer, 2, name); writer.Write((uint)children.Length);
        foreach (var child in children) child(writer);
    };
    private static Action<BinaryWriter> FileNode(string name, string text) => FileNode(name, Encoding.UTF8.GetBytes(text));
    private static Action<BinaryWriter> FileNode(string name, byte[] bytes) => writer =>
    {
        Header(writer, 1, name);
        writer.Write((uint)bytes.Length); writer.Write(bytes); writer.Write(1u); writer.Write(23u);
    };
    private static void Header(BinaryWriter writer, short kind, string name)
    {
        var bytes = Encoding.UTF8.GetBytes(name);
        writer.Write(kind); writer.Write((uint)bytes.Length); writer.Write(bytes);
        writer.Write(ulong.MaxValue); writer.Write(0u); writer.Write((byte)0);
    }
    private sealed class InjectedFailure : Exception;
}
