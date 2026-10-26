using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Events;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>Source encoding must survive the actual project load/edit/save workflow (#726).</summary>
public class SourceEncodingPersistenceTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-encoding-" + Guid.NewGuid().ToString("N"));

    public SourceEncodingPersistenceTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private readonly List<ProjectDefinition> loaded = new();
    private readonly IProjectManager projectManager = Substitute.For<IProjectManager>();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();
    private readonly IEventBus eventBus = Substitute.For<IEventBus>();
    private readonly IUserSidecarService sidecar = Substitute.For<IUserSidecarService>();
    private readonly IRecentProjectsService recentProjects = Substitute.For<IRecentProjectsService>();

    private ProjectService MakeService(bool userChoosesSave = true)
    {
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            // Stand in for the user: tick every listed file and press Yes (or No).
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm)
            {
                if (userChoosesSave) vm.Yes(); else vm.No();
            }
            return Task.FromResult(true);
        });

        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString("Str.Dialog.SourceEncoding.NothingWritten")
            .Returns("Nothing was written: {0}, in code page {1}, cannot hold {2} ({3}), in {4}.");
        localization.GetString("Str.Dialog.SourceEncoding.ReadByteForByte").Returns(ReadByteForByteText);
        localization.GetString("Str.Dialog.SourceEncoding.ReadByteForByte.Many").Returns(ReadByteForByteManyText);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            eventBus,
            projectManager,
            recentProjects,
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            localization,
            Substitute.For<IHeaderRefresher>());
    }

    private string WriteProject()
    {
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");
        return vbp;
    }

    [Fact]
    public async Task Making_twice_to_the_same_path_replaces_the_archive_with_the_second_build()
    {
        var svc = MakeService();
        await svc.OpenProject(WriteProject());
        var project = loaded.Single();
        var module = project.Modules.Single();
        var exePath = Path.Join(dir, "Program.exe");
        windowManager.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(exePath);
        var standalone = Path.Join(dir, "standalone");
        Directory.CreateDirectory(standalone);
        // Copied, never executed: MadeProgramTests is the one that runs a real runner.
        File.WriteAllText(Path.Join(standalone, ProjectService.StandaloneRunnerName()!), "runtime fixture");

        svc.RuntimeDirectory = dir;
        await svc.MakeProject(project);
        using (var archive = ZipFile.OpenRead(Path.ChangeExtension(exePath, "dll")))
        using (var reader = new StreamReader(archive.GetEntry("Module1.bas")!.Open()))
            reader.ReadToEnd().Should().Contain("Public Sub Original()");

        module.UpdateCode(module.Code.Replace("Original", "SecondBuild"));
        await svc.MakeProject(project);

        using var secondArchive = ZipFile.OpenRead(Path.ChangeExtension(exePath, "dll"));
        using var secondReader = new StreamReader(secondArchive.GetEntry("Module1.bas")!.Open());
        var code = secondReader.ReadToEnd();
        code.Should().Contain("Public Sub SecondBuild()");
        code.Should().NotContain("Public Sub Original()");
        File.ReadAllText(exePath).Should().Be("runtime fixture");
        Directory.GetFiles(dir, "*.tmp").Should().BeEmpty();
    }

    [Theory]
    [InlineData("RelatedDoc=Notes.md")]
    [InlineData("Module=Notes; Notes.md")]
    [InlineData("Class=Notes; Notes.md")]
    public async Task Make_omits_carried_files_and_keeps_the_other_lines_in_order(string itemLine)
    {
        var svc = MakeService();
        var vbp = WriteProject();
        File.WriteAllText(Path.Join(dir, "Notes.md"), "private notes");
        File.WriteAllText(vbp, "Type=Exe\r\nBefore=one\r\n" + itemLine
            + "\r\nBetween=two\r\nModule=Module1; Module1.bas\r\nAfter=three\r\nName=\"Test\"\r\n");
        await svc.OpenProject(vbp);
        var project = loaded.Single();
        var carried = project.RelatedDocuments.Single();
        var original = File.ReadAllBytes(vbp);
        var exePath = Path.Join(dir, "Program.exe");
        windowManager.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(exePath);
        var standalone = Path.Join(dir, "standalone");
        Directory.CreateDirectory(standalone);
        File.WriteAllText(Path.Join(standalone, ProjectService.StandaloneRunnerName()!), "runtime fixture");
        svc.RuntimeDirectory = dir;

        await svc.MakeProject(project);

        using var archive = ZipFile.OpenRead(Path.ChangeExtension(exePath, "dll"));
        archive.Entries.Select(e => e.FullName).Should().BeEquivalentTo("Program.vbp", "Module1.bas");
        using var reader = new StreamReader(archive.GetEntry("Program.vbp")!.Open());
        var text = reader.ReadToEnd().ReplaceLineEndings("\n");
        text.Should().NotContain("Notes").And.NotContain("RelatedDoc=").And.NotContain("..");
        text.Split('\n').Where(line => line.StartsWith("Before=") || line.StartsWith("Between=")
            || line.StartsWith("Module=") || line.StartsWith("After=") || line.StartsWith("Name="))
            .Should().Equal("Before=one", "Between=two", "Module=Module1; Module1.bas", "After=three", "Name=\"Test\"");
        project.RelatedDocuments.Should().ContainSingle().Which.Should().BeSameAs(carried);
        carried.AbsolutePath.Should().Be(Path.Join(dir, "Notes.md"));
        File.ReadAllBytes(vbp).Should().Equal(original);
        await svc.SaveProject(project, false);
        File.ReadAllText(vbp).Should().Contain(itemLine);
    }

    [Fact]
    public async Task Characters_supported_by_the_ANSI_code_page_save_as_ANSI()
    {
        var svc = MakeService();
        await svc.OpenProject(WriteProject());
        var module = loaded.Single().Modules.Single();
        module.TextEncoding = new Vb6TextEncoding(1252);
        module.UpdateCode(module.Code + "' €“”\r\n");

        await svc.SaveModule(module, false);

        var bytes = File.ReadAllBytes(module.AbsolutePath!);
        bytes.TakeLast(7).Should().Equal(new byte[] { 0x27, 0x20, 0x80, 0x93, 0x94, 0x0D, 0x0A });
        module.TextEncoding.CodePage.Should().Be(1252);
    }

    [Fact]
    public async Task An_adopted_module_keeps_its_UTF8_encoding()
    {
        var svc = MakeService();
        var project = new ProjectDefinition(VBProjectType.EXE, "Test");
        var path = Path.Join(dir, "Imported.bas");
        var utf8 = new System.Text.UTF8Encoding(true);
        var bytes = utf8.GetPreamble().Concat(utf8.GetBytes(
            "Attribute VB_Name = \"Imported\"\r\n' café\r\n")).ToArray();
        File.WriteAllBytes(path, bytes);

        await svc.AddExistingModule(project, path, ModuleKind.StandardModule);
        await svc.SaveModule(project.Modules.Single(), false);

        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public async Task Project_and_group_files_keep_their_UTF8_BOMs()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        var utf8 = new System.Text.UTF8Encoding(true);
        var projectText = File.ReadAllText(vbp) + "Description=\"café\"\r\n";
        File.WriteAllBytes(vbp, utf8.GetPreamble().Concat(utf8.GetBytes(projectText)).ToArray());
        var vbg = Path.Join(dir, "Test.vbg");
        var groupText = "VBGROUP 5.0\r\nProject=Test.vbp\r\nStartupProject=Test.vbp\r\nNote=café\r\n";
        File.WriteAllBytes(vbg, utf8.GetPreamble().Concat(utf8.GetBytes(groupText)).ToArray());

        await svc.OpenProject(vbg);
        await svc.SaveAllProjects(false);

        foreach (var path in new[] { vbp, vbg })
        {
            var bytes = File.ReadAllBytes(path);
            bytes.Take(3).Should().Equal(utf8.GetPreamble());
            utf8.GetString(bytes.AsSpan(3)).Should().Contain("café");
        }
    }

    [Fact]
    public async Task A_failed_close_time_save_keeps_the_project_open()
    {
        var svc = MakeService();
        await svc.OpenProject(WriteProject());
        var project = loaded.Single();
        var module = project.Modules.Single();
        module.TextEncoding = new Vb6TextEncoding(1252);
        module.UpdateCode(module.Code + "' こんにちは\r\n");

        Func<Task> close = () => svc.UnloadAllProjects();
        await close.Should().ThrowAsync<OperationCanceledException>();

        projectManager.DidNotReceive().UnloadAllProjects();
        module.Code.Should().Contain("こんにちは");
    }

    [Theory]
    [InlineData("frm", "VB.Form", "Form")]
    [InlineData("ctl", "VB.UserControl", "UserControl")]
    [InlineData("pag", "VB.PropertyPage", "PropertyPage")]
    public async Task Designer_files_keep_their_UTF8_BOM(string extension, string rootType, string itemKey)
    {
        var svc = MakeService();
        var path = Path.Join(dir, "Designer1." + extension);
        var utf8 = new System.Text.UTF8Encoding(true);
        var text = $"VERSION 5.00\r\nBegin {rootType} Designer1\r\n   Caption = \"café\"\r\nEnd\r\nAttribute VB_Name = \"Designer1\"\r\n' café\r\n";
        File.WriteAllBytes(path, utf8.GetPreamble().Concat(utf8.GetBytes(text)).ToArray());
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, $"Type=Exe\r\n{itemKey}=Designer1.{extension}\r\nName=\"Test\"\r\n");
        await svc.OpenProject(vbp);
        var project = loaded.Single();
        (project.Forms.Count + project.Modules.Count).Should().Be(1);

        await svc.SaveProject(project, false);

        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).Should().Equal(utf8.GetPreamble());
        utf8.GetString(bytes.AsSpan(3)).Should().Contain("café");
    }

    /// <summary>
    /// The designer saves the half it shows rather than the module, so this is the save that reads the half's
    /// encoding.
    /// </summary>
    [Theory]
    [InlineData("ctl", "VB.UserControl", "UserControl")]
    [InlineData("pag", "VB.PropertyPage", "PropertyPage")]
    public async Task The_designers_Save_Form_keeps_a_designer_files_UTF8_BOM(string extension, string rootType, string itemKey)
    {
        var svc = MakeService();
        var path = Path.Join(dir, "Designer1." + extension);
        var utf8 = new System.Text.UTF8Encoding(true);
        var text = $"VERSION 5.00\r\nBegin {rootType} Designer1\r\n   Caption = \"café\"\r\nEnd\r\nAttribute VB_Name = \"Designer1\"\r\n' café\r\n";
        File.WriteAllBytes(path, utf8.GetPreamble().Concat(utf8.GetBytes(text)).ToArray());
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, $"Type=Exe\r\n{itemKey}=Designer1.{extension}\r\nName=\"Test\"\r\n");
        await svc.OpenProject(vbp);
        var half = loaded.Single().Modules.Single().FormPart!;

        (await svc.SaveForm(half, false)).Should().BeTrue();

        var bytes = File.ReadAllBytes(path);
        bytes.Take(3).Should().Equal(utf8.GetPreamble());
        utf8.GetString(bytes.AsSpan(3)).Should().Contain("café");
    }

    [Fact]
    public async Task An_encoding_failure_leaves_both_halves_of_a_form_untouched()
    {
        var svc = MakeService();
        var path = Path.Join(dir, "Form1.frm");
        var frx = Path.ChangeExtension(path, ".frx");
        File.WriteAllText(path,
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Icon = \"Form1.frx\":0000\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        File.WriteAllBytes(frx, [4, 0, 0, 0, 1, 2, 3, 4]);
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Form1.frm\r\nName=\"Test\"\r\n");
        await svc.OpenProject(vbp);
        var form = loaded.Single().Forms.Single();
        form.CanSaveFaithfully.Should().BeTrue();
        form.TextEncoding = new Vb6TextEncoding(1252);
        form.UpdateCode(form.Code + "' こんにちは\r\n");
        var original = File.ReadAllBytes(path);
        // Make a resource write detectable even if the serializer would reproduce its loaded bytes.
        byte[] sentinel = [9, 8, 7, 6];
        File.WriteAllBytes(frx, sentinel);

        Func<Task> save = async () => await svc.SaveForm(form, false);
        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.CodePage).Should().Be(("Form1", 1252));
        MessagesShown().Should().ContainSingle(m => m.Contains("Form1") && m.Contains("1252"));

        File.ReadAllBytes(path).Should().Equal(original);
        File.ReadAllBytes(frx).Should().Equal(sentinel);
    }

    [Fact]
    public async Task Save_As_keeps_the_document_encoding()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        var utf8 = new System.Text.UTF8Encoding(true);
        var text = File.ReadAllText(path) + "' café\r\n";
        File.WriteAllBytes(path, utf8.GetPreamble().Concat(utf8.GetBytes(text)).ToArray());
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        var copy = Path.Join(dir, "copy.bas");
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(copy));

        await svc.SaveModule(module, true);

        File.ReadAllBytes(copy).Should().Equal(File.ReadAllBytes(path));
        module.AbsolutePath.Should().Be(copy);
    }

    [Fact]
    public async Task Reload_preserves_UTF8_when_an_external_editor_removes_its_BOM()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        var utf8 = new System.Text.UTF8Encoding(true);
        var text = File.ReadAllText(path) + "' café\r\n";
        File.WriteAllBytes(path, utf8.GetPreamble().Concat(utf8.GetBytes(text)).ToArray());
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        var withoutBom = utf8.GetBytes(text);
        File.WriteAllBytes(path, withoutBom);

        (await svc.ReloadModuleFromDisk(module)).Should().BeTrue();
        // The bytes alone cannot tell: read as Windows-1252 they also save back unchanged, as "cafÃ©".
        module.Code.Should().Contain("' café");
        await svc.SaveModule(module, false);

        File.ReadAllBytes(path).Should().Equal(withoutBom);
    }

    [Fact]
    public async Task Reload_adopts_an_external_UTF8_BOM_before_the_next_save()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        var path = module.AbsolutePath!;
        var utf8 = new System.Text.UTF8Encoding(true);
        var text = File.ReadAllText(path) + "' café\r\n";
        var bytes = utf8.GetPreamble().Concat(utf8.GetBytes(text)).ToArray();
        File.WriteAllBytes(path, bytes);

        (await svc.ReloadModuleFromDisk(module)).Should().BeTrue();
        await svc.SaveModule(module, false);

        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public async Task A_reload_that_finds_a_UserControl_in_another_encoding_reaches_its_designer_half()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndUserControlProject());
        var control = project.Modules.Single();
        var path = control.AbsolutePath!;
        var utf8 = new System.Text.UTF8Encoding(true);
        var bytes = utf8.GetPreamble().Concat(utf8.GetBytes(File.ReadAllText(path))).ToArray();
        File.WriteAllBytes(path, bytes);

        (await svc.ReloadModuleFromDisk(control)).Should().BeTrue();

        control.FormPart!.TextEncoding.Should().Be(Vb6TextEncoding.Utf8WithBom);
        (await svc.SaveForm(control.FormPart, false)).Should().BeTrue();
        File.ReadAllBytes(path).Take(3).Should().Equal(utf8.GetPreamble());
    }

    /// <summary>
    /// The reload records the module's encoding before it parses the designer half. When the new text no longer
    /// parses, the half is kept as it was, and it used to keep its own copy of the old encoding too.
    /// </summary>
    [Fact]
    public async Task A_reload_whose_new_text_does_not_parse_still_moves_the_designer_half_to_the_files_encoding()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndUserControlProject());
        var control = project.Modules.Single();
        var half = control.FormPart!;
        var utf8 = new System.Text.UTF8Encoding(true);
        File.WriteAllBytes(control.AbsolutePath!,
            utf8.GetPreamble().Concat(utf8.GetBytes("VERSION 5.00\r\nBegin VB.Label UserControl1\r\nEnd\r\n")).ToArray());

        (await svc.ReloadModuleFromDisk(control)).Should().BeTrue();

        control.FormPart.Should().BeSameAs(half, "a designer file that does not parse leaves the half as it was");
        half.TextEncoding.Should().Be(Vb6TextEncoding.Utf8WithBom);
    }

    [Fact]
    public async Task An_unrepresentable_edit_does_not_write_or_repoint_a_module()
    {
        var svc = MakeService();
        await svc.OpenProject(WriteProject());
        var module = loaded.Single().Modules.Single();
        module.TextEncoding = new Vb6TextEncoding(1252);
        var path = module.AbsolutePath!;
        var original = File.ReadAllBytes(path);
        module.UpdateCode(module.Code + "' こんにちは\r\n");
        var copy = Path.Join(dir, "copy.bas");
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(copy));

        Func<Task> save = async () => await svc.SaveModule(module, true);
        (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which.Character.Should().Be("こ");

        await windowManager.Received().MessageBox(Arg.Is<string>(s => s.Contains("Module1") && s.Contains("1252")), "HexIDE",
            MessageBoxButtons.Ok, MessageBoxIcon.Warning);

        File.ReadAllBytes(path).Should().Equal(original);
        File.Exists(copy).Should().BeFalse();
        File.Exists(copy + ".tmp").Should().BeFalse();
        module.AbsolutePath.Should().Be(path);
        module.Code.Should().Contain("こんにちは", "the edit remains available for correction");
    }

    [Fact]
    public async Task Saving_an_ASCII_edit_preserves_ANSI_bytes_that_are_also_valid_UTF8()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        var original = System.Text.Encoding.Latin1.GetBytes(
            "Attribute VB_Name = \"Module1\"\r\n' caf\u00c3\u00a9\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllBytes(path, original);
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        // The bytes alone cannot tell: read as UTF-8 they would also be written back unchanged.
        module.TextEncoding.Should().Be(new Vb6TextEncoding(1252), "nothing above U+00FF, so not an earlier version's UTF-8");
        module.UpdateCode(module.Code.Replace("Original", "Edited", StringComparison.Ordinal));

        await svc.SaveModule(module, false);

        File.ReadAllBytes(path).Should().Equal(System.Text.Encoding.Latin1.GetBytes(
            System.Text.Encoding.Latin1.GetString(original).Replace("Original", "Edited", StringComparison.Ordinal)));
    }

    // ── No member is dropped for its encoding ─────────────────────────────────────────────────────────

    private const string ReadByteForByteText = "{0} was read byte for byte; code page {1} could not read it.";
    private const string ReadByteForByteManyText = "These were read byte for byte; code page {1} could not read them:\n{0}";

    /// <summary>Every message box text shown so far.</summary>
    private List<string> MessagesShown() => windowManager.ReceivedCalls()
        .Where(c => c.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
        .Select(c => (string)c.GetArguments()[0]!)
        .ToList();

    private static byte[] ShiftJis(string text) =>
        System.Text.CodePagesEncodingProvider.Instance.GetEncoding(932)!.GetBytes(text);

    private static readonly byte[] WesternModule = System.Text.Encoding.Latin1.GetBytes(
        "Attribute VB_Name = \"Module1\"\r\n' café\r\nPublic Sub Original()\r\nEnd Sub\r\n");

    [Fact]
    public async Task A_module_the_hosts_code_page_cannot_read_stays_in_the_project_and_saves_back_unchanged()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(932);
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        File.WriteAllBytes(path, WesternModule);

        await svc.OpenProject(vbp);

        var module = loaded.Single().Modules.Single();
        module.Code.Should().Contain("' café");
        await windowManager.Received().MessageBox(
            Arg.Is<string>(s => s.Contains("Module1.bas") && s.Contains("932")),
            Arg.Any<string>(), Arg.Any<MessageBoxButtons>(), Arg.Any<MessageBoxIcon>());

        await svc.SaveModule(module, false);
        await svc.SaveProject(loaded.Single(), false);

        File.ReadAllBytes(path).Should().Equal(WesternModule);
        File.ReadAllText(vbp).Should().Contain("Module=Module1; Module1.bas");
    }

    [Fact]
    public async Task Adding_a_file_the_hosts_code_page_cannot_read_adopts_it_byte_for_byte()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        await svc.OpenProject(vbp);
        svc.HostAnsi = new Vb6TextEncoding(932);
        var path = Path.Join(dir, "Western.bas");
        var bytes = System.Text.Encoding.Latin1.GetBytes(
            "Attribute VB_Name = \"Western\"\r\n' café\r\nPublic Sub Hello()\r\nEnd Sub\r\n");
        File.WriteAllBytes(path, bytes);

        var adopted = await svc.AddExistingModule(loaded.Single(), path, ModuleKind.StandardModule);

        adopted.Document.Should().NotBeNull();
        adopted.UnreadableInCodePage.Should().Be(932, "the warning names the code page the read actually tried");
        await svc.SaveModule(adopted.Document!, false);
        File.ReadAllBytes(path).Should().Equal(bytes);
    }

    [Fact]
    public async Task A_module_last_read_as_UTF8_reloads_when_replaced_by_an_ANSI_revision()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        var utf8 = new System.Text.UTF8Encoding(true);
        File.WriteAllBytes(path, utf8.GetPreamble().Concat(utf8.GetBytes(
            "Attribute VB_Name = \"Module1\"\r\n' naïve\r\nPublic Sub Original()\r\nEnd Sub\r\n")).ToArray());
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();

        // A checkout of a revision from before the file was UTF-8: E9 then a line break is not UTF-8.
        File.WriteAllBytes(path, WesternModule);

        (await svc.ReloadModuleFromDisk(module)).Should().BeTrue();
        module.Code.Should().Contain("' café");
        module.TextEncoding.Should().Be(new Vb6TextEncoding(1252), "the host code page reads it; Latin-1 is the last resort");
        await svc.SaveModule(module, false);
        File.ReadAllBytes(path).Should().Equal(WesternModule);
    }

    [Fact]
    public async Task A_module_an_earlier_version_wrote_as_UTF8_opens_as_UTF8_and_saves_back_unchanged()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        // What an earlier version wrote for text with a character above U+00FF: UTF-8, no byte-order mark.
        var original = System.Text.Encoding.UTF8.GetBytes(
            "Attribute VB_Name = \"Module1\"\r\n' “café” — naïve\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllBytes(path, original);

        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();

        module.Code.Should().Contain("' “café” — naïve");
        module.TextEncoding.Should().Be(Vb6TextEncoding.Utf8);
        await svc.SaveModule(module, false);
        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    public async Task A_reload_of_a_file_read_byte_for_byte_reads_its_new_bytes_afresh()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(932);
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        File.WriteAllBytes(path, WesternModule);
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        module.TextEncoding.Should().Be(Vb6TextEncoding.Latin1);

        // Converted outside the IDE into the host's own code page.
        File.WriteAllBytes(path, ShiftJis(
            "Attribute VB_Name = \"Module1\"\r\n' こんにちは\r\nPublic Sub Original()\r\nEnd Sub\r\n"));

        (await svc.ReloadModuleFromDisk(module)).Should().BeTrue();
        module.TextEncoding.Should().Be(new Vb6TextEncoding(932));
        module.Code.Should().Contain("' こんにちは");
    }

    [Fact]
    public async Task A_reload_that_falls_back_to_reading_byte_for_byte_says_so()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(932);
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        File.WriteAllBytes(path, ShiftJis(
            "Attribute VB_Name = \"Module1\"\r\n' こんにちは\r\nPublic Sub Original()\r\nEnd Sub\r\n"));
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        windowManager.ClearReceivedCalls();

        File.WriteAllBytes(path, WesternModule);

        (await svc.ReloadModuleFromDisk(module)).Should().BeTrue();
        module.TextEncoding.Should().Be(Vb6TextEncoding.Latin1);
        MessagesShown().Should().ContainSingle(m => m.Contains("Module1.bas") && m.Contains("932"));
    }

    [Fact]
    public async Task Several_members_read_byte_for_byte_get_one_notice_of_their_own()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(932);
        File.WriteAllBytes(Path.Join(dir, "Module1.bas"), WesternModule);
        File.WriteAllBytes(Path.Join(dir, "Module2.bas"), System.Text.Encoding.Latin1.GetBytes(
            "Attribute VB_Name = \"Module2\"\r\n' naïve\r\nPublic Sub Two()\r\nEnd Sub\r\n"));
        File.WriteAllBytes(Path.Join(dir, "Main.frm"), System.Text.Encoding.Latin1.GetBytes(
            "VERSION 5.00\r\nBegin VB.Form Main\r\n   Caption         =   \"Café\"\r\nEnd\r\nAttribute VB_Name = \"Main\"\r\n"));
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp,
            "Type=Exe\r\nForm=Main.frm\r\nForm=Missing.frm\r\nModule=Module1; Module1.bas\r\nModule=Module2; Module2.bas\r\nName=\"Test\"\r\n");

        await svc.OpenProject(vbp);

        loaded.Single().Forms.Single().TextEncoding.Should().Be(Vb6TextEncoding.Latin1);
        var shown = MessagesShown();
        shown.Should().ContainSingle(m => m.StartsWith("These were read byte for byte", StringComparison.Ordinal))
            .Which.Should().ContainAll("Main.frm", "Module1.bas", "Module2.bas", "932");
        shown.Should().ContainSingle(m => m.Contains("Missing.frm"))
            .Which.Should().NotContain("byte for byte", "the load's errors are not where a member that loaded is reported");
    }

    [Fact]
    public async Task A_form_that_fails_to_parse_is_not_reported_as_read_byte_for_byte()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(932);
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllBytes(Path.Join(dir, "Broken.frm"), System.Text.Encoding.Latin1.GetBytes("Not a form: café\r\n"));
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Broken.frm\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");

        await svc.OpenProject(vbp);

        MessagesShown().Should().NotContain(m => m.Contains("byte for byte"),
            "a notice that the file will save back unchanged is untrue of a member that did not load");
    }

    [Fact]
    public async Task A_byte_order_mark_over_ANSI_text_is_kept_and_the_header_still_parses()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(WesternModule).ToArray();
        File.WriteAllBytes(path, original);

        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        module.Code.Should().NotContain("Attribute VB_Name", "the header was recognised and split off");
        await svc.SaveProject(loaded.Single(), false);

        File.ReadAllBytes(path).Should().Equal(original);
    }

    [Fact]
    public async Task A_group_file_the_hosts_code_page_cannot_read_is_named_in_a_notice()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(932);
        var vbp = WriteProject();
        var vbg = Path.Join(dir, "Group.vbg");
        File.WriteAllBytes(vbg, System.Text.Encoding.Latin1.GetBytes(
            "VBGROUP 5.0\r\nStartupProject=Test.vbp\r\n' café\r\n"));

        await svc.OpenProject(vbg);

        MessagesShown().Should().Contain(m => m.Contains("Group.vbg") && m.Contains("932"));
    }

    [Fact]
    public async Task A_form_that_does_not_parse_keeps_its_line_in_the_project_file()
    {
        var svc = MakeService();
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(dir, "Broken.frm"), "This is not a form.\r\n");
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Broken.frm\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");

        await svc.OpenProject(vbp);
        await svc.SaveProject(loaded.Single(), false);

        File.ReadAllText(vbp).Should().Contain("Form=Broken.frm");
    }

    [Fact]
    public async Task A_module_whose_load_throws_keeps_its_line_in_the_project_file()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        await using (new FileStream(Path.Join(dir, "Module1.bas"), FileMode.Open, FileAccess.Read, FileShare.None))
            await svc.OpenProject(vbp);

        loaded.Single().Modules.Should().BeEmpty();
        await svc.SaveProject(loaded.Single(), false);

        File.ReadAllText(vbp).Should().Contain("Module=Module1; Module1.bas");
    }

    [Fact]
    public async Task A_form_whose_load_throws_keeps_its_line_in_the_project_file()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        var frm = Path.Join(dir, "Main.frm");
        File.WriteAllText(frm, "VERSION 5.00\r\nBegin VB.Form Main\r\nEnd\r\n");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Main.frm\r\nModule=Module1; Module1.bas\r\nName=\"Test\"\r\n");
        await using (new FileStream(frm, FileMode.Open, FileAccess.Read, FileShare.None))
            await svc.OpenProject(vbp);

        loaded.Single().Forms.Should().BeEmpty();
        await svc.SaveProject(loaded.Single(), false);

        File.ReadAllText(vbp).Should().Contain("Form=Main.frm");
    }

    [Theory]
    [InlineData("Class1.cls", "Class=Class1; Class1.cls", "VERSION 1.0 CLASS\r\nBEGIN\r\n  MultiUse = -1  'True\r\nEND\r\nAttribute VB_Name = \"Class1\"\r\n")]
    [InlineData("Gauge.ctl", "UserControl=Gauge.ctl", "VERSION 5.00\r\nBegin VB.UserControl Gauge\r\nEnd\r\nAttribute VB_Name = \"Gauge\"\r\n")]
    [InlineData("Page1.pag", "PropertyPage=Page1.pag", "VERSION 5.00\r\nBegin VB.PropertyPage Page1\r\nEnd\r\nAttribute VB_Name = \"Page1\"\r\n")]
    public async Task A_member_of_any_kind_whose_load_throws_keeps_its_line_in_VB6s_shape(string file, string line, string content)
    {
        var svc = MakeService();
        var vbp = WriteProject();
        var member = Path.Join(dir, file);
        File.WriteAllText(member, content);
        File.AppendAllText(vbp, line + "\r\n");
        await using (new FileStream(member, FileMode.Open, FileAccess.Read, FileShare.None))
            await svc.OpenProject(vbp);

        await svc.SaveProject(loaded.Single(), false);

        File.ReadAllText(vbp).Should().Contain(line + "\r\n");
    }

    [Fact]
    public async Task Adding_a_file_whose_line_was_kept_does_not_name_it_twice()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        await using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None))
            await svc.OpenProject(vbp);
        var project = loaded.Single();

        (await svc.AddExistingModule(project, path, ModuleKind.StandardModule)).Document.Should().NotBeNull();
        await svc.SaveProject(project, false);

        System.Text.RegularExpressions.Regex.Matches(File.ReadAllText(vbp), "Module1.bas").Should().ContainSingle();
    }

    [Fact]
    public async Task Saving_an_ASCII_edit_preserves_a_UTF8_BOM_and_non_ASCII_text()
    {
        var svc = MakeService();
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        var utf8 = new System.Text.UTF8Encoding(true);
        var text = "Attribute VB_Name = \"Module1\"\r\n' café\r\nPublic Sub Original()\r\nEnd Sub\r\n";
        File.WriteAllBytes(path, utf8.GetPreamble().Concat(utf8.GetBytes(text)).ToArray());
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        module.UpdateCode(module.Code.Replace("Original", "Edited", StringComparison.Ordinal));

        await svc.SaveModule(module, false);

        File.ReadAllBytes(path).Should().Equal(utf8.GetPreamble().Concat(
            utf8.GetBytes(text.Replace("Original", "Edited", StringComparison.Ordinal))));
    }

    // ── Every save refuses the same way, and checks every file before writing any ────────────────────

    /// <summary>Every file under the test directory, with its bytes.</summary>
    private Dictionary<string, byte[]> Snapshot() =>
        Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).ToDictionary(f => f, File.ReadAllBytes);

    private void ShouldBeUnchanged(Dictionary<string, byte[]> before)
    {
        var after = Snapshot();
        after.Keys.Should().BeEquivalentTo(before.Keys, "nothing is written, created or deleted");
        foreach (var (path, bytes) in before)
            after[path].Should().Equal(bytes, "{0} is not written", path);
    }

    /// <summary>A form with a companion, and two modules, all ASCII.</summary>
    private string WriteFormAndModulesProject(string folder = "")
    {
        var home = Path.Join(dir, folder);
        Directory.CreateDirectory(home);
        File.WriteAllText(Path.Join(home, "Form1.frm"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"Form1\"\r\n   Icon = \"Form1.frx\":0000\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        File.WriteAllBytes(Path.Join(home, "Form1.frx"), [4, 0, 0, 0, 1, 2, 3, 4]);
        File.WriteAllText(Path.Join(home, "Module1.bas"), "Attribute VB_Name = \"Module1\"\r\nPublic Sub One()\r\nEnd Sub\r\n");
        File.WriteAllText(Path.Join(home, "Module2.bas"), "Attribute VB_Name = \"Module2\"\r\nPublic Sub Two()\r\nEnd Sub\r\n");
        var vbp = Path.Join(home, "Test.vbp");
        File.WriteAllText(vbp,
            "Type=Exe\r\nForm=Form1.frm\r\nModule=Module1; Module1.bas\r\nModule=Module2; Module2.bas\r\nName=\"Test\"\r\n");
        return vbp;
    }

    private async Task<(ProjectService Service, ProjectDefinition Project)> OpenInWestern(string vbp)
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        await svc.OpenProject(vbp);
        windowManager.ClearReceivedCalls();
        recentProjects.ClearReceivedCalls();
        return (svc, loaded.Single());
    }

    private static void Append(ModuleDefinition module, string line) => module.UpdateCode(module.Code + line + "\r\n");

    [Theory]
    [InlineData("caption")]
    [InlineData("module")]
    [InlineData("project")]
    public async Task Make_refuses_a_document_its_code_page_cannot_hold_before_asking_where_to_put_the_program(string where)
    {
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject());
        var form = project.Forms.Single();
        var (expected, character) = where switch
        {
            "caption" => ("Form1", "→"),
            "module" => ("Module2", "→"),
            _ => ("Проект", "П"),
        };
        if (where == "caption")
            form.Components[0].SetProperty(VBProperties.CaptionProperty, "Go →");
        else if (where == "module")
            Append(project.Modules[1], "' →");
        else
            project.Name = "Проект";
        var paths = project.Forms.Select(f => f.AbsolutePath).Concat(project.Modules.Select(m => m.AbsolutePath))
            .Append(project.AbsolutePath).ToList();
        var before = Snapshot();

        Func<Task> make = () => svc.MakeProject(project);

        var refusal = (await make.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.CodePage, refusal.Character).Should().Be((expected, 1252, character));
        MessagesShown().Should().ContainSingle("the source-encoding warning, and not the fatal-error box")
            .Which.Should().Contain(expected).And.Contain("1252");
        await windowManager.DidNotReceiveWithAnyArgs().SaveFilePickerAsync(default!);
        ShouldBeUnchanged(before);
        project.Forms.Select(f => f.AbsolutePath).Concat(project.Modules.Select(m => m.AbsolutePath))
            .Append(project.AbsolutePath).Should().Equal(paths);
    }

    [Fact]
    public async Task Save_Project_writes_none_of_its_files_when_one_cannot_be_held()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject());
        project.Forms.Single().UpdateCode(project.Forms.Single().Code + "' edited\r\n");
        Append(project.Modules[0], "' edited");
        Append(project.Modules[1], "' →");
        var before = Snapshot();

        Func<Task> save = () => svc.SaveProject(project, false);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.CodePage, refusal.Character).Should().Be(("Module2", 1252, "→"));
        ShouldBeUnchanged(before);
        eventBus.DidNotReceive().Publish(Arg.Any<DocumentSavedEvent>());
        await sidecar.DidNotReceive().SaveAsync(Arg.Any<ProjectDefinition>());
        recentProjects.DidNotReceiveWithAnyArgs().Add(default!);
        MessagesShown().Should().ContainSingle();
    }

    [Fact]
    public async Task The_menus_Save_Project_warns_and_writes_none_of_the_files()
    {
        // File > Save Project and the toolbar go through SaveAllProjects. The refusal is a cancellation, which
        // nothing upstream reports, so without the warning a person would believe the save had happened.
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject());
        Append(project.Modules[0], "' edited");
        Append(project.Modules[1], "' →");
        var before = Snapshot();

        Func<Task> save = () => svc.SaveAllProjects(false);

        await save.Should().ThrowAsync<SourceEncodingRefusal>();
        MessagesShown().Should().ContainSingle().Which.Should().Contain("Module2").And.Contain("1252")
            .And.Contain("U+2192").And.Contain("in ' →", "the line that holds it is quoted");
        ShouldBeUnchanged(before);
    }

    [Fact]
    public async Task Writing_a_project_to_a_folder_writes_nothing_when_one_file_cannot_be_held()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject());
        Append(project.Modules[1], "' →");
        var target = Path.Join(dir, "out");
        var paths = project.Forms.Select(f => f.AbsolutePath).Concat(project.Modules.Select(m => m.AbsolutePath))
            .Append(project.AbsolutePath).ToList();
        var before = Snapshot();

        Func<Task> save = () => svc.SaveProjectToDirectory(project, target);

        (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which.Document.Should().Be("Module2");
        Directory.Exists(target).Should().BeFalse("nothing is written, so no folder is made for it");
        ShouldBeUnchanged(before);
        project.Forms.Select(f => f.AbsolutePath).Concat(project.Modules.Select(m => m.AbsolutePath))
            .Append(project.AbsolutePath).Should().Equal(paths);
    }

    /// <summary>A form with no companion, and a UserControl holding a label.</summary>
    private string WriteFormAndUserControlProject()
    {
        File.WriteAllText(Path.Join(dir, "Form1.frm"),
            "VERSION 5.00\r\nBegin VB.Form Form1\r\n   Caption = \"Form1\"\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n");
        File.WriteAllText(Path.Join(dir, "UserControl1.ctl"),
            "VERSION 5.00\r\nBegin VB.UserControl UserControl1\r\n   ClientHeight = 3000\r\n"
            + "   Begin VB.Label Label1\r\n      Caption = \"Label1\"\r\n   End\r\n"
            + "End\r\nAttribute VB_Name = \"UserControl1\"\r\n");
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nForm=Form1.frm\r\nUserControl=UserControl1.ctl\r\nName=\"Test\"\r\n");
        return vbp;
    }

    [Fact]
    public async Task Save_Project_checks_a_UserControls_designer_half_before_writing_any_file()
    {
        // Form1 is written first, so a check that saw only the control's code would pass it, and Form1 would be
        // on disk before the control's own write refused.
        var (svc, project) = await OpenInWestern(WriteFormAndUserControlProject());
        var form = project.Forms.Single();
        form.UpdateCode(form.Code + "' edited\r\n");
        var control = project.Modules.Single();
        control.FormPart!.Components.Last().SetProperty(VBProperties.CaptionProperty, "Go →");
        var before = Snapshot();

        Func<Task> save = () => svc.SaveProject(project, false);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("UserControl1", "→"));
        ShouldBeUnchanged(before);
    }

    [Fact]
    public async Task An_automation_save_of_a_UserControl_names_the_character_in_its_designer_half()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndUserControlProject());
        var control = project.Modules.Single();
        control.FormPart!.Components.Last().SetProperty(VBProperties.CaptionProperty, "Go →");
        var before = Snapshot();

        Func<Task> save = () => svc.SaveModule(control, false, interactive: false);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("UserControl1", "→"));
        MessagesShown().Should().BeEmpty();
        ShouldBeUnchanged(before);
    }

    [Fact]
    public async Task Save_Form_As_to_a_name_the_project_file_cannot_hold_is_refused_in_the_project_files_name()
    {
        // The form cites no companion, so its own text does not hold its file name. The project file, which
        // names it by that file, cannot. Without the check the form was written and repointed, and every
        // later save of the project refused, naming the project and not the form.
        var (svc, project) = await OpenInWestern(WriteFormAndUserControlProject());
        var form = project.Forms.Single();
        var formPath = form.AbsolutePath;
        var chosen = Path.Join(dir, "Форма1.frm");
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(chosen));
        var before = Snapshot();

        Func<Task> save = () => svc.SaveForm(form, saveAs: true);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("Test", "Ф"));
        ShouldBeUnchanged(before);
        form.AbsolutePath.Should().Be(formPath);
    }

    [Fact]
    public async Task Save_Form_As_is_not_refused_for_a_project_file_that_cannot_be_written_as_it_stands()
    {
        // The project's own name is what its file cannot hold, before and after. That is the project's save to
        // refuse; this one does not write the project file.
        var (svc, project) = await OpenInWestern(WriteFormAndUserControlProject());
        project.Name = "Проект";
        var chosen = Path.Join(dir, "Form2.frm");
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(chosen));

        (await svc.SaveForm(project.Forms.Single(), saveAs: true)).Should().BeTrue();
        File.Exists(chosen).Should().BeTrue();
    }

    [Fact]
    public async Task Save_Project_As_to_a_name_the_group_file_cannot_hold_is_refused_in_the_group_files_name()
    {
        var vbp = WriteProject();
        var vbg = Path.Join(dir, "Group.vbg");
        File.WriteAllText(vbg, "VBGROUP 5.0\r\nProject=Test.vbp\r\nStartupProject=Test.vbp\r\n");
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        await svc.OpenProject(vbg);
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(Path.Join(dir, "Проект.vbp")));
        var before = Snapshot();

        Func<Task> save = () => svc.SaveProject(loaded.Single(), saveAs: true);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("Group", "П"));
        ShouldBeUnchanged(before);
        loaded.Single().AbsolutePath.Should().Be(vbp);
    }

    [Fact]
    public async Task A_refused_save_keeps_a_companion_it_would_have_deleted()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject());
        var root = project.Forms.Single().Components[0];
        var icon = root.BaseClass.Properties.Single(p =>
            p.PropertyType == typeof(byte[]) && root.TryGetBoxedProperty(p, out var value) && value is byte[]);
        root.SetUntypedProperty(icon, null);
        Append(project.Modules[1], "' →");
        var frx = Path.Join(dir, "Form1.frx");
        var companion = File.ReadAllBytes(frx);

        Func<Task> save = () => svc.SaveProject(project, false);

        await save.Should().ThrowAsync<SourceEncodingRefusal>();
        File.ReadAllBytes(frx).Should().Equal(companion);

        // The premise: without the refusal, this save does delete it.
        project.Modules[1].UpdateCode(project.Modules[1].Code.Replace("' →\r\n", "", StringComparison.Ordinal));
        await svc.SaveProject(project, false);
        File.Exists(frx).Should().BeFalse("clearing the form's only picture deletes its companion on save");
    }

    [Fact]
    public async Task Save_Project_As_checks_the_project_file_at_its_new_place_before_writing_any_member()
    {
        // From its new place the project file names each member by a path through a folder Windows-1252
        // cannot spell. Checked where it is now, it would pass, and the members would be written first.
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject("Модули"));
        Append(project.Modules[0], "' edited");
        var elsewhere = Path.Join(dir, "elsewhere");
        Directory.CreateDirectory(elsewhere);
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(Path.Join(elsewhere, "Test.vbp")));
        var vbp = project.AbsolutePath;
        var before = Snapshot();

        Func<Task> save = () => svc.SaveProject(project, saveAs: true);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("Test", "М"));
        await windowManager.Received(1).SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>());
        ShouldBeUnchanged(before);
        project.AbsolutePath.Should().Be(vbp);
    }

    [Fact]
    public async Task A_member_given_a_name_the_project_file_cannot_hold_is_not_written()
    {
        var (svc, project) = await OpenInWestern(WriteProject());
        var added = new ModuleDefinition(project, "Module2", ModuleKind.StandardModule) { TextEncoding = new Vb6TextEncoding(1252) };
        project.AddModule(added);
        var chosen = Path.Join(dir, "Модуль2.bas");
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(chosen));
        var before = Snapshot();

        Func<Task> save = () => svc.SaveProject(project, false);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("Test", "М"),
            "the module's own text can hold it; the project file, which names it by its file, cannot");
        File.Exists(chosen).Should().BeFalse();
        ShouldBeUnchanged(before);
        added.AbsolutePath.Should().BeNull();
    }

    [Fact]
    public async Task Save_All_As_writes_nothing_when_the_group_file_cannot_name_a_project_where_it_is_going()
    {
        var vbp = WriteProject();
        var vbg = Path.Join(dir, "Group.vbg");
        File.WriteAllText(vbg, "VBGROUP 5.0\r\nProject=Test.vbp\r\nStartupProject=Test.vbp\r\n");
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        await svc.OpenProject(vbg);
        projectManager.CurrentGroup!.TextEncoding.Should().Be(new Vb6TextEncoding(1252));
        windowManager.SaveFilePickerAsync(Arg.Is<Avalonia.Platform.Storage.FilePickerSaveOptions>(o => o.DefaultExtension == "vbp"))
            .Returns(Task.FromResult<string?>(Path.Join(dir, "Проект.vbp")));
        windowManager.SaveFilePickerAsync(Arg.Is<Avalonia.Platform.Storage.FilePickerSaveOptions>(o => o.DefaultExtension == "vbg"))
            .Returns(Task.FromResult<string?>(Path.Join(dir, "Group2.vbg")));
        Append(loaded.Single().Modules.Single(), "' edited");
        var before = Snapshot();

        Func<Task> save = () => svc.SaveAllProjects(saveAs: true);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("Group", "П"));
        MessagesShown().Should().ContainSingle().Which.Should().Contain("Group").And.Contain("1252");
        ShouldBeUnchanged(before);
        loaded.Single().AbsolutePath.Should().Be(vbp);
    }

    [Fact]
    public async Task A_picker_dismissed_part_way_through_Save_Project_writes_nothing()
    {
        var (svc, project) = await OpenInWestern(WriteProject());
        Append(project.Modules.Single(), "' edited");
        project.AddModule(new ModuleDefinition(project, "Module2", ModuleKind.StandardModule));
        windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
            .Returns(Task.FromResult<string?>(null));
        var before = Snapshot();

        Func<Task> save = () => svc.SaveProject(project, false);

        await save.Should().ThrowAsync<OperationCanceledException>();
        ShouldBeUnchanged(before);
    }

    [Fact]
    public async Task A_close_time_save_writes_none_of_the_files_when_one_cannot_be_held()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject());
        Append(project.Modules[0], "' edited");
        Append(project.Modules[1], "' →");
        var before = Snapshot();

        Func<Task> close = () => svc.UnloadAllProjects();

        (await close.Should().ThrowAsync<SourceEncodingRefusal>()).Which.Document.Should().Be("Module2");
        MessagesShown().Should().ContainSingle().Which.Should().Contain("Module2").And.Contain("1252");
        projectManager.DidNotReceive().UnloadAllProjects();
        ShouldBeUnchanged(before);
    }

    // ── A new member ─────────────────────────────────────────────────────────────────────────────────

    private static Task AddNew(ProjectService svc, ProjectDefinition project, string kind, string name, bool interactive) =>
        kind switch
        {
            "Form" => svc.AddNewForm(project, name, interactive),
            "Module" => svc.AddNewModule(project, name, ModuleKind.StandardModule, interactive),
            "Class" => svc.AddNewModule(project, name, ModuleKind.ClassModule, interactive),
            "UserControl" => svc.AddNewUserControl(project, name, interactive),
            _ => svc.AddNewPropertyPage(project, name, interactive),
        };

    [Theory]
    [InlineData("Form")]
    [InlineData("Module")]
    [InlineData("Class")]
    [InlineData("UserControl")]
    [InlineData("PropertyPage")]
    public async Task A_new_member_whose_name_its_code_page_cannot_hold_is_refused_before_anything_exists(string kind)
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { TextEncoding = new Vb6TextEncoding(1252) };

        Func<Task> add = () => AddNew(svc, project, kind, "Ж1", interactive: false);

        var refusal = (await add.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.CodePage, refusal.Character).Should().Be(("Ж1", 1252, "Ж"));
        project.Forms.Should().BeEmpty();
        project.Modules.Should().BeEmpty();
        project.WorkingDirectory.Should().BeNull("no folder is chosen for a file that is never written");
        MessagesShown().Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_member_the_project_file_cannot_name_is_refused_in_the_project_files_name()
    {
        // Š is 8A in Windows-1252, which the new file is written in, and is not in Latin-1, which the
        // project file was read in.
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { TextEncoding = Vb6TextEncoding.Latin1 };

        Func<Task> add = () => svc.AddNewModule(project, "Šifra1", ModuleKind.StandardModule, interactive: false);

        var refusal = (await add.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.CodePage, refusal.Character).Should().Be(("Test", 28591, "Š"));
        project.Modules.Should().BeEmpty();
        project.WorkingDirectory.Should().BeNull();
    }

    [Fact]
    public async Task A_new_member_added_from_the_IDE_is_refused_with_the_source_encoding_warning()
    {
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1252);
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { TextEncoding = new Vb6TextEncoding(1252) };

        Func<Task> add = () => svc.AddNewModule(project, "Ж1", ModuleKind.StandardModule);

        await add.Should().ThrowAsync<OperationCanceledException>();
        MessagesShown().Should().ContainSingle().Which.Should().Contain("Ж1").And.Contain("1252");
        project.Modules.Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_member_takes_the_host_code_page()
    {
        // Central European rather than Western, so the test cannot pass on the static default of a Western host.
        var svc = MakeService();
        svc.HostAnsi = new Vb6TextEncoding(1250);
        var project = new ProjectDefinition(VBProjectType.EXE, "Test") { TextEncoding = new Vb6TextEncoding(1250) };
        try
        {
            var form = await svc.AddNewForm(project, "Café1");
            var control = await svc.AddNewUserControl(project, "Café2");

            form.TextEncoding.Should().Be(new Vb6TextEncoding(1250));
            control.TextEncoding.Should().Be(new Vb6TextEncoding(1250));
            control.FormPart!.TextEncoding.Should().Be(new Vb6TextEncoding(1250));
            // Written by its first save, not as it is added (#500).
            windowManager.SaveFilePickerAsync(Arg.Any<Avalonia.Platform.Storage.FilePickerSaveOptions>())
                .Returns(Task.FromResult<string?>(Path.Join(dir, "Café1.frm")));
            (await svc.SaveForm(form, saveAs: false)).Should().BeTrue();
            File.ReadAllBytes(form.AbsolutePath!).Should().Contain(0xE9, "é is written as its one Windows-1250 byte");
        }
        finally
        {
            if (project.WorkingDirectory is { } scratch)
                try { Directory.Delete(scratch, recursive: true); } catch { /* best effort */ }
        }
    }

    // ── An automation save ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task An_automation_save_returns_the_refusal_and_shows_no_dialog()
    {
        var (svc, project) = await OpenInWestern(WriteProject());
        var module = project.Modules.Single();
        var original = File.ReadAllBytes(module.AbsolutePath!);
        Append(module, "' →");

        Func<Task> save = () => svc.SaveModule(module, false, interactive: false);

        (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which.Message.Should().Be(
            "Module1 is kept in code page 1252, which cannot hold '→' (U+2192), found in the line `' →`. Nothing "
            + "was written. HexIDE keeps a file in the code page it was read or created in rather than convert it, "
            + "so use characters that code page holds, and where that line names a file or folder, a name it can "
            + "hold.");
        MessagesShown().Should().BeEmpty();
        File.ReadAllBytes(module.AbsolutePath!).Should().Equal(original);
        module.Code.Should().Contain("' →", "the edit stays in the IDE for the caller to correct");
    }

    [Fact]
    public async Task An_automation_save_of_a_form_names_the_character_in_its_caption()
    {
        var (svc, project) = await OpenInWestern(WriteFormAndModulesProject());
        var form = project.Forms.Single();
        form.Components[0].SetProperty(VBProperties.CaptionProperty, "Go →");
        var before = Snapshot();

        Func<Task> save = () => svc.SaveForm(form, false, interactive: false);

        var refusal = (await save.Should().ThrowAsync<SourceEncodingRefusal>()).Which;
        (refusal.Document, refusal.Character).Should().Be(("Form1", "→"));
        MessagesShown().Should().BeEmpty();
        ShouldBeUnchanged(before);
    }
}
