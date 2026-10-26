using System.Diagnostics;
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
        await Console.Error.WriteLineAsync($"pid:{Environment.ProcessId}");
        var input = Console.OpenStandardInput();
        var output = Console.OpenStandardOutput();
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
            if (method == "exit")
            {
                if (mode is "ignore" or "tree") continue;
                // Ensure disposal really allows an orderly exit rather than winning a scheduling race.
                await Task.Delay(150);
                return shutdown ? 0 : 1;
            }
            if (method == "initialized") continue;
            if (method == "textDocument/hover" && mode == "crash")
            {
                await output.WriteAsync(Encoding.UTF8.GetBytes("Content-Length: 100\r\n\r\n{\"jsonrpc\":"));
                await output.FlushAsync();
                return 23;
            }
            if (method == "textDocument/hover" && mode == "nonzero") return 17;
            if (method == "shutdown") shutdown = true;
            var result = method == "initialize" ? "{\"capabilities\":{\"hoverProvider\":true}}" : "null";
            var reply = Encoding.UTF8.GetBytes($"{{\"jsonrpc\":\"2.0\",\"id\":{root.GetProperty("id").GetRawText()},\"result\":{result}}}");
            await output.WriteAsync(Encoding.ASCII.GetBytes($"Content-Length: {reply.Length}\r\n\r\n"));
            await output.WriteAsync(reply);
            await output.FlushAsync();
        }
    }
}
