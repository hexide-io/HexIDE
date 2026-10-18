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
            await input.ReadExactlyAsync(body);
            using var request = JsonDocument.Parse(body);
            if (mode == "silent") continue;
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
