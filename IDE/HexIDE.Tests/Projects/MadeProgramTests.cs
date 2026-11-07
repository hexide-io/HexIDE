using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using HexIDE.IDE;
using HexIDE.Events;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Sidecar;

namespace HexIDE.Tests.Projects;

/// <summary>
/// A made program starts (#1971). Every earlier Make test copied stand-in files and never ran one, which is how a runner
/// that could not start went unnoticed from #30 to v0.2.0. This one makes a Sub Main program with the real runner, runs it,
/// and reads the file it wrote.
/// </summary>
public class MadeProgramTests : IDisposable
{
    private readonly string dir = Directory.CreateTempSubdirectory("hexide-made-").FullName;
    private readonly List<ProjectDefinition> loaded = [];
    private readonly IProjectManager projectManager = Substitute.For<IProjectManager>();
    private readonly IWindowManager windowManager = Substitute.For<IWindowManager>();

    public void Dispose()
    {
        try { Directory.Delete(dir, recursive: true); } catch { /* best effort: a killed program may hold its file a moment */ }
        GC.SuppressFinalize(this);
    }

    [MadeProgramFact]
    public async Task A_made_program_starts_and_runs_its_Sub_Main()
    {
        var marker = Path.Join(dir, "marker.txt");
        File.WriteAllText(Path.Join(dir, "Module1.bas"),
            "Attribute VB_Name = \"Module1\"\r\nPublic Sub Main()\r\n    Dim f As Integer\r\n    f = FreeFile\r\n"
            + $"    Open \"{marker}\" For Output As #f\r\n    Print #f, \"made\"\r\n    Close #f\r\nEnd Sub\r\n");
        var vbp = Path.Join(dir, "Made.vbp");
        File.WriteAllText(vbp, "Type=Exe\r\nModule=Module1; Module1.bas\r\nStartup=\"Sub Main\"\r\nName=\"Made\"\r\n");
        var exe = Path.Join(dir, "out", OperatingSystem.IsWindows() ? "Made.exe" : "Made");
        Directory.CreateDirectory(Path.GetDirectoryName(exe)!);

        var service = Service();
        service.RuntimeDirectory = MadeProgramFactAttribute.MakeRuntime;
        windowManager.SaveFilePickerAsync(Arg.Any<FilePickerSaveOptions>()).Returns(exe);
        await service.OpenProject(vbp);
        await service.MakeProject(loaded.Single());

        using var program = Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe)! })!;
        var exited = program.WaitForExit(TimeSpan.FromMinutes(2));
        if (!exited) program.Kill(entireProcessTree: true);

        exited.Should().BeTrue("a Sub Main program with no form ends when Main returns; one still running is showing an error");
        program.ExitCode.Should().Be(0);
        File.ReadAllText(marker).Trim().Should().Be("made");
    }

    [Fact]
    public void The_runner_is_present_where_the_build_requires_it()
    {
        // A skip that nobody reads proves nothing, so Windows CI sets HEXIDE_REQUIRE_MADE_PROGRAM, which turns the
        // test above's skip into this failure. Kept out of the attribute, which xunit v3 discards when it throws.
        if (Environment.GetEnvironmentVariable("HEXIDE_REQUIRE_MADE_PROGRAM") != "1") return;
        File.Exists(MadeProgramFactAttribute.Runner).Should().BeTrue(
            $"HEXIDE_REQUIRE_MADE_PROGRAM is set, and the runner should have been published to {MadeProgramFactAttribute.Runner}");
    }

    private ProjectService Service()
    {
        projectManager.LoadedProjects.Returns(_ => loaded);
        projectManager.When(m => m.AddProject(Arg.Any<ProjectDefinition>())).Do(ci => loaded.Add(ci.Arg<ProjectDefinition>()));
        var sidecar = Substitute.For<IUserSidecarService>();
        sidecar.LoadAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        sidecar.SaveAsync(Arg.Any<ProjectDefinition>()).Returns(Task.CompletedTask);
        return new ProjectService(
            () => throw new InvalidOperationException("new-project dialog must not be reached"), windowManager,
            Substitute.For<IEventBus>(), projectManager, Substitute.For<IRecentProjectsService>(),
            Substitute.For<IReferenceLibraryService>(), sidecar, new FileBaselineStore(),
            Substitute.For<HexIDE.Localization.ILocalizationService>(), Substitute.For<IHeaderRefresher>());
    }
}

/// <summary>
/// Runs where a runner has been published: on Windows, into <c>artifacts/make-runtime/standalone/</c>, or into the
/// <c>standalone/</c> folder of <c>HEXIDE_MAKE_RUNTIME</c>. Other platforms skip, since starting a program needs a desktop.
/// </summary>
public sealed class MadeProgramFactAttribute : FactAttribute
{
    public static string MakeRuntime { get; } = Environment.GetEnvironmentVariable("HEXIDE_MAKE_RUNTIME") is { Length: > 0 } set
        ? set
        : Path.Join(RepoRoot(), "artifacts", "make-runtime");

    public static string Runner => Path.Join(MakeRuntime, "standalone", ProjectService.StandaloneRunnerName() ?? "HexIDE.Standalone");

    public MadeProgramFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows()) Skip = "A made program is started on Windows only: elsewhere it needs a desktop session.";
        else if (!File.Exists(Runner))
            Skip = $"No runner at {Runner}. From IDE/, run "
                 + "`dotnet publish HexIDE.Standalone -f net10.0 -o ../artifacts/make-runtime/standalone/` to run this.";
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !File.Exists(Path.Join(dir.FullName, "CLAUDE.md"))) dir = dir.Parent;
        return dir?.FullName ?? AppContext.BaseDirectory;
    }
}
