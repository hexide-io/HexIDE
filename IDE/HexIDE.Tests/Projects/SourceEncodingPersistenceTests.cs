using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Projects;
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

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        var localization = Substitute.For<HexIDE.Localization.ILocalizationService>();
        localization.GetString("Str.Dialog.SourceEncoding.SaveFailed")
            .Returns("{0} cannot be saved using source code page {1}. Your edit remains open.");
        localization.GetString("Str.Dialog.SourceEncoding.ReadByteForByte").Returns(ReadByteForByteText);
        localization.GetString("Str.Dialog.SourceEncoding.ReadByteForByte.Many").Returns(ReadByteForByteManyText);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            localization);
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
        await save.Should().ThrowAsync<OperationCanceledException>();

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
        await save.Should().ThrowAsync<OperationCanceledException>();

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
        var vbp = WriteProject();
        var path = Path.Join(dir, "Module1.bas");
        var original = System.Text.Encoding.Latin1.GetBytes(
            "Attribute VB_Name = \"Module1\"\r\n' caf\u00c3\u00a9\r\nPublic Sub Original()\r\nEnd Sub\r\n");
        File.WriteAllBytes(path, original);
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
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
}
