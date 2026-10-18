using System.Diagnostics;
using System.Text;
using System.Text.Json;

namespace HexIDE.Dap.TestAdapter;

// A real child process with literal protocol framing, deliberately independent of HexIDE.Dap.
public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var mode = args[0];
        if (mode == "exit") { Console.Error.Write("adapter startup failed"); return 37; }
        // A failed test must not leave an orphan alive indefinitely.
        if (mode == "child") { await Task.Delay(TimeSpan.FromMinutes(1)); return 0; }
        if (mode == "malformed")
        {
            Console.Write("Content-Length: nope\r\n\r\n");
            Console.Out.Flush();
            await Task.Delay(Timeout.Infinite);
        }
        if (mode == "flood")
        {
            Console.Error.Write(new string('x', 256 * 1024));
            Console.Error.Write("stderr-end");
            Console.Error.Flush();
        }
        if (mode is "tree" or "orphan")
        {
            var start = new ProcessStartInfo(Environment.ProcessPath!) { UseShellExecute = false, CreateNoWindow = true };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("child");
            using var child = Process.Start(start)!;
            await Send(new { seq = 1, type = "event", @event = "ready", body = new { childId = child.Id } });
            if (mode == "orphan") return 0;
            await Task.Delay(Timeout.Infinite);
        }
        await Send(new { seq = 1, type = "event", @event = "ready" });
        if (mode == "hang") { await Task.Delay(Timeout.Infinite); return 0; }

        using var input = Console.OpenStandardInput();
        var one = new byte[1];
        int? heldVariables = null;
        int? heldEvaluation = null;
        string callerValue = "42";
        while (true)
        {
            var header = new StringBuilder();
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await input.ReadAsync(one) == 0) return 0;
                header.Append((char)one[0]);
            }
            var length = int.Parse(header.ToString()[16..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            var body = new byte[length];
            // Disposal can close stdin between the header and body. Treat that like EOF in
            // the header above, so the fixture's exit code does not depend on write timing.
            try { await input.ReadExactlyAsync(body); }
            catch (EndOfStreamException) { return 0; }
            using var request = JsonDocument.Parse(body);
            if (mode == "silent") continue;
            if (mode is "session" or "frames" or "frames-delayed" or "evaluate")
            {
                var command = request.RootElement.GetProperty("command").GetString();
                if (command == "evaluate")
                {
                    var arguments = request.RootElement.GetProperty("arguments");
                    var expression = arguments.GetProperty("expression").GetString();
                    var frame = arguments.GetProperty("frameId").GetInt32();
                    if (expression == "wait")
                    {
                        heldEvaluation = request.RootElement.GetProperty("seq").GetInt32();
                        await Send(new { seq = 10, type = "event", @event = "output", body = new { output = "evaluation-pending\n" } });
                        continue;
                    }
                    if (expression == "callerValue = 99" && frame == 7) callerValue = "99";
                    await Send(new { seq = 11, type = "response", request_seq = request.RootElement.GetProperty("seq").GetInt32(),
                        command, success = expression != "reject" && arguments.GetProperty("context").GetString() == "repl",
                        message = "fixture refusal", body = new { result = $"frame:{frame}:{expression}", variablesReference = 0 } });
                    continue;
                }
                if (mode == "frames-delayed" && command == "variables" &&
                    request.RootElement.GetProperty("arguments").GetProperty("variablesReference").GetInt32() == 8)
                {
                    heldVariables = request.RootElement.GetProperty("seq").GetInt32();
                    await Send(new { seq = 7, type = "event", @event = "output", body = new { output = "caller-pending\n" } });
                    continue;
                }
                object reply = command switch
                {
                    "initialize" => new { supportsConfigurationDoneRequest = true },
                    "threads" => new { threads = new[] { new { id = 1, name = "Main" } } },
                    "stackTrace" when mode != "session" => new { stackFrames = new[]
                    {
                        new { id = 0, name = "Callee", line = 6, column = 1, source = new { path = Path.GetFullPath("Callee.bas") } },
                        new { id = 7, name = "Caller", line = 12, column = 1, source = new { path = Path.GetFullPath("Caller.bas") } },
                    } },
                    "scopes" when mode != "session" => new { scopes = new[] { new { name = "Locals", variablesReference = request.RootElement.GetProperty("arguments").GetProperty("frameId").GetInt32() + 1, expensive = false } } },
                    "variables" when mode != "session" => FrameVariables(request.RootElement.GetProperty("arguments").GetProperty("variablesReference").GetInt32(), callerValue),
                    "stackTrace" => new { stackFrames = new[] { new { id = 0, name = "Main", line = 6, column = 1 } } },
                    "scopes" => new { scopes = new[] { new { name = "Locals", variablesReference = 1, expensive = false } } },
                    "variables" => new { variables = new[] { new { name = "answer", value = "41", type = "Long", variablesReference = 0 } } },
                    _ => new { },
                };
                await Send(new { seq = 2, type = "response", request_seq = request.RootElement.GetProperty("seq").GetInt32(), command, success = true, body = reply });
                if (command == "initialize") await Send(new { seq = 3, type = "event", @event = "initialized" });
                if (command is "configurationDone" or "next")
                    await Send(new { seq = 4, type = "event", @event = "stopped", body = new { threadId = 1, reason = "breakpoint" } });
                if (command == "continue")
                {
                    await Send(new { seq = 5, type = "event", @event = "output", body = new { output = "42\n" } });
                    await Send(new { seq = 6, type = "event", @event = "terminated" });
                }
                if (heldVariables is { } held && command is "variables" or "next" or "continue")
                {
                    await Send(new { seq = 8, type = "response", request_seq = held, command = "variables", success = true, body = FrameVariables(8) });
                    heldVariables = null;
                }
                if (heldEvaluation is { } evaluation && command is "scopes" or "next" or "continue" or "disconnect")
                {
                    await Send(new { seq = 12, type = "response", request_seq = evaluation, command = "evaluate", success = true,
                        body = new { result = "obsolete-evaluation", variablesReference = 0 } });
                    heldEvaluation = null;
                }
                if (command == "disconnect") return 0;
                continue;
            }
            await Send(new
            {
                seq = 2, type = "response", request_seq = request.RootElement.GetProperty("seq").GetInt32(),
                command = request.RootElement.GetProperty("command").GetString(), success = true,
                body = new { arguments = args.Skip(1), cwd = Environment.CurrentDirectory,
                    environment = Environment.GetEnvironmentVariable("HEXIDE_DAP_FIXTURE_VALUE") }
            });
            if (mode == "reply-exit") return 0;
        }
    }

    private static object FrameVariables(int reference, string callerValue = "42") => new
    {
        variables = new[] { new { name = reference == 8 ? "callerValue" : "calleeValue", value = reference == 8 ? callerValue : "41", type = "Long", variablesReference = 0 } },
    };

    private static async Task Send(object value)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(value);
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        var output = Console.OpenStandardOutput();
        await output.WriteAsync(header);
        await output.WriteAsync(body);
        await output.FlushAsync();
    }
}
