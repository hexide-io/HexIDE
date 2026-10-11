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
