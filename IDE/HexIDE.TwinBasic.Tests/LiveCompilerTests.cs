using System.Runtime.CompilerServices;
using System.Text;

namespace HexIDE.TwinBasic.Tests;

/// <summary>
/// The adapter against a real compiler, started without the IDE or its native host (#1689). Opt-in: set
/// <c>HEXIDE_TWINBASIC_IDE</c> to an install folder, the one holding <c>twinBASIC.exe</c>. CI has no twinBASIC, so these
/// skip there, visibly. The Community path: no licence key is read or sent.
/// </summary>
public sealed class LiveCompilerTests(ITestOutputHelper output)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [LiveCompilerFact]
    public async Task A_compiler_started_without_the_IDE_loads_a_project_attaches_every_channel_and_shuts_down()
    {
        var install = LiveCompilerFactAttribute.Install!;
        var stage = Directory.CreateTempSubdirectory("hexide-tb-live-");
        var project = Path.Combine(stage.FullName, "impexp.twinproj");
        File.Copy(Path.Combine(AppContext.BaseDirectory, "Fixtures", "impexp.twinproj"), project);
        var original = File.ReadAllBytes(project);
        var packages = new LoggingPackageProvider(Path.Combine(install, "packages"));
        var language = CompilerLanguagePack.ToLanguageData(
            CompilerLanguagePack.Read(await File.ReadAllTextAsync(CompilerLanguagePack.PathIn(install), Token)));

        TwinBasicContainer container;
        await using (var file = File.OpenRead(project)) container = TwinBasicContainerReader.Read(file, cancellationToken: Token);
        var stub = CompilerStub.ForProject(container);
        output.WriteLine($"stub: {stub.FileName}");

        var compiler = await CompilerProcess.StartAsync(install, stub, cancellationToken: Token);
        CompilerPreparedProject? prepared = null;
        CompilerChannelSession? channels = null;
        try
        {
            var root = await CompilerRootSession.ConnectAsync(compiler.RootEndpoint,
                new CompilerRootInitialization(CompilerProcess.PackageNamesIn(install), language), cancellationToken: Token);
            root.Connection.ProcessId.Should().Be(compiler.ProcessId, "the root names the process this started");
            output.WriteLine($"initialized: {root.Connection.InitializationNotifications.Length} notification(s)");

            prepared = await CompilerProjectPreparation.PrepareAsync(root, project, packages, cancellationToken: Token);
            output.WriteLine($"prepared: {prepared.Notifications.Length} notification(s); packages asked for: "
                             + string.Join(", ", packages.Asked.Select(a => $"{a.Id} ({(a.Found ? "found" : "missing")})")));

            channels = await CompilerChannelSession.AttachToPreparedProjectAsync(compiler.RootEndpoint, root, cancellationToken: Token);
            using var quiet = CancellationTokenSource.CreateLinkedTokenSource(Token);
            quiet.CancelAfter(TimeSpan.FromSeconds(10));
            var received = new List<CompilerChannelMessage>();
            try { while (true) received.Add(await channels.ReceiveAsync(quiet.Token)); }
            catch (OperationCanceledException) when (!Token.IsCancellationRequested) { /* ten quiet seconds */ }

            foreach (var group in received.GroupBy(m => m.Channel))
                output.WriteLine($"{group.Key}: {group.Count()} message(s), {group.Sum(m => m.ReceivedBytes)} bytes");
            channels.Completion.IsCompleted.Should().BeFalse("every channel is still connected after ten seconds");

            await prepared.RequestShutdownAsync(Token);
            var exitCode = await compiler.WaitForExitAsync(TimeSpan.FromSeconds(10), Token);
            output.WriteLine($"shut down: exit code {exitCode}");
            exitCode.Should().Be(0, "a compiler asked to shut down ends cleanly");
        }
        finally
        {
            channels?.Dispose();
            compiler.Dispose();
            prepared?.Dispose();
        }

        File.ReadAllBytes(project).Should().Equal(original, "the compiler is given a staged copy, never the project itself");
        stage.Delete(recursive: true);
    }

    /// <summary>
    /// Records root initialization and shutdown on this beta (#1218, #1689). Nothing loads a project, since the package
    /// contents that would cross are not ours to check in. Initialization carries the install's package names and its
    /// language pack, which is <c>{language}</c> in the recording for the same reason. A one-entry object in its place
    /// never completes initialization, as an empty one does not (#1200): measured, BETA 1024. The process id, the five
    /// ports and the instance identity are placeholders too. Set <c>HEXIDE_TWINBASIC_RECORDING_DIR</c> to keep the file.
    /// </summary>
    [LiveCompilerFact]
    public async Task Root_initialization_and_shutdown_are_recorded()
    {
        var install = LiveCompilerFactAttribute.Install!;
        var pack = CompilerLanguagePack.Read(await File.ReadAllTextAsync(CompilerLanguagePack.PathIn(install), Token));
        var languageJson = System.Text.Json.JsonSerializer.Serialize(pack);
        using var languageObject = System.Text.Json.JsonDocument.Parse(languageJson);
        var initialization = new CompilerRootInitialization(CompilerProcess.PackageNamesIn(install),
            CompilerWireCodec.ReadLanguageData(languageObject.RootElement));
        var beta = System.Text.RegularExpressions.Regex.Match(Path.GetFileName(Path.TrimEndingDirectorySeparator(install)), @"\d+$").Value;
        if (beta.Length == 0) beta = "unknown";

        using var compiler = await CompilerProcess.StartAsync(install, cancellationToken: Token);
        var raw = new StringWriter();
        var transport = await CompilerWebSocketTransport.ConnectAsync(compiler.RootEndpoint, cancellationToken: Token);
        transport.RecordTo(new CompilerRecording(raw, new(1, beta, "Community", "root-initialization"),
            new CompilerRecordingValues(new Dictionary<string, byte[]>())), "root");
        using var root = await CompilerRootSession.InitializeAsync(transport, initialization, cancellationToken: Token);
        await root.RequestShutdownAsync(Token);
        (await compiler.WaitForExitAsync(TimeSpan.FromSeconds(10), Token)).Should().Be(0);

        var info = root.Connection;
        var redacted = CompilerRecordingTests.Redact(raw.ToString(), new CompilerRecordingValues(new Dictionary<string, byte[]>
        {
            ["{pid}"] = BitConverter.GetBytes(info.ProcessId),
            ["{port:fs}"] = BitConverter.GetBytes(info.FileSystemPort),
            ["{port:debugger}"] = BitConverter.GetBytes(info.DebuggerPort),
            ["{port:language}"] = BitConverter.GetBytes(info.LanguagePort),
            ["{port:formImage}"] = BitConverter.GetBytes(info.FormImagePort),
            ["{port:liveBackup}"] = BitConverter.GetBytes(info.LiveBackupPort),
            ["{instance}"] = Encoding.UTF8.GetBytes(info.ServerId),
            ["{language}"] = Encoding.UTF8.GetBytes(languageJson),
        }));
        redacted.Length.Should().BeLessThan(16 * 1024, "the language pack is a placeholder, not kept");
        foreach (var line in redacted.Split('\n', StringSplitOptions.RemoveEmptyEntries).Skip(1))
        {
            var frame = System.Text.Json.JsonSerializer.Deserialize<CompilerRecordedFrame>(line)!;
            var bytes = Convert.FromBase64String(frame.Payload);
            output.WriteLine($"{frame.Sequence} {frame.Direction} {bytes.Length} bytes: {Convert.ToHexString(bytes.AsSpan(0, Math.Min(bytes.Length, 16)))}");
        }
        redacted.Should().NotContain(info.ServerId, "the instance identity is a placeholder");

        if (Environment.GetEnvironmentVariable("HEXIDE_TWINBASIC_RECORDING_DIR") is { Length: > 0 } keep)
            await File.WriteAllTextAsync(Path.Combine(keep, "root-initialization.jsonl"), redacted, Token);
    }

    /// <summary>Answers with a package's file when the identifier names one under the install's packages folder.</summary>
    private sealed class LoggingPackageProvider(string folder) : ICompilerPackageProvider
    {
        public List<(string Id, bool Found)> Asked { get; } = [];

        public ValueTask<Stream?> OpenReadAsync(string packageId, CancellationToken cancellationToken = default)
        {
            var path = Path.IsPathFullyQualified(packageId) ? packageId : Path.Combine(folder, packageId, "package.twinproj");
            var full = Path.GetFullPath(path);
            var found = full.StartsWith(Path.GetFullPath(folder) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
                        && File.Exists(full);
            lock (Asked) Asked.Add((packageId, found));
            return ValueTask.FromResult<Stream?>(found ? File.OpenRead(full) : null);
        }
    }
}

public sealed class LiveCompilerFactAttribute : FactAttribute
{
    public static string? Install { get; } = Environment.GetEnvironmentVariable("HEXIDE_TWINBASIC_IDE");

    public LiveCompilerFactAttribute([CallerFilePath] string? sourceFilePath = null, [CallerLineNumber] int sourceLineNumber = -1)
        : base(sourceFilePath, sourceLineNumber)
    {
        if (!OperatingSystem.IsWindows()) Skip = "The twinBASIC compiler runs on Windows only.";
        else if (string.IsNullOrEmpty(Install)) Skip = "Set HEXIDE_TWINBASIC_IDE to a twinBASIC install folder to run this against a real compiler.";
        else if (!File.Exists(CompilerProcess.ExecutableIn(Install))) Skip = "HEXIDE_TWINBASIC_IDE names a folder with no bin\\twinBASIC_win32.exe in it.";
    }
}
