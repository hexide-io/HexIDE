using System.Diagnostics;
using System.IO.Pipes;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;

namespace HexIDE.Lsp.TestServer;

public static class Program
{
    public static async Task<int> Main(string[] args)
    {
        var mode = args[0];
        if (mode == "child")
        {
            await Task.Delay(Timeout.Infinite);
            return 0;
        }
        if (mode == "tree")
        {
            var start = new ProcessStartInfo("dotnet") { UseShellExecute = false };
            start.ArgumentList.Add(typeof(Program).Assembly.Location);
            start.ArgumentList.Add("child");
            using var child = Process.Start(start)!;
            await Console.Error.WriteLineAsync($"child:{child.Id}");
        }
        if (mode != "logging") await Console.Error.WriteLineAsync($"pid:{Environment.ProcessId}");
        Stream input = Console.OpenStandardInput();
        Stream output = Console.OpenStandardOutput();
        if (mode == "tcp")
        {
            var tcp = new TcpClient();
            await tcp.ConnectAsync(args[1], int.Parse(args[2], System.Globalization.CultureInfo.InvariantCulture));
            input = output = tcp.GetStream();
        }
        else if (args.Length > 1 && !mode.StartsWith("initialize-", StringComparison.Ordinal))
        {
            var pipe = new NamedPipeClientStream(".", args[1], PipeDirection.InOut, PipeOptions.Asynchronous);
            await pipe.ConnectAsync();
            input = output = pipe;
        }
        var shutdown = false;
        while (true)
        {
            var header = new StringBuilder();
            var one = new byte[1];
            while (!header.ToString().EndsWith("\r\n\r\n", StringComparison.Ordinal))
            {
                if (await input.ReadAsync(one) == 0)
                {
                    await Task.Delay(Timeout.Infinite);
                    return 0;
                }
                header.Append((char)one[0]);
            }
            var length = int.Parse(header.ToString().Split("\r\n")[0].Split(':')[1]);
            var body = new byte[length];
            await input.ReadExactlyAsync(body);
            using var message = JsonDocument.Parse(body);
            var root = message.RootElement;
            var method = root.GetProperty("method").GetString();
            if (method == "initialize" && mode.StartsWith("initialize-", StringComparison.Ordinal))
            {
                var first = !File.Exists(args[1]);
                await File.AppendAllTextAsync(args[1], $"{Environment.ProcessId}{Environment.NewLine}");
                if (mode == "initialize-silent" || (mode == "initialize-once" && first)) continue;
                if (mode == "initialize-exit") return 7;
                if (mode == "initialize-partial")
                {
                    await output.WriteAsync("Content-Length: 100\r\n\r\n{"u8.ToArray());
                    await output.FlushAsync();
                    continue;
                }
                if (mode == "initialize-refuse")
                {
                    var error = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":{root.GetProperty("id").GetRawText()},\"error\":{{\"code\":-32602,\"message\":\"refused\"}}}}");
                    await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {error.Length}\r\n\r\n"));
                    await output.WriteAsync(error);
                    await output.FlushAsync();
                    continue;
                }
            }
            if (mode == "tcp") await Console.Out.WriteLineAsync($"tcp-received:{method}");
            if (method == "exit")
            {
                if (mode == "logging")
                {
                    await Console.Error.WriteLineAsync("stack-frame");
                    return 3;
                }
                if (mode is "ignore" or "tree" or "unanswered") continue;
                // Ensure disposal really allows an orderly exit rather than winning a scheduling race.
                await Task.Delay(150);
                return shutdown ? 0 : 1;
            }
            if (method is "initialized" or "$/cancelRequest") continue;
            if (method == "textDocument/hover" && mode == "crash")
            {
                await output.WriteAsync(Encoding.UTF8.GetBytes("Content-Length: 100\r\n\r\n{\"jsonrpc\":"));
                await output.FlushAsync();
                return 23;
            }
            if (method == "textDocument/hover" && mode == "nonzero") return 17;
            if (method == "textDocument/hover" && mode == "exit1") return 1;
            if (method == "textDocument/hover" && mode == "exit2") return 2;
            if (method == "shutdown" && mode == "unanswered")
            {
                await HoldAfterShutdownAsync(input);
                return 0;
            }
            if (method == "shutdown") shutdown = true;
            var result = method == "initialize" ? "{\"capabilities\":{\"hoverProvider\":true}}" : "null";
            var reply = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":{root.GetProperty("id").GetRawText()},\"result\":{result}}}");
            try
            {
                await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {reply.Length}\r\n\r\n"));
                await output.WriteAsync(reply);
                await output.FlushAsync();
            }
            finally
            {
                // Teardown can break a Windows pipe before exit is read (or the reply flush completes).
                // Drain so Windows pipe flushes can finish, then outlast the exit wait (#3244, #3599).
                if (mode == "slow" && shutdown)
                {
                    await HoldAfterShutdownAsync(input);
                }
            }
            if (mode == "slow" && shutdown) return 0;
        }
    }

    private static async Task HoldAfterShutdownAsync(Stream input)
    {
        try
        {
            await input.CopyToAsync(Stream.Null);
        }
        catch (IOException)
        {
            // A broken pipe is also a disconnect.
        }
        await Task.Delay(TimeSpan.FromMinutes(1));
    }
}
