using System.Diagnostics;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using HexIDE.Lsp;
using Microsoft.Extensions.Logging.Abstractions;
using StreamJsonRpc;

namespace HexIDE.Lsp.TestHost;

// Separate owner and server processes let the test kill the owner without running any disposal code.
public static class Program
{
    public static async Task Main(string[] args)
    {
        if (args[0] == "exit")
        {
            Environment.Exit(int.Parse(args[1]));
            return;
        }
        if (args[0] == "echo")
        {
            await Console.OpenStandardInput().CopyToAsync(Console.OpenStandardOutput());
            return;
        }
        if (args[0] == "launch-probe")
        {
            // LaunchConfigurationTests: what a configured launch handed this process, written where the test reads it,
            // all at once, so the test never reads half of it.
            var output = Environment.GetEnvironmentVariable("HEXIDE_LAUNCH_OUTPUT")!;
            string[] received =
            [
                Environment.GetEnvironmentVariable("HEXIDE_LAUNCH_PROBE") ?? "",
                Environment.GetEnvironmentVariable("PATH") ?? "",
                .. args.Skip(1).Select(argument => "[" + argument + "]"),
            ];
            await File.WriteAllLinesAsync(output + ".tmp", received);
            File.Move(output + ".tmp", output);
            return;
        }
        if (args[0] == "teardown-probe")
        {
            // NamedPipeShutdownTests: a server still bringing up. It says it is running, waits for the test to start
            // teardown, and exits that many milliseconds later, leaving a marker only if nothing killed it first.
            await File.WriteAllTextAsync(args[1], "ready");
            while (!File.Exists(args[2])) await Task.Delay(20);
            await Task.Delay(int.Parse(args[4]));
            await File.WriteAllTextAsync(args[3] + ".tmp", "done");
            File.Move(args[3] + ".tmp", args[3]);
            return;
        }
        if (args[0] == "clean-server")
        {
            using var child = Process.Start(StartInfo(args[1], args[2]))!;
            while (!File.Exists(args[2])) await Task.Delay(20);
            Console.WriteLine(child.Id);
            await Console.In.ReadLineAsync();
            Environment.Exit(37);
            return;
        }
        using var termination = (OperatingSystem.IsLinux() || OperatingSystem.IsMacOS())
            && args[0] is "server" or "child"
            ? PosixSignalRegistration.Create(PosixSignal.SIGTERM, context => context.Cancel = true)
            : null;
        if (args[0] is "child" or "terminable-child")
        {
            if (args.Length > 1) await File.WriteAllTextAsync(args[1], "ready");
            await Task.Delay(TimeSpan.FromMinutes(1));
            return;
        }

        if (args[0] == "server")
        {
            // Wait for the test before spawning, so the descendant is created after job assignment.
            while (!File.Exists(args[2])) await Task.Delay(20);
            using var child = Process.Start(StartInfo("child"))!;
            await File.WriteAllTextAsync(args[1] + ".tmp", $"{Environment.ProcessId} {child.Id}");
            File.Move(args[1] + ".tmp", args[1]);
            if (args.Length > 3)
            {
                using var pipe = new NamedPipeClientStream(".", args[3], PipeDirection.InOut);
                await pipe.ConnectAsync();
                await Task.Delay(TimeSpan.FromMinutes(1));
            }
            else await Task.Delay(TimeSpan.FromMinutes(1));
            return;
        }

        var server = StartInfo("server", args[1], args[2]);
        ILspTransport transport;
        if (args[0] == "pipe")
        {
            var pipeName = $"h406-{Guid.NewGuid():N}"[..19];
            server.ArgumentList.Add(pipeName);
            transport = new NamedPipeLspTransport(pipeName, NamedPipeRole.Listen,
                NullLogger<NamedPipeLspTransport>.Instance,
                new NamedPipeLaunch(server.FileName, "", ArgumentList: server.ArgumentList.ToArray()));
        }
        else transport = new StdioProcessLspTransport(
            new LspServerInfo(server.FileName, "", "") { ArgumentList = server.ArgumentList.ToArray() },
            NullLogger<StdioProcessLspTransport>.Instance);

        var connecting = transport.ConnectAsync(new SystemTextJsonFormatter());
        // Pipe startup waits for the server; stdio returns after starting and assigning it.
        // Either way the server waits for this gate before creating its own child.
        if (args[0] != "pipe") await connecting;
        await File.WriteAllTextAsync(args[2], "start");
        await connecting;
        while (!File.Exists(args[1])) await Task.Delay(20);
        Console.WriteLine("ready");
        await Task.Delay(TimeSpan.FromMinutes(1));
        GC.KeepAlive(transport);
    }

    private static ProcessStartInfo StartInfo(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet") { UseShellExecute = false, CreateNoWindow = true };
        start.ArgumentList.Add(typeof(Program).Assembly.Location);
        foreach (var argument in args) start.ArgumentList.Add(argument);
        return start;
    }
}
