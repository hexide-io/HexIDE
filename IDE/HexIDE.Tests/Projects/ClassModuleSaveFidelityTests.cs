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

/// <summary>
/// End-to-end proof for issue #18 — that the real ProjectService load → save path preserves a class
/// header, not merely that ModuleFileFormat can.
///
/// ClassHeaderPreservationTests covers the format helper. This covers the wiring, which is where the
/// equivalent UserControl bug (#12/#17) actually lived: the helper was correct and the caller passed it
/// the wrong thing. Testing only the helper would have missed that twice.
/// </summary>
public class ClassModuleSaveFidelityTests : IDisposable
{
    private readonly string dir = Path.Join(Path.GetTempPath(), "hexide-cls-" + Guid.NewGuid().ToString("N"));

    private static readonly string Vb6Classes =
        Path.Join(Environment.GetEnvironmentVariable("VB6_TEMPLATES")
                  ?? @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template", "Classes");

    public ClassModuleSaveFidelityTests() => Directory.CreateDirectory(dir);

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort */ }
        GC.SuppressFinalize(this);
    }

    private readonly List<ProjectDefinition> loaded = new();

    private ProjectService MakeService()
    {
        var projectManager = Substitute.For<IProjectManager>();
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>()))
                      .Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));

        var windowManager = Substitute.For<IWindowManager>();
        windowManager.ShowDialog(Arg.Any<IDialog>()).Returns(ci =>
        {
            if (ci.Arg<IDialog>() is SaveChangesViewModel vm) vm.Yes();
            return Task.FromResult(true);
        });

        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);

        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"),
            windowManager,
            Substitute.For<IEventBus>(),
            projectManager,
            Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(),
            sidecar,
            new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>());
    }

    /// <summary>Stages a VB6-shipped class beside a minimal .vbp. Null when VB6 is absent.</summary>
    private string? StageClass(string fileName)
    {
        var src = Path.Join(Vb6Classes, fileName);
        if (!File.Exists(src)) return null;

        // The .vbp's Class= name is the module's VB_Name, not its file name — that is what a real project
        // has, and getting it wrong makes the rename path fire and look like a fidelity defect.
        var text = File.ReadAllText(src);
        var vbName = text.Split('\n')
            .Select(l => l.Trim())
            .Where(l => l.StartsWith("Attribute VB_Name", StringComparison.OrdinalIgnoreCase))
            .Select(l => l.Substring(l.IndexOf('"') + 1).TrimEnd('"', '\r'))
            .First();

        var fileStem = Path.GetFileNameWithoutExtension(src).Replace(" ", "");
        File.Copy(src, Path.Join(dir, fileStem + ".cls"));
        var vbp = Path.Join(dir, "Test.vbp");
        File.WriteAllText(vbp, $"Type=Exe\r\nClass={vbName}; {fileStem}.cls\r\nName=\"Test\"\r\n");
        return vbp;
    }

    [Theory]
    [InlineData("Complex Data Consumer.cls")]
    [InlineData("Data Source.cls")]
    public async Task Saving_a_project_leaves_an_untouched_class_byte_identical(string fileName)
    {
        var vbp = StageClass(fileName);
        if (vbp is null) return; // VB6 not installed (CI)

        var clsPath = Directory.EnumerateFiles(dir, "*.cls").Single();
        var before = await File.ReadAllTextAsync(clsPath);

        var svc = MakeService();
        await svc.OpenProject(vbp);
        await svc.SaveProject(loaded.Single(), saveAs: false);

        (await File.ReadAllTextAsync(clsPath)).Should().Be(before,
            "a save must not rewrite a class the user never opened");
    }

    [Fact]
    public async Task Editing_the_body_leaves_the_header_untouched()
    {
        var vbp = StageClass("Data Source.cls");
        if (vbp is null) return;

        var clsPath = Directory.EnumerateFiles(dir, "*.cls").Single();
        var before = await File.ReadAllTextAsync(clsPath);

        var svc = MakeService();
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        module.UpdateCode(module.Code + "\r\nPublic Sub Added()\r\nEnd Sub\r\n");
        await svc.SaveProject(loaded.Single(), saveAs: false);

        var after = await File.ReadAllTextAsync(clsPath);
        after.Should().Contain("Public Sub Added()");

        // Everything above the first non-attribute line must be identical.
        static string HeaderOf(string s) =>
            string.Join("\n", s.Split('\n').TakeWhile(l =>
                l.TrimStart().StartsWith("VERSION", StringComparison.OrdinalIgnoreCase) ||
                l.TrimStart().StartsWith("BEGIN", StringComparison.OrdinalIgnoreCase) ||
                l.TrimStart().StartsWith("END", StringComparison.OrdinalIgnoreCase) ||
                l.TrimStart().StartsWith("Attribute ", StringComparison.OrdinalIgnoreCase) ||
                l.Contains('=')));

        HeaderOf(after).Should().Be(HeaderOf(before));
    }

    [Fact]
    public async Task A_reload_from_disk_also_preserves_the_header()
    {
        // ReloadModuleFromDisk is a second, independent load path — it had the same defect class as the
        // main loader for UserControls (#17), so it gets its own assertion here rather than assumed.
        var vbp = StageClass("Data Source.cls");
        if (vbp is null) return;

        var clsPath = Directory.EnumerateFiles(dir, "*.cls").Single();
        var before = await File.ReadAllTextAsync(clsPath);

        var svc = MakeService();
        await svc.OpenProject(vbp);
        var module = loaded.Single().Modules.Single();
        (await svc.ReloadModuleFromDisk(module)).Should().BeTrue();
        await svc.SaveProject(loaded.Single(), saveAs: false);

        (await File.ReadAllTextAsync(clsPath)).Should().Be(before);
    }
}
