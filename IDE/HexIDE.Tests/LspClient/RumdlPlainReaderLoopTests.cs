using System.Diagnostics;
using System.Text;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// DRAFT ONLY, NOT FOR MERGE (#390). Starts rumdl many times with no HexIDE code between the test and the pipe, to
/// tell a stall in rumdl from one in the reader. The client's record shows rumdl logging its whole initialize handler
/// and then no answer reaching our read. If a plain reader on the same runner never misses an answer, the reader is
/// ours to look at; if it misses at a similar rate, the stall is rumdl's.
/// </summary>
public class RumdlPlainReaderLoopTests
{
    private const int Starts = 300;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);

    private static readonly byte[] Frame = BuildFrame();

    private static byte[] BuildFrame()
    {
        // The shape rumdl logged for our client: no root, and a client that pulls diagnostics.
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"processId\":"
                 + Environment.ProcessId
                 + ",\"rootUri\":null,\"capabilities\":{\"textDocument\":{\"diagnostic\":{\"dynamicRegistration\":false},\"synchronization\":{}}}}}";
        var bytes = Encoding.UTF8.GetBytes(body);
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"), .. bytes];
    }

    [Fact]
    public async Task A_bare_dotnet_reader_gets_every_answer()
    {
        var rumdl = ForeignServer.Markdown.Find()!;
        var missed = new List<int>();
        var times = new List<double>();
        for (var i = 0; i < Starts; i++)
        {
            var psi = new ProcessStartInfo(rumdl)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true,
                UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
            };
            psi.Arguments = ForeignServer.Markdown.ServerArguments;
            using var p = Process.Start(psi)!;
            var drainErr = p.StandardError.BaseStream.CopyToAsync(Stream.Null, TestContext.Current.CancellationToken);
            var clock = Stopwatch.StartNew();
            var input = p.StandardInput.BaseStream;
            await input.WriteAsync(Frame, TestContext.Current.CancellationToken);
            await input.FlushAsync(TestContext.Current.CancellationToken);

            var buffer = new byte[64];
            var read = p.StandardOutput.BaseStream.ReadAsync(buffer, TestContext.Current.CancellationToken).AsTask();
            if (await Task.WhenAny(read, Task.Delay(Wait, TestContext.Current.CancellationToken)) == read && await read > 0)
                times.Add(clock.Elapsed.TotalMilliseconds);
            else
                missed.Add(i);
            try { p.Kill(); } catch { /* already gone */ }
            await p.WaitForExitAsync(TestContext.Current.CancellationToken);
            try { await drainErr; } catch { /* pipe closed */ }
        }

        times.Sort();
        var summary = $"bare .NET reader: {missed.Count} of {Starts} starts unanswered within {Wait.TotalSeconds:0}s "
                    + $"[{string.Join(",", missed.Take(10))}]; answered median {(times.Count > 0 ? times[times.Count / 2] : 0):0}ms, "
                    + $"max {(times.Count > 0 ? times[^1] : 0):0}ms";
        TestContext.Current.SendDiagnosticMessage(summary);
        missed.Should().BeEmpty(summary);
    }

    [Fact]
    public async Task A_python_reader_gets_every_answer()
    {
        var rumdl = ForeignServer.Markdown.Find()!;
        var script = Path.Combine(Path.GetTempPath(), $"rumdl_many_{Environment.ProcessId}.py");
        await File.WriteAllTextAsync(script, """
            import json, os, subprocess, sys, threading, time
            exe, cwd, runs, timeout, frame_hex = sys.argv[1], sys.argv[2], int(sys.argv[3]), float(sys.argv[4]), sys.argv[5]
            frame = bytes.fromhex(frame_hex)
            times, hung = [], []
            for i in range(runs):
                proc = subprocess.Popen([exe, "server"], cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.DEVNULL)
                start = time.perf_counter()
                proc.stdin.write(frame)
                proc.stdin.flush()
                got = {}
                def read():
                    if proc.stdout.readline():
                        got["at"] = time.perf_counter()
                t = threading.Thread(target=read, daemon=True)
                t.start()
                t.join(timeout)
                if "at" in got:
                    times.append((got["at"] - start) * 1000)
                else:
                    hung.append(i)
                proc.kill()
                proc.wait()
            times.sort()
            med = times[len(times) // 2] if times else 0
            mx = times[-1] if times else 0
            print(f"python reader: {len(hung)} of {runs} starts unanswered within {timeout:.0f}s {hung[:10]}; answered median {med:.0f}ms, max {mx:.0f}ms")
            """, TestContext.Current.CancellationToken);

        var psi = new ProcessStartInfo(OperatingSystem.IsWindows() ? "python" : "python3")
        {
            RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        foreach (var a in new[] { "-I", script, rumdl, Path.GetTempPath(), Starts.ToString(), Wait.TotalSeconds.ToString("0"), Convert.ToHexString(Frame) })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
        var stderr = p.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
        await p.WaitForExitAsync(TestContext.Current.CancellationToken);
        var summary = (await stdout).Trim() + " " + (await stderr).Trim();
        TestContext.Current.SendDiagnosticMessage(summary);
        summary.Should().StartWith("python reader: 0 of", summary);
    }
}
