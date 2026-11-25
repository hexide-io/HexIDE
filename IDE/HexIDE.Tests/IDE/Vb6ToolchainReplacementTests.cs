using System.Security.Cryptography;
using System.Text.Json;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Sidecar;

namespace HexIDE.Tests.IDE;

// Consent follows the IDE measurements in oracle PR #4501; Run ownership is the decision in #4504.
public sealed class Vb6ToolchainReplacementTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "hexide-vb6-replace-" + Guid.NewGuid().ToString("N"));
    private readonly IWindowManager windows = Substitute.For<IWindowManager>();
    private readonly IEventBus events = Substitute.For<IEventBus>();
    private readonly IProjectManager projects = Substitute.For<IProjectManager>();
    private readonly ILocalizationService strings = Substitute.For<ILocalizationService>();
    private readonly UserSidecarService sidecar;
    private readonly ProjectDefinition project;
    private readonly Build build;
    private string ExePath => Path.Combine(directory, "Output.exe");
    private string SidecarPath => Path.Combine(directory, "Test.user.hexproj");

    public Vb6ToolchainReplacementTests()
    {
        Directory.CreateDirectory(directory);
        project = Project();
        strings.GetString(Arg.Any<string>()).Returns(c => (string)c[0]);
        strings.GetString("Str.FilePicker.ReplaceExisting").Returns("{0} already exists. Do you want to replace it?");
        Answer(MessageBoxResult.Yes);
        sidecar = NewSidecar();
        build = NewBuild(sidecar);
    }

    private Build NewBuild(IUserSidecarService store, string? outputPath = null)
    {
        var previous = Environment.GetEnvironmentVariable("VB6_EXE");
        Environment.SetEnvironmentVariable("VB6_EXE", Environment.ProcessPath);
        try { return new Build(windows, events, strings, store, outputPath ?? ExePath); }
        finally { Environment.SetEnvironmentVariable("VB6_EXE", previous); }
    }

    private ProjectDefinition Project()
    {
        var result = TestHelpers.CreateProjectWithForm();
        result.AbsolutePath = Path.Combine(directory, "Test.vbp");
        result.UnknownPreSectionLines.Add((0, "ExeName32=\"Output.exe\""));
        File.WriteAllText(result.AbsolutePath, "Type=Exe\r\nExeName32=\"Output.exe\"\r\n");
        return result;
    }

    private UserSidecarService NewSidecar() => new(
        Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(), projects);

    private void Answer(MessageBoxResult answer) => windows.MessageBox(
        Arg.Any<string>(), Arg.Any<string?>(), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<MessageBoxLayout>()).Returns(answer);

    private Task Asked(int count) => windows.Received(count).MessageBox(
        "Output.exe already exists. Do you want to replace it?", null,
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, null, MessageBoxLayout.SecondButtonDefault);

    private sealed class Build(IWindowManager windows, IEventBus events, ILocalizationService strings,
        IUserSidecarService sidecar, string exePath)
        : Vb6ToolchainService(Substitute.For<ILspClient>(), events, windows, strings,
            new CodeTrust(windows, strings, _ => false), sidecar)
    {
        public int Builds { get; private set; }
        public int Starts { get; private set; }
        public int ExitCode { get; set; }
        public bool Timeout { get; set; }
        public bool WriteOnFailure { get; set; }
        public string? OutputDirectory { get; private set; }
        public Action<string>? DuringBuild { get; set; }
        public Action? AfterBuild { get; set; }
        internal override Task<Vb6BuildOutcome?> RunVb6Async(string projectPath, string? outputDirectory)
        {
            Builds++;
            OutputDirectory = outputDirectory;
            DuringBuild?.Invoke(projectPath);
            if (ExitCode == 0 || WriteOnFailure)
                File.WriteAllText(outputDirectory is null ? exePath : Path.Combine(outputDirectory, Path.GetFileName(exePath)),
                    "built by RunVb6Async " + Builds);
            AfterBuild?.Invoke();
            if (Timeout) return Task.FromResult<Vb6BuildOutcome?>(null);
            return Task.FromResult<Vb6BuildOutcome?>(new(ExitCode, ""));
        }
        internal override void StartExe(string path)
        {
            path.Should().Be(exePath);
            Starts++;
        }
    }

    [Fact]
    public async Task Make_existing_output_asks_with_No_default()
    {
        File.WriteAllText(ExePath, "original");
        await build.MakeWithVb6Async(project);
        await Asked(1);
    }

    [Fact]
    public async Task Make_No_builds_and_writes_nothing()
    {
        File.WriteAllText(ExePath, "original");
        Answer(MessageBoxResult.No);
        (await build.MakeWithVb6Async(project)).Should().BeFalse();
        build.Builds.Should().Be(0);
        File.ReadAllText(ExePath).Should().Be("original");
        events.DidNotReceive().Publish(Arg.Any<ApplyAllUnsavedChangesEvent>());
        File.Exists(SidecarPath).Should().BeFalse();
        Directory.GetDirectories(directory).Should().BeEmpty();
    }

    [Fact]
    public async Task Make_Yes_asks_and_builds_without_recording_Run_ownership()
    {
        File.WriteAllText(ExePath, "original");
        (await build.MakeWithVb6Async(project)).Should().BeTrue();
        await Asked(1);
        build.Builds.Should().Be(1);
        File.ReadAllText(ExePath).Should().NotBe("original");
        File.Exists(SidecarPath).Should().BeFalse();
    }

    [Fact]
    public async Task Run_unowned_output_asks_once_and_No_starts_nothing()
    {
        File.WriteAllText(ExePath, "original");
        Answer(MessageBoxResult.No);
        await build.RunWithVb6Async(project);
        await Asked(1);
        build.Builds.Should().Be(0);
        build.Starts.Should().Be(0);
        File.ReadAllText(ExePath).Should().Be("original");
    }

    [Fact]
    public async Task Run_replaced_output_asks_once_and_No_starts_nothing()
    {
        await sidecar.LoadAsync(project);
        await build.RunWithVb6Async(project);
        File.WriteAllText(ExePath, "someone else's output");
        Answer(MessageBoxResult.No);
        await build.RunWithVb6Async(project);
        await Asked(1);
        build.Builds.Should().Be(1);
        build.Starts.Should().Be(1);
        File.ReadAllText(ExePath).Should().Be("someone else's output");
    }

    [Fact]
    public async Task Run_records_hash_and_filename_and_keeps_them_on_reopen()
    {
        await sidecar.LoadAsync(project);
        await build.RunWithVb6Async(project);
        using var json = JsonDocument.Parse(File.ReadAllText(SidecarPath));
        var stored = json.RootElement.GetProperty("lastRunExe");
        stored.GetProperty("fileName").GetString().Should().Be("Output.exe");
        stored.GetProperty("hash").GetString().Should().Be(Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ExePath))));
        projects.ProjectUnloaded += Raise.Event<Action<ProjectDefinition>>(project);
        var reopened = Project();
        var reloaded = NewSidecar();
        await reloaded.LoadAsync(reopened);
        await reloaded.SaveAsync(reopened);
        File.ReadAllText(SidecarPath).Should().Contain(stored.GetProperty("hash").GetString()!);
        var next = NewBuild(reloaded);
        await next.RunWithVb6Async(reopened);
        await Asked(0);
        next.Starts.Should().Be(1);
    }

    [Fact]
    public async Task Make_without_output_asks_nothing()
    {
        (await build.MakeWithVb6Async(project)).Should().BeTrue();
        await Asked(0);
        build.Builds.Should().Be(1);
    }

    [Fact]
    public async Task Run_twice_asks_nothing_for_its_own_output()
    {
        await sidecar.LoadAsync(project);
        await build.RunWithVb6Async(project);
        await build.RunWithVb6Async(project);
        await Asked(0);
        build.Builds.Should().Be(2);
        build.Starts.Should().Be(2);
    }

    [Fact]
    public async Task Sidecar_keeps_unknown_keys()
    {
        File.WriteAllText(SidecarPath, """{"version":3,"future":{"keep":42}}""");
        await sidecar.LoadAsync(project);
        await build.RunWithVb6Async(project);
        await sidecar.SaveAsync(project);
        using var json = JsonDocument.Parse(File.ReadAllText(SidecarPath));
        json.RootElement.GetProperty("future").GetProperty("keep").GetInt32().Should().Be(42);
    }

    [Fact]
    public async Task Run_Yes_asks_only_once_and_builds_and_starts()
    {
        File.WriteAllText(ExePath, "original");
        await build.RunWithVb6Async(project);
        await Asked(1);
        build.Builds.Should().Be(1);
        build.Starts.Should().Be(1);
    }

    [Fact]
    public async Task Make_still_asks_for_the_last_Run_output()
    {
        await sidecar.LoadAsync(project);
        await build.RunWithVb6Async(project);
        Answer(MessageBoxResult.No);
        (await build.MakeWithVb6Async(project)).Should().BeFalse();
        await Asked(1);
        build.Builds.Should().Be(1);
    }

    [Theory]
    [InlineData("Different.exe")]
    [InlineData("")]
    [InlineData(null)]
    public async Task Run_same_hash_without_the_recorded_filename_asks(string? fileName)
    {
        File.WriteAllText(ExePath, "original");
        File.WriteAllText(SidecarPath, JsonSerializer.Serialize(new
        {
            version = 3,
            lastRunExe = new { fileName, hash = Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(ExePath))) },
        }));
        await sidecar.LoadAsync(project);
        Answer(MessageBoxResult.No);
        await build.RunWithVb6Async(project);
        await Asked(1);
        build.Builds.Should().Be(0);
        build.Starts.Should().Be(0);
    }

    [Fact]
    public async Task Failed_Run_does_not_record_ownership()
    {
        build.ExitCode = 1;
        await build.RunWithVb6Async(project);
        build.Starts.Should().Be(0);
        File.Exists(SidecarPath).Should().BeFalse();
    }

    // Output locations measured in oracle PR #4756, recorded in issue #4538.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Make_Path32_checks_the_output_folder(bool staleBesideProject)
    {
        var output = Path.Combine(directory, "out", "Output.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        project.UnknownPreSectionLines.Add((1, "Path32=\"out\""));
        File.WriteAllText(staleBesideProject ? ExePath : output, "original");
        var compiler = NewBuild(sidecar, output);
        (await compiler.MakeWithVb6Async(project)).Should().BeTrue();
        await Asked(staleBesideProject ? 0 : 1);
        if (staleBesideProject) File.ReadAllText(ExePath).Should().Be("original");
    }

    [Fact]
    public async Task Run_Path32_starts_and_records_the_output_in_that_folder()
    {
        var output = Path.Combine(directory, "out", "Output.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(output)!);
        project.UnknownPreSectionLines.Add((1, "Path32=\"out\""));
        File.WriteAllText(ExePath, "stale");
        await sidecar.LoadAsync(project);
        var compiler = NewBuild(sidecar, output);
        await compiler.RunWithVb6Async(project);
        compiler.Starts.Should().Be(1);
        await Asked(0);
        sidecar.GetLastRunExe(project)!.Hash.Should().Be(
            Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(output))));
        File.ReadAllText(ExePath).Should().Be("stale");
    }

    [Fact]
    public async Task Failed_build_with_missing_Path32_creates_no_folder_or_Run_record()
    {
        project.UnknownPreSectionLines.Add((1, "Path32=\"out\""));
        var compiler = NewBuild(sidecar, Path.Combine(directory, "out", "Output.exe"));
        compiler.ExitCode = 1;
        await compiler.RunWithVb6Async(project);
        compiler.OutputDirectory.Should().BeNull();
        Directory.Exists(Path.Combine(directory, "out")).Should().BeFalse();
        compiler.Starts.Should().Be(0);
        sidecar.GetLastRunExe(project).Should().BeNull();
    }

    [Fact]
    public async Task Path32_without_ExeName32_Make_and_Run_use_the_project_name_beside_the_project()
    {
        project.UnknownPreSectionLines.Clear();
        project.UnknownPreSectionLines.Add((0, "Path32=\"out\""));
        var output = Path.Combine(directory, project.Name + ".exe");
        File.WriteAllText(output, "original");
        var compiler = NewBuild(sidecar, output);
        (await compiler.MakeWithVb6Async(project)).Should().BeTrue();
        await compiler.RunWithVb6Async(project);
        compiler.Starts.Should().Be(1);
        await windows.Received(2).MessageBox(
            project.Name + ".exe already exists. Do you want to replace it?", null,
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, null, MessageBoxLayout.SecondButtonDefault);
        Directory.Exists(Path.Combine(directory, "out")).Should().BeFalse();
    }

    [WindowsOnlyFact]
    public async Task Absolute_Path32_Make_and_Run_use_that_folder()
    {
        var outputDirectory = Path.Combine(directory, "absolute");
        Directory.CreateDirectory(outputDirectory);
        var output = Path.Combine(outputDirectory, "Output.exe");
        project.UnknownPreSectionLines.Add((1, $"Path32=\"{outputDirectory}\""));
        File.WriteAllText(output, "original");
        var compiler = NewBuild(sidecar, output);
        (await compiler.MakeWithVb6Async(project)).Should().BeTrue();
        await compiler.RunWithVb6Async(project);
        await Asked(2);
        compiler.Starts.Should().Be(1);
    }

    // Staging policy and /outdir measurements: issue #4757 and oracle PR #4756.
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Staged_failure_or_timeout_keeps_old_output(bool timeout)
    {
        File.WriteAllText(ExePath, "original");
        build.ExitCode = 1;
        build.Timeout = timeout;
        build.WriteOnFailure = true;
        (await build.MakeWithVb6Async(project)).Should().BeFalse();
        File.ReadAllText(ExePath).Should().Be("original");
        Directory.GetDirectories(directory).Should().BeEmpty();
    }

    [Fact]
    public async Task Staged_success_swaps_output_from_a_unique_child_folder()
    {
        File.WriteAllText(ExePath, "original");
        (await build.MakeWithVb6Async(project)).Should().BeTrue();
        build.OutputDirectory.Should().NotBeNull();
        Path.GetDirectoryName(build.OutputDirectory).Should().Be(directory);
        File.ReadAllText(ExePath).Should().Be("built by RunVb6Async 1");
        Directory.Exists(build.OutputDirectory).Should().BeFalse();
        var first = build.OutputDirectory;
        (await build.MakeWithVb6Async(project)).Should().BeTrue();
        build.OutputDirectory.Should().NotBe(first);
        Directory.GetDirectories(directory).Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Ineligible_project_builds_in_place(bool dll)
    {
        if (dll) project.ProjectType = VBProjectType.OleDll;
        else
        {
            project.UnknownPreSectionLines.Add((1, "AutoIncrementVer=1"));
            File.AppendAllText(project.AbsolutePath!, "AutoIncrementVer=1\r\n");
        }
        File.WriteAllText(ExePath, "original");
        (await build.MakeWithVb6Async(project)).Should().BeTrue();
        build.OutputDirectory.Should().BeNull();
        Directory.GetDirectories(directory).Should().BeEmpty();
        File.ReadAllText(ExePath).Should().Be("built by RunVb6Async 1");
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task Staged_build_repairs_only_Path32(bool hadPath, bool failed)
    {
        var original = "Type=Exe\r\n" + (hadPath ? "  Path32 = \".\"\r\n" : "") + "Name=Before\r\n";
        // Capture after pending editor changes have been applied, not before the save event.
        events.When(e => e.Publish(Arg.Any<ApplyAllUnsavedChangesEvent>()))
            .Do(_ => File.WriteAllText(project.AbsolutePath!, original));
        build.ExitCode = failed ? 1 : 0;
        build.DuringBuild = path => File.WriteAllText(path,
            "Type=Exe\r\nPath32=\"..\\x\"\r\nName=After\r\nRevisionVer=9\r\n");
        (await build.MakeWithVb6Async(project)).Should().Be(!failed);
        File.ReadAllText(project.AbsolutePath!).ReplaceLineEndings("\n").Should().Be(
            ("Type=Exe\n" + (hadPath ? "  Path32 = \".\"\n" : "") + "Name=After\nRevisionVer=9\n"));
        Directory.GetDirectories(directory).Should().BeEmpty();
    }

    [Fact]
    public async Task Staged_swap_failure_reports_error_and_cleans_up()
    {
        strings.GetString("Str.Vb6Toolchain.CompilationFailed").Returns("Compilation failed: {0}");
        // A directory at the destination refuses File.Move on Linux as well as Windows.
        Directory.CreateDirectory(ExePath);
        File.WriteAllText(Path.Combine(ExePath, "original"), "old bytes");
        (await build.MakeWithVb6Async(project)).Should().BeFalse();
        File.ReadAllText(Path.Combine(ExePath, "original")).Should().Be("old bytes");
        Directory.GetDirectories(directory).Should().Equal(ExePath);
        // Windows' own text names no file, so the message names the destination itself.
        await windows.Received(1).MessageBox(
            Arg.Is<string>(message => message.StartsWith("Compilation failed: " + ExePath + ": ")),
            "Str.Vb6Toolchain.BuildFailedTitle", MessageBoxButtons.Ok, MessageBoxIcon.Error);
    }

    private void InPlace(bool dll)
    {
        if (dll) project.ProjectType = VBProjectType.OleDll;
        else File.AppendAllText(project.AbsolutePath!, "AutoIncrementVer=1\r\n");
        strings.GetString("Str.Vb6Toolchain.PreviousOutputKept").Returns("{0} may be damaged. Its previous version is kept as {1}.");
    }

    // Recovery is HexIDE policy (#4952), not a claim about interrupted VB6 writes.
    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, 0)]
    [InlineData(false, 1)]
    [InlineData(true, 1)]
    [InlineData(false, 2)]
    [InlineData(true, 2)]
    [InlineData(false, 3)]
    [InlineData(true, 3)]
    [InlineData(false, 4)]
    [InlineData(true, 4)]
    public async Task In_place_build_preserves_previous_output(bool dll, int result)
    {
        InPlace(dll);
        File.WriteAllText(ExePath, "original");
        var earlier = ExePath + ".earlier.recovery";
        File.WriteAllText(earlier, "earlier");
        build.ExitCode = result == 0 ? 0 : 1;
        build.Timeout = result == 2;
        build.WriteOnFailure = result is 1 or 2;
        build.DuringBuild = _ =>
        {
            var copies = Directory.GetFiles(directory, "*.recovery").Where(p => p != earlier).ToArray();
            copies.Should().ContainSingle();
            File.ReadAllText(copies[0]).Should().Be("original");
            if (result == 4) File.Delete(ExePath);
        };
        (await build.MakeWithVb6Async(project)).Should().Be(result == 0);
        var kept = Directory.GetFiles(directory, "*.recovery").Where(p => p != earlier).ToArray();
        var damaged = result is 1 or 2 or 4;
        if (dll && damaged)
        {
            kept.Should().ContainSingle();
            File.ReadAllText(kept[0]).Should().Be("original");
            await windows.Received(1).MessageBox(
                ExePath + " may be damaged. Its previous version is kept as " + kept[0] + ".",
                "Str.Vb6Toolchain.BuildFailedTitle", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
        }
        else
        {
            kept.Should().BeEmpty();
            windows.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
                .Should().NotContain(call => ((string)call.GetArguments()[0]!).Contains("may be damaged"));
        }
        if (dll && damaged)
        {
            var messages = windows.ReceivedCalls().Where(call => call.GetMethodInfo().Name == nameof(IWindowManager.MessageBox))
                .Select(call => call.GetArguments()).ToArray();
            messages[^1][0].Should().Be(ExePath + " may be damaged. Its previous version is kept as " + kept[0] + ".");
            messages[^2][3].Should().Be(MessageBoxIcon.Error);
        }
        if (result != 4 || !dll)
            File.ReadAllText(ExePath).Should().Be(result == 0 || (dll && damaged) ? "built by RunVb6Async 1" : "original");
        File.ReadAllText(earlier).Should().Be("earlier");
    }

    [Fact]
    public async Task In_place_refused_restore_keeps_and_names_copy()
    {
        InPlace(false);
        File.WriteAllText(ExePath, "original");
        build.ExitCode = 1;
        build.DuringBuild = _ =>
        {
            File.Delete(ExePath);
            Directory.CreateDirectory(ExePath); // Refuses replacement on Linux and Windows.
        };
        (await build.MakeWithVb6Async(project)).Should().BeFalse();
        var copy = Directory.GetFiles(directory, "*.recovery").Should().ContainSingle().Subject;
        File.ReadAllText(copy).Should().Be("original");
        await windows.Received(1).MessageBox(
            ExePath + " may be damaged. Its previous version is kept as " + copy + ".",
            "Str.Vb6Toolchain.BuildFailedTitle", MessageBoxButtons.Ok, MessageBoxIcon.Warning);
    }

    [Fact]
    public async Task In_place_success_with_refused_flush_still_succeeds_and_removes_copy()
    {
        InPlace(false);
        File.WriteAllText(ExePath, "original");
        build.DuringBuild = _ => Directory.GetFiles(directory, "*.recovery").Should().ContainSingle();
        build.AfterBuild = () =>
        {
            File.Delete(ExePath);
            Directory.CreateDirectory(ExePath);
        };
        (await build.MakeWithVb6Async(project)).Should().BeTrue();
        Directory.GetFiles(directory, "*.recovery").Should().BeEmpty();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task In_place_first_build_makes_no_recovery_copy(bool dll)
    {
        InPlace(dll);
        build.DuringBuild = _ => Directory.GetFiles(directory, "*.recovery").Should().BeEmpty();
        (await build.MakeWithVb6Async(project)).Should().BeTrue();
        File.ReadAllText(ExePath).Should().Be("built by RunVb6Async 1");
        Directory.GetFiles(directory, "*.recovery").Should().BeEmpty();
    }

    // A build that throws rather than reporting is settled as a failed one: the copy stays only where it's needed.
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task In_place_build_that_throws_settles_the_copy(bool dll, bool wrote)
    {
        InPlace(dll);
        File.WriteAllText(ExePath, "original");
        build.DuringBuild = _ =>
        {
            if (wrote) File.WriteAllText(ExePath, "partial");
            throw new InvalidOperationException("the compiler could not start");
        };
        Func<Task> make = () => build.MakeWithVb6Async(project);
        await make.Should().ThrowAsync<InvalidOperationException>();
        var kept = Directory.GetFiles(directory, "*.recovery");
        if (dll && wrote)
        {
            kept.Should().ContainSingle();
            File.ReadAllText(kept[0]).Should().Be("original");
            File.ReadAllText(ExePath).Should().Be("partial");
        }
        else
        {
            kept.Should().BeEmpty();
            File.ReadAllText(ExePath).Should().Be("original");
        }
    }

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
