using System.Diagnostics;
using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.IDE;
using System.Text;
using System.Text.Json.Nodes;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.TwinBasic.Tests;

public sealed class ArchiveSourceFolderTests : IDisposable
{
    private readonly string directory = Path.Combine(ResolvedTemp.Root, "hexide-folder-" + Guid.NewGuid().ToString("N"));
    private string Source => Path.Combine(directory, "project.twinproj");
    private string Target => Path.Combine(directory, ".hexide", "project.twinproj");
    private string Manifest => Path.Combine(directory, ".hexide", ".manifests", "project.twinproj.json");
    private string Control => Path.Combine(directory, ".hexide", ".sync", "project.twinproj");
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
    public void A_brand_syncs_into_existing_stock_state_without_a_second_folder()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), Source);
        Directory.CreateDirectory(Path.Combine(directory, ".hexide"));
        var engine = new ArchiveSourceFolder
        {
            Identity = new HexIDE.IDE.DistributionIdentity("rdcore", "RDCore") { ProjectFolder = ".rdcore" }
        };
        engine.Synchronize(Source, Token).Directory.Should().Be(Target);
        Directory.Exists(Target).Should().BeTrue();
        Directory.Exists(Path.Combine(directory, ".rdcore")).Should().BeFalse();
    }

    [Fact]
    public void A_brand_creates_its_own_marked_folder_when_no_state_exists()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), Source);
        var engine = new ArchiveSourceFolder
        {
            Identity = new HexIDE.IDE.DistributionIdentity("rdcore", "RDCore") { ProjectFolder = ".rdcore" }
        };
        var target = Path.Combine(directory, ".rdcore", "project.twinproj");
        engine.Synchronize(Source, Token).Directory.Should().Be(target);
        Directory.Exists(target).Should().BeTrue();
        File.Exists(Path.Combine(directory, ".rdcore", "project-state.json")).Should().BeTrue();
        Directory.Exists(Path.Combine(directory, ".hexide")).Should().BeFalse();
    }

    [Fact]
    public void Synchronization_writes_the_cross_distribution_folder_marker()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), Source);
        Sync();
        var marker = Path.Combine(directory, ".hexide", "project-state.json");
        File.Exists(marker).Should().BeTrue();
        var json = JsonNode.Parse(File.ReadAllText(marker))!;
        json["kind"]!.GetValue<string>().Should().Be("hexide.project-state");
        json["version"]!.GetValue<int>().Should().Be(1);
    }

    [Fact]
    public void Two_marked_folders_refuse_to_write_and_name_both()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), Source);
        Directory.CreateDirectory(Path.Combine(directory, ".hexide"));
        var brand = Path.Combine(directory, ".rdcore");
        Directory.CreateDirectory(brand);
        File.WriteAllText(Path.Combine(brand, "project-state.json"), """{"kind":"hexide.project-state","version":1}""");
        var sync = () => Sync();
        sync.Should().Throw<IOException>().WithMessage("*.hexide*.rdcore*");
        Directory.GetFileSystemEntries(Path.Combine(directory, ".hexide")).Should().BeEmpty();
        Directory.GetFileSystemEntries(brand).Should().HaveCount(1);
    }

    [Fact]
    public void Published_fixture_preserves_every_file_and_all_entry_metadata()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), Source);
        var original = File.ReadAllBytes(Source);
        var result = Sync();
        result.Changed.Should().BeTrue();
        result.Directory.Should().EndWith(Path.Combine(".hexide", "project.twinproj"));
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
        files.Should().Contain(".hexide/project.twinproj/main.twin");
        files.Should().Contain(".hexide/.manifests/project.twinproj.json");
        files.Should().NotContain(".sync");
        Git("-c", "core.autocrlf=true", "add", ".");
        Git("show", ":.hexide/project.twinproj/main.twin").Should().Be("line one\r\nline two\n");
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

    [Fact]
    public void Published_manifest_bytes_match_main_ef65c5d96()
    {
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), Source);
        Sync();
        // Captured from main's ArchiveSourceFolder, before extracting the engine (#5148).
        var manifest = Directory.GetFiles(Path.Combine(directory, ".hexide"), "project.twinproj.json", SearchOption.AllDirectories)
            .Should().ContainSingle().Which;
        File.ReadAllBytes(manifest).Should().Equal(Encoding.UTF8.GetBytes(File.ReadAllText(Path.Combine(AppContext.BaseDirectory,
            "Fixtures", "impexp.source-folder.json")).ReplaceLineEndings(Environment.NewLine)));
    }

    [Fact]
    public void Folder_marker_bytes_match_main_ef65c5d96()
    {
        Save(FileNode("main", "data"));
        Sync();
        File.ReadAllBytes(Path.Combine(directory, ".hexide", "project-state.json"))
            .Should().Equal("{\"kind\":\"hexide.project-state\",\"version\":1}"u8.ToArray());
    }

    [Fact]
    public void Matched_legacy_folder_with_baseline_moves_without_rewriting_manifest()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        var bytes = File.ReadAllBytes(LegacyManifest);
        var time = new DateTime(2020, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        File.SetLastWriteTimeUtc(LegacyManifest, time);
        Sync().Changed.Should().BeFalse();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("data");
        File.ReadAllBytes(Manifest).Should().Equal(bytes);
        File.GetLastWriteTimeUtc(Manifest).Should().Be(time);
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj")).Should().BeFalse();
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj-sync")).Should().BeFalse();
    }

    [Fact]
    public void Matched_legacy_clone_is_adopted_and_moved()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        Directory.Delete(LegacyControl, recursive: true);
        Sync().Changed.Should().BeFalse();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("data");
        File.ReadAllBytes(Path.Combine(Control, "baseline.json")).Should().Equal(File.ReadAllBytes(Manifest));
    }

    [Fact]
    public void Edited_legacy_tree_refuses_without_disk_changes()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        File.WriteAllText(Path.Combine(LegacyTarget, "main"), "edited");
        RefusesWithoutChanges("*file was edited*");
    }

    [Fact]
    public void Changed_legacy_manifest_refuses_without_disk_changes()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        File.AppendAllText(LegacyManifest, " ");
        RefusesWithoutChanges("*tracked manifest was changed*");
    }

    [Fact]
    public void Both_layouts_refuse_without_disk_changes()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        Directory.CreateDirectory(Target);
        File.WriteAllText(Path.Combine(Target, "keep"), "unrelated");
        RefusesWithoutChanges("*Both synchronization layouts exist*");
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
    public void Interrupted_legacy_transaction_recovers_before_moving(string checkpoint, bool existing)
    {
        if (existing) { Save(FileNode("main", "old")); Sync(); }
        Save(FileNode("main", "new"));
        var engine = new ArchiveSourceFolder { Checkpoint = p => { if (p == checkpoint) throw new InjectedFailure(); } };
        FluentActions.Invoking(() => engine.Synchronize(Source, Token)).Should().ThrowExactly<InjectedFailure>();
        PutInLegacy();
        Sync();
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("new");
        File.Exists(Path.Combine(Control, "pending.json")).Should().BeFalse();
        Directory.Exists(LegacyControl).Should().BeFalse();
        if (existing)
            Directory.GetFiles(Control, "main", SearchOption.AllDirectories)
                .Should().Contain(p => File.ReadAllText(p) == "old");
    }

    [Fact]
    public void Moving_one_legacy_project_retains_the_other_until_its_sync()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        var second = Path.Combine(directory, "second.twinproj");
        File.Copy(Source, second);
        new ArchiveSourceFolder().Synchronize(second, Token);
        PutInLegacy("second.twinproj");
        Sync();
        var oldSecond = Path.Combine(directory, ".hexide", ".twinproj", "second.twinproj", "main");
        File.ReadAllText(oldSecond).Should().Be("data");
        new ArchiveSourceFolder().Synchronize(second, Token).Changed.Should().BeFalse();
        File.ReadAllText(Path.Combine(directory, ".hexide", "second.twinproj", "main")).Should().Be("data");
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj")).Should().BeFalse();
    }

    [Fact]
    public void Concurrent_legacy_sync_cannot_acquire_the_active_migration_lease()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        var checkedLease = false;
        var engine = new ArchiveSourceFolder
        {
            Checkpoint = point =>
            {
                if (point != "legacy-verified") return;
                checkedLease = true;
                FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
            }
        };
        engine.Synchronize(Source, Token);
        checkedLease.Should().BeTrue();
        Sync().Changed.Should().BeFalse();
    }

    [Fact]
    public void A_long_portable_project_name_can_move_without_exceeding_the_filename_limit()
    {
        Save(FileNode("main", "data"));
        var name = new string('a', 230) + ".twinproj";
        var source = Path.Combine(directory, name);
        File.Move(Source, source);
        var engine = new ArchiveSourceFolder();
        engine.Synchronize(source, Token);
        PutInLegacy(name);
        engine.Synchronize(source, Token).Changed.Should().BeFalse();
        File.ReadAllText(Path.Combine(directory, ".hexide", name, "main")).Should().Be("data");
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj")).Should().BeFalse();
    }

    [Fact]
    public void Moving_a_directory_with_an_engine_lease_is_refused_on_every_platform()
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        var move = typeof(CompoundSourceFolder).GetMethod("MoveDirectory",
            System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)!
            .CreateDelegate<Action<string, string>>();
        var checkedLease = false;
        var engine = new ArchiveSourceFolder
        {
            Checkpoint = point =>
            {
                if (point != "legacy-verified") return;
                checkedLease = true;
                FluentActions.Invoking(() => move(LegacyControl, LegacyControl + ".moved"))
                    .Should().ThrowExactly<IOException>();
            }
        };
        engine.Synchronize(Source, Token);
        checkedLease.Should().BeTrue();
    }

    [Theory]
    [InlineData("migration-recorded")]
    [InlineData("migration-manifest-directory")]
    [InlineData("migration-state-directory")]
    [InlineData("migration-control-created")]
    [InlineData("migration-owner-written")]
    [InlineData("migration-new-lease")]
    [InlineData("migration-tree-moved")]
    [InlineData("migration-manifest-moved")]
    [InlineData("migration-state-.gitignore")]
    [InlineData("migration-state-baseline.json")]
    [InlineData("migration-state-transaction")]
    [InlineData("migration-state-transaction", 2)]
    [InlineData("migration-old-lock-removed")]
    [InlineData("migration-old-owner-removed")]
    [InlineData("migration-old-control-removed")]
    [InlineData("migration-attributes")]
    [InlineData("migration-cleaned")]
    [InlineData("migration-record-removed")]
    public void Interrupted_migration_finishes_without_changing_projection_bytes(string checkpoint, int occurrence = 1)
    {
        Save(FileNode("main", "previous")); Sync();
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        var tree = Inventory(LegacyTarget);
        var manifest = File.ReadAllBytes(LegacyManifest);
        var baseline = File.ReadAllBytes(Path.Combine(LegacyControl, "baseline.json"));
        var state = Inventory(LegacyControl).Where(p => !p.StartsWith("lock:") && !p.StartsWith("owner:")).ToArray();
        var seen = 0;
        var engine = new ArchiveSourceFolder
        {
            Checkpoint = point =>
            {
                if (point != checkpoint || ++seen != occurrence) return;
                FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>();
                throw new InjectedFailure();
            }
        };
        FluentActions.Invoking(() => engine.Synchronize(Source, Token)).Should().ThrowExactly<InjectedFailure>();
        Sync().Changed.Should().BeFalse();
        Inventory(Target).Should().Equal(tree);
        File.ReadAllBytes(Manifest).Should().Equal(manifest);
        File.ReadAllBytes(Path.Combine(Control, "baseline.json")).Should().Equal(baseline);
        Inventory(Control).Where(p => !p.StartsWith("lock:") && !p.StartsWith("owner:")).Should().Equal(state);
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj")).Should().BeFalse();
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj-sync")).Should().BeFalse();
    }

    [Theory]
    [InlineData("migration-recorded", false)]
    [InlineData("migration-owner-written", false)]
    [InlineData("migration-tree-moved", true)]
    [InlineData("migration-state-baseline.json", true)]
    public void Edited_tree_after_migration_record_refuses_without_disk_changes(string checkpoint, bool moved)
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        var engine = new ArchiveSourceFolder
        {
            Checkpoint = point => { if (point == checkpoint) throw new InjectedFailure(); }
        };
        FluentActions.Invoking(() => engine.Synchronize(Source, Token)).Should().ThrowExactly<InjectedFailure>();
        var edited = Path.Combine(moved ? Target : LegacyTarget, "main");
        File.WriteAllText(edited, "edited after interruption");
        RefusesWithoutChanges("*" + edited + "*");
    }

    [Theory]
    [InlineData("migration-recorded")]
    [InlineData("migration-control-created")]
    [InlineData("migration-owner-written")]
    [InlineData("migration-tree-moved")]
    [InlineData("migration-manifest-moved")]
    [InlineData("migration-cleaned")]
    public void Interrupted_clone_migration_is_verified_and_adopted(string checkpoint)
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        Directory.Delete(LegacyControl, recursive: true);
        var manifest = File.ReadAllBytes(LegacyManifest);
        var engine = new ArchiveSourceFolder
        {
            Checkpoint = point => { if (point == checkpoint) throw new InjectedFailure(); }
        };
        FluentActions.Invoking(() => engine.Synchronize(Source, Token)).Should().ThrowExactly<InjectedFailure>();
        Sync().Changed.Should().BeFalse();
        File.ReadAllBytes(Manifest).Should().Equal(manifest);
        File.ReadAllBytes(Path.Combine(Control, "baseline.json")).Should().Equal(manifest);
        File.ReadAllText(Path.Combine(Target, "main")).Should().Be("data");
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj")).Should().BeFalse();
        Directory.Exists(Path.Combine(directory, ".hexide", ".twinproj-sync")).Should().BeFalse();
    }

    [Theory]
    [InlineData("owner")]
    [InlineData(".gitignore")]
    [InlineData("baseline.json")]
    public void Changed_migration_metadata_refuses_without_disk_changes(string name)
    {
        Save(FileNode("main", "data")); Sync(); PutInLegacy();
        var engine = new ArchiveSourceFolder
        {
            Checkpoint = point => { if (point == "migration-manifest-moved") throw new InjectedFailure(); }
        };
        FluentActions.Invoking(() => engine.Synchronize(Source, Token)).Should().ThrowExactly<InjectedFailure>();
        File.AppendAllText(Path.Combine(LegacyControl, name), "edited");
        RefusesWithoutChanges("*Existing data is retained.*");
    }

    [Fact]
    public void A_second_format_uses_the_same_engine_and_layout()
    {
        var source = Path.Combine(directory, "sample.bundle");
        File.WriteAllText(source, "saved");
        var engine = new CompoundSourceFolder(new ExampleFormat());
        var result = engine.Synchronize(source, Token);
        result.Directory.Should().EndWith(Path.Combine(".hexide", "sample.bundle"));
        File.ReadAllText(Path.Combine(result.Directory, "content", "raw")).Should().Be("saved");
        File.ReadAllText(Path.Combine(directory, ".hexide", ".manifests", "sample.bundle.json"))
            .Should().Contain("example-metadata");
        engine.Synchronize(source, Token).Changed.Should().BeFalse();
        File.WriteAllText(source, "updated");
        engine.Synchronize(source, Token).Changed.Should().BeTrue();
        File.ReadAllText(Path.Combine(result.Directory, "content", "raw")).Should().Be("updated");
    }

    [Theory]
    [InlineData("project-state.json")]
    [InlineData(".manifests")]
    [InlineData(".sync")]
    [InlineData(".gitattributes")]
    [InlineData(".twinproj")]
    [InlineData(".twinproj-sync")]
    [InlineData("PROJECT-STATE.JSON")]
    [InlineData(".MANIFESTS")]
    [InlineData(".SYNC")]
    [InlineData(".GITATTRIBUTES")]
    [InlineData(".TWINPROJ")]
    [InlineData(".TWINPROJ-SYNC")]
    public void Root_control_names_are_refused_before_any_output(string name)
    {
        var source = Path.Combine(directory, name);
        File.WriteAllText(source, "saved");
        var before = Inventory(directory);
        FluentActions.Invoking(() => new CompoundSourceFolder(new ExampleFormat()).Synchronize(source, Token))
            .Should().ThrowExactly<IOException>().WithMessage("*Existing data is retained.*");
        Inventory(directory).Should().Equal(before);
    }

    private sealed class ExampleFormat : ICompoundFolderFormat
    {
        public CompoundFolderSnapshot Read(byte[] bytes, string source, CancellationToken cancellationToken)
        {
            var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
            var entries = new[]
            {
                new { Path = Array.Empty<string>(), Name = "Example", Directory = true, Length = 0, Hash = (string?)null, Extra = "example-metadata" },
                new { Path = new[] { "content" }, Name = "content", Directory = true, Length = 0, Hash = (string?)null, Extra = "folder" },
                new { Path = new[] { "content", "raw" }, Name = "raw", Directory = false, Length = bytes.Length, Hash = (string?)hash, Extra = "file" }
            };
            return new(JsonSerializer.SerializeToUtf8Bytes(new { Format = 1, ContainerHash = hash, Entries = entries }),
                [new([], null), new(["content"], null), new(["content", "raw"], bytes)]);
        }
        public void ValidateManifest(byte[] bytes) { }
    }

    private static readonly DateTime RetentionNow = new(2026, 11, 24, 12, 0, 0, DateTimeKind.Utc);
    private sealed class RetentionClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(RetentionNow);
    }
    private CompoundSourceFolder RetentionEngine() => new(new TwinBasicFolderFormat()) { Clock = new RetentionClock() };

    private string[] RetentionHistory()
    {
        for (var i = 0; i < 12; i++)
        {
            Save(FileNode("main", i.ToString(System.Globalization.CultureInfo.InvariantCulture)));
            Sync();
        }
        var transactions = Directory.GetDirectories(Control).Order(StringComparer.Ordinal).ToArray();
        for (var i = 0; i < transactions.Length; i++)
            File.SetLastWriteTimeUtc(Path.Combine(transactions[i], "new.json"), RetentionNow.AddDays(-30).AddHours(i));
        return transactions;
    }
    private CompoundFolderResult ChangingRetentionSync()
    {
        Save(FileNode("main", "latest"));
        var result = RetentionEngine().Synchronize(Source, Token);
        result.Changed.Should().BeTrue();
        return result;
    }

    public static bool IsWindows => OperatingSystem.IsWindows();

    // A junction needs no privilege on Windows, where a symbolic link does.
    private static void Junction(string link, string target)
    {
        var start = new ProcessStartInfo("cmd.exe") { RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var arg in new[] { "/c", "mklink", "/J", link, target }) start.ArgumentList.Add(arg);
        using var process = Process.Start(start)!;
        process.StandardOutput.ReadToEnd();
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit(10_000).Should().BeTrue("mklink timed out.");
        process.ExitCode.Should().Be(0, error);
        (File.GetAttributes(link) & FileAttributes.ReparsePoint).Should().Be(FileAttributes.ReparsePoint);
    }

    [Fact]
    public void Retention_prunes_only_old_transactions_outside_the_newest_ten()
    {
        var history = RetentionHistory();
        // The changing sync makes thirteen: two expired trees, one recent tree, and ten newer ones.
        for (var i = 2; i < history.Length; i++)
            File.SetLastWriteTimeUtc(Path.Combine(history[i], "new.json"), RetentionNow.AddDays(-6).AddHours(i));
        ChangingRetentionSync();
        history.Take(2).Should().OnlyContain(p => !Directory.Exists(p));
        history.Skip(2).Should().OnlyContain(p => Directory.Exists(p));
    }

    [Fact]
    public void Retention_keeps_old_transactions_within_the_newest_ten()
    {
        var history = RetentionHistory();
        ChangingRetentionSync();
        history.Skip(3).Should().OnlyContain(p => Directory.Exists(p));
    }

    [Theory]
    [InlineData("previous")]
    [InlineData("next")]
    [InlineData("missing-metadata")]
    [InlineData("unreadable-metadata")]
    public void Retention_keeps_external_edits_and_undatable_transactions(string change)
    {
        var history = RetentionHistory();
        var candidate = history.First(p => Directory.Exists(Path.Combine(p, "previous")));
        if (change == "previous") File.AppendAllText(Path.Combine(candidate, "previous", "main"), "edit");
        if (change == "next")
        {
            Directory.CreateDirectory(Path.Combine(candidate, "next"));
            File.WriteAllText(Path.Combine(candidate, "next", "main"), "edit");
        }
        if (change == "missing-metadata") File.Delete(Path.Combine(candidate, "new.json"));
        if (change == "unreadable-metadata") File.WriteAllText(Path.Combine(candidate, "new.json"), "invalid");
        ChangingRetentionSync();
        Directory.Exists(candidate).Should().BeTrue();
    }

    [Fact]
    public void Retention_leaves_non_candidates_untouched()
    {
        var history = RetentionHistory();
        // Old, datable and verifiable, but named as the sync never names a transaction.
        var upper = Path.Combine(Control, Path.GetFileName(history[0]).ToUpperInvariant());
        var hyphens = Path.Combine(Control, Guid.ParseExact(Path.GetFileName(history[1]), "N").ToString("D"));
        Directory.Move(history[0], upper);
        Directory.Move(history[1], hyphens);
        var other = Path.Combine(Control, "unrelated");
        Directory.CreateDirectory(other);
        File.WriteAllText(Path.Combine(other, "keep"), "directory");
        File.WriteAllText(Path.Combine(Control, "loose"), "file");
        ChangingRetentionSync();
        File.ReadAllText(Path.Combine(other, "keep")).Should().Be("directory");
        File.ReadAllText(Path.Combine(Control, "loose")).Should().Be("file");
        Directory.Exists(upper).Should().BeTrue();
        Directory.Exists(hyphens).Should().BeTrue();
    }

    [Fact(Skip = "A symbolic link needs a privilege on Windows; the junction tests cover links there", SkipWhen = nameof(IsWindows))]
    public void Retention_keeps_linked_candidates_without_touching_targets()
    {
        var history = RetentionHistory();
        var outside = Path.Combine(directory, "outside");
        File.WriteAllText(outside, "keep");
        var link = Path.Combine(history[0], "link");
        File.CreateSymbolicLink(link, outside);
        try
        {
            var result = ChangingRetentionSync();
            File.Exists(link).Should().BeTrue();
            File.ReadAllText(outside).Should().Be("keep");
            result.Kept.Should().ContainSingle(k => k.StartsWith(history[0] + ":", StringComparison.Ordinal));
        }
        finally { File.Delete(link); }
    }

    [Theory(Skip = "Junctions are Windows-only", SkipUnless = nameof(IsWindows))]
    [InlineData("inside")]
    [InlineData("candidate")]
    public void Retention_never_deletes_through_a_junction(string where)
    {
        var history = RetentionHistory();
        var outside = Path.Combine(directory, "outside");
        string junction;
        if (where == "inside")
        {
            Directory.CreateDirectory(outside);
            File.WriteAllText(Path.Combine(outside, "keep"), "keep");
            junction = Path.Combine(history[0], "junction");
        }
        else
        {
            // An old, datable transaction moved out of .sync, with a junction left in its place.
            Directory.Move(history[0], outside);
            junction = history[0];
        }
        var before = Inventory(outside);
        Junction(junction, outside);
        try
        {
            var result = ChangingRetentionSync();
            (File.GetAttributes(junction) & FileAttributes.ReparsePoint).Should().Be(FileAttributes.ReparsePoint);
            Inventory(outside).Should().Equal(before);
            result.Kept.Should().ContainSingle(k => k.StartsWith(history[0], StringComparison.Ordinal));
        }
        finally { Directory.Delete(junction); }
    }

    [Fact]
    public void Retention_finishes_a_deletion_that_stopped_part_way()
    {
        RetentionHistory();
        var renamed = Path.Combine(Control, Guid.NewGuid().ToString("N") + ".pruning");
        Directory.CreateDirectory(Path.Combine(renamed, "previous"));
        File.WriteAllText(Path.Combine(renamed, "previous", "main"), "left over");
        ChangingRetentionSync().Kept.Should().BeEmpty();
        Directory.Exists(renamed).Should().BeFalse();
    }

    [Fact(Skip = "Only Windows refuses to rename a folder holding an open file", SkipUnless = nameof(IsWindows))]
    public void Retention_keeps_and_reports_a_folder_it_cannot_remove_and_still_succeeds()
    {
        var history = RetentionHistory();
        var held = Path.Combine(history[0], "new.json");
        var before = Inventory(history[0]);
        CompoundFolderResult result;
        using (new FileStream(held, FileMode.Open, FileAccess.Read, FileShare.Read))
            result = ChangingRetentionSync();
        Inventory(history[0]).Should().Equal(before);
        result.Kept.Should().ContainSingle(k => k.StartsWith(history[0] + ":", StringComparison.Ordinal));
        Directory.Exists(history[1]).Should().BeFalse();
        Directory.Exists(history[2]).Should().BeFalse();
    }

    [Fact]
    public void Retention_does_nothing_while_a_journal_exists()
    {
        var history = RetentionHistory();
        File.WriteAllText(Path.Combine(Control, "pending.json"), "pending");
        RetentionEngine().Prune(Control, "");
        history.Should().OnlyContain(p => Directory.Exists(p));
    }

    [Fact]
    public void Retention_does_nothing_on_an_unchanged_sync()
    {
        var history = RetentionHistory();
        RetentionEngine().Synchronize(Source, Token).Changed.Should().BeFalse();
        history.Should().OnlyContain(p => Directory.Exists(p));
    }

    private string LegacyTarget => Path.Combine(directory, ".hexide", ".twinproj", "project.twinproj");
    private string LegacyManifest => Path.Combine(directory, ".hexide", ".twinproj", ".manifests", "project.twinproj.json");
    private string LegacyControl => Path.Combine(directory, ".hexide", ".twinproj-sync", "project.twinproj");

    private void PutInLegacy(string name = "project.twinproj")
    {
        var root = Path.Combine(directory, ".hexide");
        Directory.CreateDirectory(Path.Combine(root, ".twinproj", ".manifests"));
        Directory.CreateDirectory(Path.Combine(root, ".twinproj-sync"));
        if (Directory.Exists(Path.Combine(root, name)))
            Directory.Move(Path.Combine(root, name), Path.Combine(root, ".twinproj", name));
        if (File.Exists(Path.Combine(root, ".manifests", name + ".json")))
            File.Move(Path.Combine(root, ".manifests", name + ".json"), Path.Combine(root, ".twinproj", ".manifests", name + ".json"));
        if (Directory.Exists(Path.Combine(root, ".sync", name)))
            Directory.Move(Path.Combine(root, ".sync", name), Path.Combine(root, ".twinproj-sync", name));
        File.WriteAllBytes(Path.Combine(root, ".twinproj", ".gitattributes"), "* -text\n"u8.ToArray());
    }

    private void RefusesWithoutChanges(string message)
    {
        var before = Inventory(directory);
        FluentActions.Invoking(() => Sync()).Should().ThrowExactly<IOException>().WithMessage(message);
        Inventory(directory).Should().Equal(before);
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
