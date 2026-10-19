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
        Assert.True(result.Changed);
        Assert.Equal(Target, result.Directory);
        using var input = new MemoryStream(original);
        var project = ArchiveProjectSnapshot.Read(input, Source, cancellationToken: Token);
        var entries = JsonNode.Parse(File.ReadAllText(Manifest))!["Entries"]!.AsArray();
        var index = 0;
        void Verify(ArchiveProjectEntry entry)
        {
            var metadata = entries[index++]!;
            Assert.Equal(entry.Identity.Segments, metadata["Path"]!.AsArray().Select(x => x!.GetValue<string>()));
            Assert.Equal(entry.Name, metadata["Name"]!.GetValue<string>());
            Assert.Equal(entry.Source.Category, metadata["Category"]!.GetValue<byte>());
            Assert.Equal(entry.Source.Flags, metadata["Flags"]!.GetValue<uint>());
            Assert.Equal(entry.Source.Revision, metadata["Revision"]!.GetValue<ulong>());
            var path = Path.Combine([Target, .. entry.Identity.Segments]);
            if (entry is ArchiveProjectFolder folder)
            {
                Assert.True(Directory.Exists(path));
                foreach (var child in folder.Children) Verify(child);
            }
            else
            {
                var file = (ArchiveProjectDocument)entry;
                Assert.Equal(file.File.Content.ToArray(), File.ReadAllBytes(path));
                Assert.Equal(file.File.Revisions, metadata["Revisions"]!.AsArray().Select(x => x!.GetValue<uint>()));
            }
        }
        Verify(project.Root);
        Assert.Equal(project.EntryCount, index);
        Assert.Equal(original, File.ReadAllBytes(Source));
        Assert.DoesNotContain(directory, File.ReadAllText(Manifest));
    }

    [Fact]
    public void Opaque_binary_and_mixed_text_bytes_survive_without_decoding()
    {
        byte[] bytes = [0xEF, 0xBB, 0xBF, 0, 0xFF, 0x80, 13, 10, 10, 13];
        Save(Folder("Resources", FileNode("payload.bin", bytes)));
        Sync();
        Assert.Equal(bytes, File.ReadAllBytes(Path.Combine(Target, "Resources", "payload.bin")));
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
        Assert.False(Sync().Changed);
        Assert.Equal(oldTime, File.GetLastWriteTimeUtc(member));
        Assert.Equal(oldTime, File.GetLastWriteTimeUtc(Manifest));
        Assert.Equal("state", File.ReadAllText(Path.Combine(Target, ".meta")));
        var second = Path.Combine(directory, "second.twinproj");
        File.Copy(Source, second);
        var other = new ArchiveSourceFolder().Synchronize(second, Token);
        Assert.NotEqual(Target, other.Directory);
        Assert.Equal(manifest, File.ReadAllBytes(Manifest));
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
        Assert.True(Sync().Changed);
        Assert.False(File.Exists(Path.Combine(Target, "old.twin")));
        Assert.Equal("new", File.ReadAllText(Path.Combine(Target, "Sources", "new.twin")));
        var backup = Assert.Single(Directory.GetFiles(Control, "old.twin", SearchOption.AllDirectories));
        Assert.Equal("old", File.ReadAllText(backup));
        Assert.Equal("keep", File.ReadAllText(sibling));
        Assert.Equal("git", File.ReadAllText(Path.Combine(directory, ".git", "sentinel")));
        Assert.False(File.Exists(Path.Combine(Control, "pending.json")));
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
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal(before, Inventory(Target));
        Assert.Equal(metadata, File.Exists(Manifest) ? File.ReadAllBytes(Manifest) : null);
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
        Assert.Throws<InjectedFailure>(() => sync.Synchronize(Source, Token));
        Sync();
        Assert.Equal("replacement", File.ReadAllText(Path.Combine(Target, "new")));
        Assert.False(File.Exists(Path.Combine(Target, "old")));
        Assert.False(File.Exists(Path.Combine(Control, "pending.json")));
        Assert.False(Sync().Changed);
        if (existing)
            Assert.Contains(Directory.GetFiles(Control, "old", SearchOption.AllDirectories), p => File.ReadAllText(p) == "original");
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
                    File.WriteAllText(Assert.Single(Directory.GetFiles(Control, "main", SearchOption.AllDirectories),
                        p => Path.GetFileName(Path.GetDirectoryName(p)) == "previous"), "late edit");
            }
        };
        Assert.Throws<IOException>(() => sync.Synchronize(Source, Token));
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal("late edit", File.ReadAllText(Path.Combine(Target, "main")));
    }

    [Fact]
    public void Source_change_and_cancellation_before_commit_leave_previous_tree_intact()
    {
        Save(FileNode("main", "old")); Sync();
        Save(FileNode("main", "new"));
        var sync = new ArchiveSourceFolder { Checkpoint = _ => Save(FileNode("main", "changed again")) };
        Assert.Throws<IOException>(() => sync.Synchronize(Source, Token));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Target, "main")));
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        sync.Checkpoint = _ => cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => sync.Synchronize(Source, cancellation.Token));
        Assert.Equal("old", File.ReadAllText(Path.Combine(Target, "main")));
        Assert.False(File.Exists(Path.Combine(Control, "pending.json")));
    }

    [Fact]
    public void Cloned_tree_is_adopted_only_when_it_matches_the_current_archive()
    {
        Save(FileNode("main", "old")); Sync();
        Directory.Delete(Control, recursive: true);
        Assert.False(Sync().Changed);
        Directory.Delete(Control, recursive: true);
        Save(FileNode("main", "new"));
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal("old", File.ReadAllText(Path.Combine(Target, "main")));
        Assert.False(Directory.Exists(Control));
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
        Assert.Throws<IOException>(() => Sync());
        Assert.False(Directory.Exists(Path.Combine(directory, ".hexide")));
    }

    [Theory]
    [InlineData("Main", "main")]
    [InlineData("é", "e\u0301")]
    public void Portable_collisions_are_rejected_on_every_host(string one, string two)
    {
        Save(FileNode(one, "1"), FileNode(two, "2"));
        Assert.Throws<IOException>(() => Sync());
        Assert.False(Directory.Exists(Path.Combine(directory, ".hexide")));
    }

    [Fact]
    public void Unowned_control_directory_and_unrelated_destination_are_refused()
    {
        Save(FileNode("main", "data"));
        Directory.CreateDirectory(Control);
        File.WriteAllText(Path.Combine(Control, "keep"), "original");
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal(new[] { "keep" }, Directory.GetFiles(Control).Select(Path.GetFileName));
        Directory.Delete(Control, recursive: true);
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "keep"), "original");
        Assert.Throws<IOException>(() => Sync());
        Assert.False(Directory.Exists(Control));
    }

    [Fact]
    public void Concurrent_sync_cannot_acquire_the_active_lease()
    {
        Save(FileNode("main", "data"));
        var sync = new ArchiveSourceFolder { Checkpoint = point => { if (point == "staged") Assert.Throws<IOException>(() => Sync()); } };
        sync.Synchronize(Source, Token);
        Assert.False(Sync().Changed);
    }

    [Fact]
    public void Linux_links_in_members_or_lock_are_refused_without_touching_targets()
    {
        // Windows symlink creation needs privileges; Linux CI exercises links unconditionally.
        if (OperatingSystem.IsWindows()) return;
        Save(FileNode("main", "data")); Sync();
        var outside = Path.Combine(directory, "outside");
        File.WriteAllText(outside, "data");
        var member = Path.Combine(Target, "main");
        File.Delete(member);
        File.CreateSymbolicLink(member, outside);
        Assert.Throws<IOException>(() => Sync());
        File.Delete(member);
        File.WriteAllText(member, "data");
        var lease = Path.Combine(Control, "lock");
        File.Delete(lease);
        File.CreateSymbolicLink(lease, outside);
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal("data", File.ReadAllText(outside));
    }

    [Fact]
    public void Git_tracks_projection_and_manifest_but_ignores_local_recovery_state()
    {
        Save(FileNode("main.twin", "line one\r\nline two\n")); Sync();
        Git("init", "--quiet");
        var files = Git("ls-files", "--others", "--exclude-standard");
        Assert.Contains(".hexide/.twinproj/project.twinproj/main.twin", files);
        Assert.Contains(".hexide/.twinproj/.manifests/project.twinproj.json", files);
        Assert.DoesNotContain(".twinproj-sync", files);
        Git("-c", "core.autocrlf=true", "add", ".");
        Assert.Equal("line one\r\nline two\n", Git("show", ":.hexide/.twinproj/project.twinproj/main.twin"));
    }

    [Fact]
    public void Malformed_baseline_is_a_controlled_error_and_does_not_change_the_tree()
    {
        Save(FileNode("main", "data")); Sync();
        File.WriteAllText(Path.Combine(Control, "baseline.json"), "{ broken");
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal("data", File.ReadAllText(Path.Combine(Target, "main")));
    }

    [Fact]
    public void Changed_projection_after_interrupted_install_is_never_overwritten()
    {
        Save(FileNode("main", "data"));
        var sync = new ArchiveSourceFolder { Checkpoint = point => { if (point == "new-moved") throw new InjectedFailure(); } };
        Assert.Throws<InjectedFailure>(() => sync.Synchronize(Source, Token));
        File.WriteAllText(Path.Combine(Target, "main"), "user edit");
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal("user edit", File.ReadAllText(Path.Combine(Target, "main")));
        Assert.True(File.Exists(Path.Combine(Control, "pending.json")));
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
        Assert.Throws<IOException>(() => Sync());
        Assert.Equal("data", File.ReadAllText(Path.Combine(Target, "main")));
    }

    private string Git(params string[] args)
    {
        var start = new ProcessStartInfo("git") { WorkingDirectory = directory, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in args) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        var output = process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        Assert.True(process.WaitForExit(10_000), "Git timed out.");
        Assert.True(process.ExitCode == 0, error);
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
