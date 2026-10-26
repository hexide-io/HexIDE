using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using HexIDE.Processes;

namespace HexIDE.TwinBasic;

public enum CompilerArchitecture { Win32, Win64 }

/// <summary>
/// Which of an install's compiler executables a project needs. A project that is not DEP-aware needs a <c>_noDEP</c>
/// one, and a 32-bit project that is large-address-aware needs an <c>_LAA</c> one; given the wrong one, loading the
/// project answers -4, -5 or -6 (<see cref="CompilerProjectStatusException"/>). There is no 64-bit <c>_LAA</c>.
/// </summary>
public sealed record CompilerStub(CompilerArchitecture Architecture = CompilerArchitecture.Win32, bool DepAware = true,
    bool LargeAddressAware = false)
{
    public string FileName =>
        (Architecture == CompilerArchitecture.Win64 ? "twinBASIC_win64" : "twinBASIC_win32")
        + (DepAware ? "" : "_noDEP")
        + (Architecture == CompilerArchitecture.Win32 && LargeAddressAware ? "_LAA" : "")
        + ".exe";

    /// <summary>
    /// The stub a project's own <c>Settings</c> call for: DEP-aware only where <c>project.depAware</c> is <c>true</c>, and
    /// large-address-aware only where <c>project.largeAddressAware</c> is. A project with neither setting, or with no
    /// <c>Settings</c> at all, needs <c>_noDEP</c>: the impexp fixture loads on no other stub (measured, BETA 1024).
    /// </summary>
    public static CompilerStub ForProject(TwinBasicContainer project, CompilerArchitecture architecture = CompilerArchitecture.Win32)
    {
        ArgumentNullException.ThrowIfNull(project);
        var settings = project.Root.Children.OfType<TwinBasicFile>().FirstOrDefault(f => f.Name == "Settings");
        if (settings is null) return new(architecture, DepAware: false);
        try
        {
            using var json = System.Text.Json.JsonDocument.Parse(settings.Content.AsMemory(),
                new() { AllowTrailingCommas = true, CommentHandling = System.Text.Json.JsonCommentHandling.Skip, MaxDepth = 64 });
            bool Flag(string name) => json.RootElement.ValueKind == System.Text.Json.JsonValueKind.Object
                && json.RootElement.TryGetProperty(name, out var value) && value.ValueKind == System.Text.Json.JsonValueKind.True;
            return new(architecture, Flag("project.depAware"), Flag("project.largeAddressAware"));
        }
        catch (System.Text.Json.JsonException) { throw new InvalidDataException("The project's Settings are not JSON."); }
    }
}

/// <summary>
/// A compiler started without the twinBASIC IDE or its native host (#1689), as <c>bin\STUB.exe --compiler=0</c> with
/// <c>bin\</c> as its working directory. Its first 42 bytes on standard output are a little-endian 32-bit port, on which
/// it listens at 127.0.0.1, and then a braced GUID, which is the key every one of its sockets is opened under:
/// <c>ws://127.0.0.1:PORT/KEY/root</c> (measured, BETA 1024).
/// <para>
/// <c>--ide=PID</c> is a different mode, which the comment on #1689 of 2026-10-24 took for this one. It starts the page
/// server of the IDE's front end, whose key is for its pages, and it ends when the process it names does. In this mode
/// nothing watches the parent, so the compiler is held in a <see cref="ChildProcessJob"/>, which ends it with HexIDE.
/// </para>
/// Disposal ends the compiler and waits for it, since a live compiler holds its staged project open on Windows (#1204).
/// To end it cleanly first, ask the root to shut down (<see cref="CompilerRootSession.ShutdownAsync"/>) and then
/// <see cref="WaitForExitAsync"/>.
/// </summary>
public sealed partial class CompilerProcess : IDisposable
{
    internal const int EndpointBytes = 4 + 38;

    private readonly Process process;
    private readonly ChildProcessJob job;
    private readonly Task drained;
    private int disposed;

    public int ProcessId { get; }

    public int Port { get; }

    /// <summary>The key the compiler's sockets are opened under. Never put in a message or a log: it admits a client.</summary>
    internal string Key { get; }

    /// <summary>The root socket's address. It holds the key, so it is not for a message or a log either.</summary>
    public Uri RootEndpoint => RootEndpointFor(Port, Key);

    internal static Uri RootEndpointFor(int port, string key) => new($"ws://127.0.0.1:{port}/{key}/root");

    private CompilerProcess(Process process, ChildProcessJob job, int port, string key, Task drained)
    {
        this.process = process;
        this.job = job;
        this.drained = drained;
        ProcessId = process.Id;
        Port = port;
        Key = key;
    }

    /// <summary>The compiler executable an install folder holds, the folder holding <c>twinBASIC.exe</c>.</summary>
    public static string ExecutableIn(string installFolder, CompilerStub? stub = null) =>
        Path.Combine(installFolder, "bin", (stub ?? new()).FileName);

    /// <summary>
    /// The package list a compiler's initialization carries: the names of the folders under the install's
    /// <c>packages\</c>, in ordinal order.
    /// </summary>
    public static IReadOnlyList<string> PackageNamesIn(string installFolder) =>
        [.. Directory.EnumerateDirectories(Path.Combine(installFolder, "packages")).Select(Path.GetFileName).OfType<string>()
            .Order(StringComparer.Ordinal)];

    public static async Task<CompilerProcess> StartAsync(string installFolder, CompilerStub? stub = null,
        TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installFolder);
        var executable = ExecutableIn(Path.GetFullPath(installFolder), stub);
        if (!File.Exists(executable)) throw new FileNotFoundException("No such twinBASIC compiler executable in that install folder.", executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        start.ArgumentList.Add("--compiler=0");
        var job = new ChildProcessJob();
        Process process;
        try
        {
            process = Process.Start(start) ?? throw new IOException("The twinBASIC compiler did not start.");
        }
        catch
        {
            job.Dispose();
            throw;
        }

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            job.Assign(process);
            // Nothing is ever written to it: standard input is the null device to the compiler.
            process.StandardInput.Close();
            var (port, key) = await ReadEndpointAsync(process.StandardOutput.BaseStream, deadline.Token).ConfigureAwait(false);
            // Anything it writes afterwards is read and dropped, so a full pipe can never stall it.
            var drained = Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(Stream.Null),
                process.StandardError.BaseStream.CopyToAsync(Stream.Null));
            return new CompilerProcess(process, job, port, key, drained);
        }
        catch (Exception e)
        {
            var exited = process.HasExited ? $" It exited with code {process.ExitCode}." : "";
            End(process);
            job.Dispose();
            process.Dispose();
            if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (e is OperationCanceledException) throw new TimeoutException("The twinBASIC compiler did not report its endpoint." + exited);
            if (e is EndOfStreamException) throw new IOException("The twinBASIC compiler closed its output before reporting its endpoint." + exited, e);
            throw;
        }
    }

    /// <summary>Reads the endpoint record. The key is never put in an exception, since it admits a client.</summary>
    internal static async Task<(int Port, string Key)> ReadEndpointAsync(Stream output, CancellationToken cancellationToken)
    {
        var record = new byte[EndpointBytes];
        await output.ReadExactlyAsync(record, cancellationToken).ConfigureAwait(false);
        var port = BinaryPrimitives.ReadUInt32LittleEndian(record);
        if (port is 0 or > 65535) throw new InvalidDataException("The twinBASIC compiler reported an invalid port.");
        var key = Encoding.ASCII.GetString(record, 4, 38);
        if (!KeyPattern().IsMatch(key)) throw new InvalidDataException("The twinBASIC compiler reported a key that is not a braced GUID.");
        return ((int)port, key);
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex KeyPattern();

    /// <summary>Waits for the compiler to end, as it does after a shutdown request, and answers its exit code.</summary>
    public async Task<int> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken = default)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try { await process.WaitForExitAsync(deadline.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new TimeoutException("The twinBASIC compiler did not end in time."); }
        return process.ExitCode;
    }

    private static void End(Process process)
    {
        try
        {
            if (!process.HasExited) process.Kill(entireProcessTree: true);
            process.WaitForExit(10_000);
        }
        catch (InvalidOperationException) { /* never started, or already reaped */ }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0) return;
        End(process);
        job.Dispose();
        try { drained.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { /* the pipes closed with it */ }
        process.Dispose();
    }
}
