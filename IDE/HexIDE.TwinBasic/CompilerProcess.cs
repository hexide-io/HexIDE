using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace HexIDE.TwinBasic;

public enum CompilerArchitecture { Win32, Win64 }

/// <summary>
/// A compiler started without the twinBASIC IDE (#1689). The IDE starts <c>bin\twinBASIC_win32.exe --ide=PID</c> naming
/// itself, and this starts it the same way naming this process. The flag is a watchdog: the compiler ended within a
/// second of the process it names (measured, BETA 1024). Its first 42 bytes on standard output are a little-endian
/// 32-bit port, on which it listens at 127.0.0.1, and then a braced GUID.
/// <para>
/// <b>The GUID is not the root socket's pass key.</b> <c>/{GUID}/root</c> was refused with 404, which the compiler's
/// "404 bad passkey" names, under every spelling tried. How the IDE gives the compiler its key is not yet known, so this
/// starts a compiler but cannot yet connect to one.
/// </para>
/// Disposal ends the compiler and waits for it, since a live compiler holds its staged project open on Windows (#1204).
/// </summary>
public sealed partial class CompilerProcess : IDisposable
{
    internal const int EndpointBytes = 4 + 38;

    private readonly Process process;
    private readonly Task drained;
    private int disposed;

    public int ProcessId { get; }

    public int Port { get; }

    /// <summary>The GUID the compiler reported after its port. What it identifies is not yet known (#1689).</summary>
    public string ReportedGuid { get; }

    private CompilerProcess(Process process, int port, string reportedGuid, Task drained)
    {
        this.process = process;
        this.drained = drained;
        ProcessId = process.Id;
        Port = port;
        ReportedGuid = reportedGuid;
    }

    /// <summary>The compiler executable an install folder holds, the one holding <c>twinBASIC.exe</c>.</summary>
    public static string ExecutableIn(string installFolder, CompilerArchitecture architecture) =>
        Path.Combine(installFolder, "bin", architecture == CompilerArchitecture.Win64 ? "twinBASIC_win64.exe" : "twinBASIC_win32.exe");

    public static async Task<CompilerProcess> StartAsync(string installFolder,
        CompilerArchitecture architecture = CompilerArchitecture.Win32, TimeSpan? timeout = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(installFolder);
        var executable = ExecutableIn(Path.GetFullPath(installFolder), architecture);
        if (!File.Exists(executable)) throw new FileNotFoundException("No twinBASIC compiler executable in that install folder.", executable);

        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, CreateNoWindow = true,
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
            WorkingDirectory = Path.GetDirectoryName(executable)!,
        };
        start.ArgumentList.Add($"--ide={Environment.ProcessId}");
        var process = Process.Start(start) ?? throw new IOException("The twinBASIC compiler did not start.");

        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout ?? TimeSpan.FromSeconds(30));
        try
        {
            var (port, key) = await ReadEndpointAsync(process.StandardOutput.BaseStream, deadline.Token).ConfigureAwait(false);
            // Anything it writes afterwards is read and dropped, so a full pipe can never stall it.
            var drained = Task.WhenAll(process.StandardOutput.BaseStream.CopyToAsync(Stream.Null),
                process.StandardError.BaseStream.CopyToAsync(Stream.Null));
            return new CompilerProcess(process, port, key, drained);
        }
        catch (Exception e)
        {
            var exited = process.HasExited ? $" It exited with code {process.ExitCode}." : "";
            End(process);
            process.Dispose();
            if (e is OperationCanceledException && cancellationToken.IsCancellationRequested)
                throw new OperationCanceledException(cancellationToken);
            if (e is OperationCanceledException) throw new TimeoutException("The twinBASIC compiler did not report its endpoint." + exited);
            if (e is EndOfStreamException) throw new IOException("The twinBASIC compiler closed its output before reporting its endpoint." + exited, e);
            throw;
        }
    }

    /// <summary>Reads the endpoint record. The GUID is never put in an exception, in case it proves to be a secret.</summary>
    internal static async Task<(int Port, string Key)> ReadEndpointAsync(Stream output, CancellationToken cancellationToken)
    {
        var record = new byte[EndpointBytes];
        await output.ReadExactlyAsync(record, cancellationToken).ConfigureAwait(false);
        var port = BinaryPrimitives.ReadUInt32LittleEndian(record);
        if (port is 0 or > 65535) throw new InvalidDataException("The twinBASIC compiler reported an invalid port.");
        var key = Encoding.ASCII.GetString(record, 4, 38);
        if (!Key().IsMatch(key)) throw new InvalidDataException("The twinBASIC compiler reported a key that is not a braced GUID.");
        return ((int)port, key);
    }

    [GeneratedRegex(@"^\{[0-9A-Fa-f]{8}(-[0-9A-Fa-f]{4}){3}-[0-9A-Fa-f]{12}\}$")]
    private static partial Regex Key();

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
        try { drained.Wait(TimeSpan.FromSeconds(5)); } catch (AggregateException) { /* the pipes closed with it */ }
        process.Dispose();
    }
}
