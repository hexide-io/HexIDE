using System.Diagnostics;
using System.Text;

// NB: namespace deliberately avoids a `Lsp` segment — see VBLspClientTests.
namespace HexIDE.Tests.LspClient;

/// <summary>
/// DRAFT ONLY, NOT FOR MERGE (#390). Starts rumdl many times with no HexIDE code between the test and the pipe.
/// The first run of this: a bare .NET reader missed 9/300 (macOS) and 5/300 (Linux) while Python missed 0/300 on
/// both. These variants split what differs between the two.
/// </summary>
public class RumdlPlainReaderLoopTests
{
    private const int Starts = 300;
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan Late = TimeSpan.FromSeconds(20);

    private static readonly byte[] Frame = BuildFrame();

    private static byte[] BuildFrame()
    {
        var body = "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"initialize\",\"params\":{\"processId\":"
                 + Environment.ProcessId
                 + ",\"rootUri\":null,\"capabilities\":{\"textDocument\":{\"diagnostic\":{\"dynamicRegistration\":false},\"synchronization\":{}}}}}";
        var bytes = Encoding.UTF8.GetBytes(body);
        return [.. Encoding.ASCII.GetBytes($"Content-Length: {bytes.Length}\r\n\r\n"), .. bytes];
    }

    private static string Python => OperatingSystem.IsWindows() ? "python" : "python3";

    // ── is the child's stdio non-blocking? ────────────────────────────────────────────────────────

    private const string FlagsProbe =
        "import fcntl,os; print(' '.join(f'fd{fd}=' + ('NONBLOCK' if fcntl.fcntl(fd, fcntl.F_GETFL) & os.O_NONBLOCK else 'blocking') for fd in (0,1,2)))";

    [Fact(Skip = "Settled in run 4: blocking on Linux and macOS, from .NET and from Popen")]
    public async Task The_stdio_a_dotnet_child_inherits_is_blocking()
    {
        Assert.SkipWhen(OperatingSystem.IsWindows(), "fcntl is POSIX");
        var ct = TestContext.Current.CancellationToken;

        var psi = new ProcessStartInfo(Python)
        {
            RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false,
        };
        psi.ArgumentList.Add("-c");
        psi.ArgumentList.Add(FlagsProbe);
        using var p = Process.Start(psi)!;
        var fromDotnet = (await p.StandardOutput.ReadToEndAsync(ct)).Trim() + " " + (await p.StandardError.ReadToEndAsync(ct)).Trim();
        await p.WaitForExitAsync(ct);

        var viaPopen = new ProcessStartInfo(Python) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        viaPopen.ArgumentList.Add("-c");
        viaPopen.ArgumentList.Add(
            "import subprocess,sys; r = subprocess.run([sys.executable, '-c', " + System.Text.Json.JsonSerializer.Serialize(FlagsProbe)
          + "], stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=subprocess.PIPE); print(r.stdout.decode().strip(), r.stderr.decode().strip())");
        using var q = Process.Start(viaPopen)!;
        var fromPopen = (await q.StandardOutput.ReadToEndAsync(ct)).Trim() + " " + (await q.StandardError.ReadToEndAsync(ct)).Trim();
        await q.WaitForExitAsync(ct);

        var summary = $"child of .NET Process.Start: [{fromDotnet}]; child of Python Popen: [{fromPopen}]";
        fromDotnet.Should().NotContain("NONBLOCK", summary);
        // Always reported, so the run says what it saw either way.
        false.Should().BeTrue("this is the measurement, not a check: " + summary);
    }

    // ── bare .NET readers ────────────────────────────────────────────────────────────────────────

    public enum Read { Async, BlockingThread }

    // Run 4 settled read mode, stderr and launcher: every variant missed, Python too, rumdl alive and silent after 25 s.
    // What is left is when the request arrives relative to rumdl's own startup, so this run varies only that.
    [Theory]
    [InlineData(Read.Async, true, 0)]
    [InlineData(Read.Async, true, 250)]
    public async Task A_bare_dotnet_reader_gets_every_answer(Read how, bool redirectStderr, int delayMs)
    {
        var ct = TestContext.Current.CancellationToken;
        var rumdl = ForeignServer.Markdown.Find()!;
        var missed = new List<string>();
        var times = new List<double>();
        for (var i = 0; i < Starts; i++)
        {
            var psi = new ProcessStartInfo(rumdl, ForeignServer.Markdown.ServerArguments)
            {
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = redirectStderr,
                UseShellExecute = false, WorkingDirectory = Path.GetTempPath(),
            };
            psi.Environment.Remove("RUST_LOG");   // as in the original failures, which had no RUST_LOG
            using var p = Process.Start(psi)!;
            var drainErr = redirectStderr ? p.StandardError.BaseStream.CopyToAsync(Stream.Null, ct) : Task.CompletedTask;
            var clock = Stopwatch.StartNew();
            var input = p.StandardInput.BaseStream;
            if (delayMs > 0) await Task.Delay(delayMs, ct);
            await input.WriteAsync(Frame, ct);
            await input.FlushAsync(ct);

            var buffer = new byte[64];
            var output = p.StandardOutput.BaseStream;
            var read = how == Read.Async
                ? output.ReadAsync(buffer, ct).AsTask()
                : Task.Factory.StartNew(() => output.Read(buffer, 0, buffer.Length), ct, TaskCreationOptions.LongRunning, TaskScheduler.Default);

            if (await Task.WhenAny(read, Task.Delay(Wait, ct)) == read && await read > 0)
            {
                times.Add(clock.Elapsed.TotalMilliseconds);
            }
            else
            {
                // Late or never? And was rumdl still alive when we gave up?
                var aliveAtMiss = !p.HasExited;
                var lateAnswer = await Task.WhenAny(read, Task.Delay(Late, ct)) == read;
                var what = lateAnswer ? $"answered late at {clock.Elapsed.TotalSeconds:0.0}s ({await read} bytes)" : $"no answer after {clock.Elapsed.TotalSeconds:0}s";
                missed.Add($"#{i}: {what}, alive at miss: {aliveAtMiss}, alive now: {!p.HasExited}");
            }
            try { p.Kill(); } catch { /* already gone */ }
            await p.WaitForExitAsync(ct);
            try { await read; } catch { /* closed */ }
            try { await drainErr; } catch { /* pipe closed */ }
        }

        times.Sort();
        var summary = $"bare .NET reader ({how}, stderr {(redirectStderr ? "redirected" : "inherited")}, written after {delayMs}ms): {missed.Count} of {Starts} "
                    + $"unanswered within {Wait.TotalSeconds:0}s; answered median {(times.Count > 0 ? times[times.Count / 2] : 0):0}ms, "
                    + $"max {(times.Count > 0 ? times[^1] : 0):0}ms. Misses: {string.Join(" | ", missed)}";
        missed.Should().BeEmpty(summary);
    }

    // ── Python readers ───────────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("devnull")]
    public async Task A_python_reader_gets_every_answer(string stderrMode)
    {
        var ct = TestContext.Current.CancellationToken;
        var rumdl = ForeignServer.Markdown.Find()!;
        var script = Path.Combine(Path.GetTempPath(), $"rumdl_many_{Environment.ProcessId}.py");
        await File.WriteAllTextAsync(script, """
            import os, subprocess, sys, threading, time
            exe, cwd, runs, timeout, frame_hex, err_mode = sys.argv[1], sys.argv[2], int(sys.argv[3]), float(sys.argv[4]), sys.argv[5], sys.argv[6]
            frame = bytes.fromhex(frame_hex)
            env = dict(os.environ); env.pop("RUST_LOG", None)
            times, hung = [], []
            for i in range(runs):
                err = subprocess.PIPE if err_mode == "pipe" else subprocess.DEVNULL
                proc = subprocess.Popen([exe, "server"], cwd=cwd, stdin=subprocess.PIPE, stdout=subprocess.PIPE, stderr=err, env=env)
                if err_mode == "pipe":
                    threading.Thread(target=lambda: proc.stderr.read(), daemon=True).start()
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
            print(f"python reader (stderr {err_mode}): {len(hung)} of {runs} starts unanswered within {timeout:.0f}s {hung[:10]}; answered median {med:.0f}ms, max {mx:.0f}ms")
            """, ct);

        var psi = new ProcessStartInfo(Python) { RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        foreach (var a in new[] { "-I", script, rumdl, Path.GetTempPath(), Starts.ToString(), Wait.TotalSeconds.ToString("0"), Convert.ToHexString(Frame), stderrMode })
            psi.ArgumentList.Add(a);
        using var p = Process.Start(psi)!;
        var stdout = p.StandardOutput.ReadToEndAsync(ct);
        var stderr = p.StandardError.ReadToEndAsync(ct);
        await p.WaitForExitAsync(ct);
        var summary = (await stdout).Trim() + " " + (await stderr).Trim();
        summary.Should().Contain(": 0 of", summary);
    }
}
