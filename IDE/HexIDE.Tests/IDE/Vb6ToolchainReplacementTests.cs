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
        return result;
    }

    private UserSidecarService NewSidecar() => new(
        Substitute.For<IBookmarkService>(), Substitute.For<IBreakpointService>(), projects);

    private void Answer(MessageBoxResult answer) => windows.MessageBox(
        Arg.Any<string>(), Arg.Any<string?>(), MessageBoxButtons.YesNo, MessageBoxIcon.Warning,
        Arg.Any<Avalonia.Controls.Window?>(), Arg.Any<bool>(), Arg.Any<MessageBoxLayout>()).Returns(answer);

    private Task Asked(int count) => windows.Received(count).MessageBox(
        "Output.exe already exists. Do you want to replace it?", null,
        MessageBoxButtons.YesNo, MessageBoxIcon.Warning, null, false, MessageBoxLayout.SecondButtonDefault);

    private sealed class Build(IWindowManager windows, IEventBus events, ILocalizationService strings,
        IUserSidecarService sidecar, string exePath)
        : Vb6ToolchainService(Substitute.For<ILspClient>(), events, windows, strings,
            new CodeTrust(windows, strings, _ => false), sidecar)
    {
        public int Builds { get; private set; }
        public int Starts { get; private set; }
        public int ExitCode { get; set; }
        internal override Task<Vb6BuildOutcome?> RunVb6Async(string projectPath)
        {
            Builds++;
            if (ExitCode == 0) File.WriteAllText(exePath, "built by RunVb6Async " + Builds);
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
            MessageBoxButtons.YesNo, MessageBoxIcon.Warning, null, false, MessageBoxLayout.SecondButtonDefault);
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

    public void Dispose() => Directory.Delete(directory, recursive: true);
}
