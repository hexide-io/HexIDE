namespace HexIDE.TwinBasic.Tests;

public sealed class ArchiveProjectStructureTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-structure-" + Guid.NewGuid());
    public ArchiveProjectStructureTests() => Directory.CreateDirectory(directory);

    [Theory]
    [InlineData("05-module-added", "06-module-renamed", "MyModule.twin", "rename", "Helpers")]
    [InlineData("05-module-added", "02-reopened", "MyModule.twin", "delete", null)]
    [InlineData("06-module-renamed", "02-reopened", "Helpers.twin", "delete", null)]
    [InlineData("02-reopened", "07-folder-added", null, "folder", "MyFolder")]
    public void Measured_operation_matches_the_entire_IDE_save(string before, string after, string? target, string operation, string? name)
    {
        // docs/twinbasic-oracle/twinproj-container-v1.md, One change at a time (#5144).
        var snapshot = Create(before);
        var original = File.ReadAllBytes(snapshot.ContainerPath);
        var result = ArchiveProjectStructure.Save(snapshot, Identity(target), Operation(operation), name);
        File.ReadAllBytes(snapshot.ContainerPath).Should().Equal(Fixture(after));
        result.Container.Root.Name.Should().Be(snapshot.Name);
        File.ReadAllBytes(Directory.GetFiles(directory, "*.recovery").Single()).Should().Equal(original);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("CON")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("1start")]
    [InlineData("é")]
    [InlineData("NUL")]
    [InlineData("COM1")]
    [InlineData("LPT9")]
    [InlineData("Form1")]
    [InlineData("form1")]
    [InlineData("long")]
    public void Invalid_module_name_refuses_without_writing(string name)
    {
        Refused("05-module-added", Identity("MyModule.twin"), ArchiveStructureOperation.Rename,
            name == "long" ? new string('a', 256) : name, ArchiveStructureFailure.Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("..")]
    [InlineData("a/b")]
    [InlineData("a\\b")]
    [InlineData("CON")]
    [InlineData(" leading")]
    [InlineData("trailing ")]
    [InlineData("myfolder")]
    public void Invalid_folder_name_refuses_without_writing(string name) =>
        Refused("07-folder-added", Identity(), ArchiveStructureOperation.NewFolder, name, ArchiveStructureFailure.Name);

    [Theory]
    [InlineData("folder", "rename")]
    [InlineData("folder", "delete")]
    [InlineData("Form1.twin", "rename")]
    [InlineData("Form1.twin", "delete")]
    [InlineData("Form1.tbform", "rename")]
    [InlineData("Form1.tbform", "delete")]
    [InlineData("settings", "rename")]
    [InlineData("settings", "delete")]
    [InlineData("settings", "folder")]
    [InlineData("sources", "module")]
    [InlineData("sources", "form")]
    public void Unmeasured_operations_refuse_without_writing(string target, string operation)
    {
        var identity = target == "settings" ? new ArchiveEntryIdentity(PathFor(), ["Settings"])
            : Identity(target == "folder" ? "MyFolder" : target == "sources" ? null : target);
        Refused("07-folder-added", identity, Operation(operation), "NewName", ArchiveStructureFailure.Unmeasured);
    }

    [Theory]
    [InlineData("rename")]
    [InlineData("delete")]
    [InlineData("folder")]
    public void Disk_change_refuses_each_operation_without_writing(string operation)
    {
        var snapshot = Create("05-module-added");
        File.AppendAllText(snapshot.ContainerPath, "external");
        var before = File.ReadAllBytes(snapshot.ContainerPath);
        Action save = () => ArchiveProjectStructure.Save(snapshot, Identity(operation == "folder" ? null : "MyModule.twin"), Operation(operation), "NewName");
        save.Should().Throw<ArchiveSaveException>().Where(e => e.Failure == ArchiveSaveFailure.ChangedOnDisk);
        File.ReadAllBytes(snapshot.ContainerPath).Should().Equal(before);
        Directory.GetFiles(directory).Should().ContainSingle();
    }

    [Fact]
    public void Nested_folder_is_first_and_identifier_limit_is_ASCII_bytes()
    {
        var snapshot = Create("07-folder-added");
        var result = ArchiveProjectStructure.Save(snapshot, Identity("MyFolder"), ArchiveStructureOperation.NewFolder, new string('a', 255));
        result.TryGetEntry(new(PathFor(), ["Sources", "MyFolder", new string('a', 255)]), out var entry).Should().BeTrue();
        entry.Should().BeOfType<ArchiveProjectFolder>();
        entry!.Source.Revision.Should().Be(0);
        entry.Source.Flags.Should().Be(0);
        entry.Source.Category.Should().Be(0);
    }

    [Theory]
    [InlineData(250, true)]
    [InlineData(251, false)]
    public void A_renamed_module_name_with_its_extension_is_at_most_255_bytes(int length, bool accepted)
    {
        var snapshot = Create("05-module-added");
        var before = File.ReadAllBytes(snapshot.ContainerPath);
        Action save = () => ArchiveProjectStructure.Save(snapshot, Identity("MyModule.twin"), ArchiveStructureOperation.Rename,
            new string('a', length));
        if (accepted)
        {
            save.Should().NotThrow();
            return;
        }
        save.Should().Throw<ArchiveStructureException>().Where(e => e.Failure == ArchiveStructureFailure.Name);
        File.ReadAllBytes(snapshot.ContainerPath).Should().Equal(before);
    }

    [Fact]
    public void A_non_project_archive_stays_read_only()
    {
        var path = Path.Combine(directory, "archive.twinpack");
        var before = Fixture("05-module-added");
        File.WriteAllBytes(path, before);
        using var input = File.OpenRead(path);
        var snapshot = ArchiveProjectSnapshot.Read(input, path, cancellationToken: TestContext.Current.CancellationToken);
        foreach (var operation in new[] { ArchiveStructureOperation.Rename, ArchiveStructureOperation.Delete, ArchiveStructureOperation.NewFolder })
        {
            var identity = new ArchiveEntryIdentity(path, operation == ArchiveStructureOperation.NewFolder
                ? ["Sources"] : ["Sources", "MyModule.twin"]);
            ArchiveProjectStructure.CanEdit(snapshot, identity, operation).Should().BeFalse();
            Action save = () => ArchiveProjectStructure.Save(snapshot, identity, operation, "NewName");
            save.Should().Throw<ArchiveStructureException>().Where(e => e.Failure == ArchiveStructureFailure.Unmeasured);
        }
        File.ReadAllBytes(path).Should().Equal(before);
        Directory.GetFiles(directory).Should().ContainSingle();
    }

    private void Refused(string fixture, ArchiveEntryIdentity identity, ArchiveStructureOperation operation, string? name, ArchiveStructureFailure reason)
    {
        var snapshot = Create(fixture);
        var before = File.ReadAllBytes(snapshot.ContainerPath);
        Action save = () => ArchiveProjectStructure.Save(snapshot, identity, operation, name);
        var refusal = save.Should().Throw<ArchiveStructureException>().Where(e => e.Failure == reason).Which;
        if (reason == ArchiveStructureFailure.Unmeasured) refusal.Message.Should().Contain("not been measured");
        File.ReadAllBytes(snapshot.ContainerPath).Should().Equal(before);
        Directory.GetFiles(directory).Should().ContainSingle();
    }
    private static ArchiveStructureOperation Operation(string operation) => operation switch
    {
        "rename" => ArchiveStructureOperation.Rename, "delete" => ArchiveStructureOperation.Delete,
        "folder" => ArchiveStructureOperation.NewFolder, "module" => ArchiveStructureOperation.AddModule,
        _ => ArchiveStructureOperation.AddForm
    };
    private string PathFor() => Path.Combine(directory, "project.twinproj");
    private ArchiveEntryIdentity Identity(string? name = null) => new(PathFor(), name == null ? ["Sources"] : ["Sources", name]);
    private static byte[] Fixture(string name) => File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tb1044-" + name + ".twinproj"));
    private ArchiveProjectSnapshot Create(string fixture)
    {
        File.WriteAllBytes(PathFor(), Fixture(fixture));
        using var input = File.OpenRead(PathFor());
        return ArchiveProjectSnapshot.Read(input, PathFor());
    }
    public void Dispose() => Directory.Delete(directory, true);
}
