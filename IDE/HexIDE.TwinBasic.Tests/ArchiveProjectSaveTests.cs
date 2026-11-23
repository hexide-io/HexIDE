using System.Collections.Immutable;
using System.Text;

namespace HexIDE.TwinBasic.Tests;

public sealed class ArchiveProjectSaveTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-save-" + Guid.NewGuid());

    public ArchiveProjectSaveTests() => Directory.CreateDirectory(directory);

    [Fact]
    public void Measured_edit_is_identical_to_the_IDE_save_and_recovery_keeps_the_original()
    {
        // docs/twinbasic-oracle/a-container-hexide-wrote.md, beta 1044 (#5143).
        var path = Path.Combine(directory, "project.twinproj");
        var original = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tb1044-02-reopened.twinproj"));
        File.WriteAllBytes(path, original);
        var snapshot = Read(path);
        var form = Documents(snapshot.Root).Single(d => d.Name == "Form1.twin");
        var text = form.ReadText().Text!;
        text.Should().Contain("[Description(\"\")]");
        var saved = ArchiveProjectSave.Save(snapshot, new Dictionary<ArchiveEntryIdentity, string>
        { [form.Identity] = text.Replace("[Description(\"\")]", "[Description(\"x\")]", StringComparison.Ordinal) });
        File.ReadAllBytes(path).Should().Equal(File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "tb1044-04-one-character.twinproj")));
        File.ReadAllBytes(Directory.GetFiles(directory, "*.recovery").Single()).Should().Equal(original);
        saved.OriginalBytes.Should().Equal(File.ReadAllBytes(path));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void External_change_is_refused_without_touching_the_file(bool sameLength)
    {
        var snapshot = Create();
        if (sameLength)
        {
            var changed = File.ReadAllBytes(snapshot.ContainerPath);
            changed[0] ^= 1;
            File.WriteAllBytes(snapshot.ContainerPath, changed);
        }
        else File.AppendAllText(snapshot.ContainerPath, "external change");
        var before = File.ReadAllBytes(snapshot.ContainerPath);
        var document = Documents(snapshot.Root).Single();
        Action save = () => ArchiveProjectSave.Save(snapshot, new Dictionary<ArchiveEntryIdentity, string> { [document.Identity] = "edited" });
        save.Should().Throw<ArchiveSaveException>().Where(e => e.Failure == ArchiveSaveFailure.ChangedOnDisk)
            .WithMessage("*" + snapshot.ContainerPath + "*");
        File.ReadAllBytes(snapshot.ContainerPath).Should().Equal(before);
        Directory.GetFiles(directory).Should().ContainSingle();
    }

    [Theory]
    [InlineData("surrogate")]
    [InlineData("leading-bom")]
    [InlineData("overflow")]
    [InlineData("too-large")]
    public void Unwritable_container_is_refused_before_touching_disk(string reason)
    {
        var snapshot = Create(revision: reason == "overflow" ? ulong.MaxValue : 5);
        var before = File.ReadAllBytes(snapshot.ContainerPath);
        var document = Documents(snapshot.Root).Single();
        var text = reason == "surrogate" ? "\uD800" : reason == "leading-bom" ? "\uFEFFedit" : reason == "too-large" ? new string('x', 64 * 1024 * 1024) : "edit";
        Action save = () => ArchiveProjectSave.Save(snapshot, new Dictionary<ArchiveEntryIdentity, string> { [document.Identity] = text });
        save.Should().Throw<ArchiveSaveException>().Where(e => e.Failure == ArchiveSaveFailure.CannotRoundTrip);
        File.ReadAllBytes(snapshot.ContainerPath).Should().Equal(before);
        Directory.GetFiles(directory).Should().ContainSingle();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Saves_keep_BOM_metadata_and_unchanged_entries_and_use_the_new_baseline(bool bom)
    {
        var snapshot = Create(bom);
        var document = Documents(snapshot.Root).Single();
        var saved = ArchiveProjectSave.Save(snapshot, new Dictionary<ArchiveEntryIdentity, string> { [document.Identity] = "edited\r\n" });
        var file = Documents(saved.Root).Single().File;
        file.Content.Should().Equal(bom ? (byte[])[0xEF, 0xBB, 0xBF, .. Encoding.UTF8.GetBytes("edited\r\n")] : Encoding.UTF8.GetBytes("edited\r\n"));
        file.Revision.Should().Be(7);
        file.Flags.Should().Be(document.File.Flags);
        file.Category.Should().Be(document.File.Category);
        file.Revisions.Should().Equal(document.File.Revisions);
        saved.Container.Root.Revision.Should().Be(snapshot.Container.Root.Revision);
        var again = ArchiveProjectSave.Save(saved, new Dictionary<ArchiveEntryIdentity, string> { [document.Identity] = "second" });
        Documents(again.Root).Single().File.Revision.Should().Be(9);
    }

    private ArchiveProjectSnapshot Create(bool bom = false, ulong revision = 5)
    {
        var path = Path.Combine(directory, "project.twinproj");
        var content = Encoding.UTF8.GetBytes("original\r\n");
        var container = new TwinBasicContainer(1, new TwinBasicDirectory("Project", 12, 0, 0,
            [new TwinBasicFile("Main.twin", revision, 8, 4, bom ? [0xEF, 0xBB, 0xBF, .. content] : [.. content], [3, 7])]));
        using (var output = File.Create(path)) TwinBasicContainerWriter.Write(container, output);
        return Read(path);
    }

    private static ArchiveProjectSnapshot Read(string path)
    {
        using var input = File.OpenRead(path);
        return ArchiveProjectSnapshot.Read(input, path);
    }

    private static IEnumerable<ArchiveProjectDocument> Documents(ArchiveProjectFolder folder) =>
        folder.Children.SelectMany(e => e is ArchiveProjectFolder child ? Documents(child) : [(ArchiveProjectDocument)e]);

    public void Dispose() => Directory.Delete(directory, true);
}
