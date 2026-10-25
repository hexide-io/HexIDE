using System.Runtime.CompilerServices;

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
