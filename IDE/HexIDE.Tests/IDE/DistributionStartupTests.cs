using System.Diagnostics;
using System.IO;
using HexIDE.Desktop;
using HexIDE.IDE;
using DesktopProgram = HexIDE.Desktop.Program;

namespace HexIDE.Tests.IDE;

public sealed class DistributionStartupTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void A_broken_installation_stops_before_any_state_is_read_or_created(bool help)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousHelp = ServerOptions.HelpRequested;
        typeof(ServerOptions).GetProperty(nameof(ServerOptions.HelpRequested))!.SetValue(null, false);
        try
        {
            File.WriteAllText(Path.Combine(directory, DistributionDescriptor.FileName), "{");
            var identity = new DistributionHolder();
            var output = new List<string>();
            var errors = new List<string>();
            var settings = Substitute.For<Action>();
            var logs = Substitute.For<Action>();
            var recovery = Substitute.For<Action>();

            var result = DesktopProgram.RunDistributionGate(help ? ["--help"] : [], directory, directory,
                identity.Establish, render => output.Add(render(new ConsoleTarget(false, int.MaxValue))), errors.Add,
                () => { settings(); logs(); recovery(); });

            // Main puts all subsequent startup, including every state-root lookup, behind this continuation.
            settings.DidNotReceive().Invoke();
            logs.DidNotReceive().Invoke();
            recovery.DidNotReceive().Invoke();
            result.Should().Be(2);
            string.Concat(help ? output : errors).Should().Contain("no settings, logs or recovery files were opened");
            output.Count.Should().Be(help ? 2 : 0);
            errors.Count.Should().Be(help ? 0 : 1);
            // A failed descriptor must not establish (or read) the supplied identity holder.
            var establish = () => identity.Establish(new DistributionIdentity("test", "Test"));
            establish.Should().NotThrow();
            Directory.GetFileSystemEntries(directory).Should().ContainSingle()
                .Which.Should().Be(Path.Combine(directory, DistributionDescriptor.FileName));
        }
        finally
        {
            typeof(ServerOptions).GetProperty(nameof(ServerOptions.HelpRequested))!.SetValue(null, previousHelp);
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task The_real_entry_point_refuses_a_broken_installation_without_creating_state(bool help)
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-start-" + Guid.NewGuid().ToString("N"));
        var installation = Path.Combine(directory, "installation");
        var state = Path.Combine(directory, "state");
        Directory.CreateDirectory(installation);
        try
        {
            // Keep the real executable's manifest and managed dependencies together in a broken installation.
            var source = Path.GetDirectoryName(typeof(DesktopProgram).Assembly.Location)!;
            foreach (var file in Directory.GetFiles(source))
                File.Copy(file, Path.Combine(installation, Path.GetFileName(file)));
            File.WriteAllText(Path.Combine(installation, DistributionDescriptor.FileName), "{");
            var start = new ProcessStartInfo("dotnet")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            start.ArgumentList.Add(Path.Combine(installation, "HexIDE.Desktop.dll"));
            if (help) start.ArgumentList.Add("--help");
            start.ArgumentList.Add("--user-data-dir");
            start.ArgumentList.Add(Path.Combine(state, "settings"));
            start.Environment["XDG_CONFIG_HOME"] = Path.Combine(state, "config");
            start.Environment["XDG_DATA_HOME"] = Path.Combine(state, "data");
            start.Environment["XDG_STATE_HOME"] = Path.Combine(state, "recovery");
            start.Environment["APPDATA"] = Path.Combine(state, "roaming");
            start.Environment["LOCALAPPDATA"] = Path.Combine(state, "local");
            using var process = Process.Start(start)!;
            var stdout = process.StandardOutput.ReadToEndAsync(TestContext.Current.CancellationToken);
            var stderr = process.StandardError.ReadToEndAsync(TestContext.Current.CancellationToken);
            try
            {
                await process.WaitForExitAsync(TestContext.Current.CancellationToken)
                    .WaitAsync(TimeSpan.FromSeconds(30), TestContext.Current.CancellationToken);
                process.ExitCode.Should().Be(2, await stderr);
                (await stdout).Should().Contain("no settings, logs or recovery files were opened");
                Directory.Exists(state).Should().BeFalse();
            }
            finally
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    await process.WaitForExitAsync(TestContext.Current.CancellationToken);
                }
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void Stock_help_exits_successfully_before_startup()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousHelp = ServerOptions.HelpRequested;
        typeof(ServerOptions).GetProperty(nameof(ServerOptions.HelpRequested))!.SetValue(null, false);
        try
        {
            var identity = new DistributionHolder();
            var output = new List<string>();
            DesktopProgram.RunDistributionGate(["--help"], directory, directory, identity.Establish,
                render => output.Add(render(new ConsoleTarget(false, int.MaxValue))),
                _ => throw new InvalidOperationException("Help must not report a startup error"),
                () => throw new InvalidOperationException("Help must not open state")).Should().Be(0);
            identity.Current.Should().BeSameAs(DistributionIdentity.Stock);
            output.Should().ContainSingle().Which.Should().Be(ServerOptions.HelpText(new ConsoleTarget(false, int.MaxValue)));
        }
        finally
        {
            typeof(ServerOptions).GetProperty(nameof(ServerOptions.HelpRequested))!.SetValue(null, previousHelp);
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public void A_valid_installation_establishes_its_identity_before_starting_state_once()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-gate-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var previousHelp = ServerOptions.HelpRequested;
        typeof(ServerOptions).GetProperty(nameof(ServerOptions.HelpRequested))!.SetValue(null, false);
        try
        {
            File.WriteAllText(Path.Combine(directory, DistributionDescriptor.FileName),
                """{"schemaVersion":1,"id":"acme","name":"Acme"}""");
            var identity = new DistributionHolder();
            var starts = 0;
            DesktopProgram.RunDistributionGate([], directory, directory, identity.Establish,
                _ => throw new InvalidOperationException("A valid start has no help output"),
                _ => throw new InvalidOperationException("A valid start has no branding error"),
                () => { identity.Current.Id.Should().Be("acme"); starts++; }).Should().BeNull();
            starts.Should().Be(1);
        }
        finally
        {
            typeof(ServerOptions).GetProperty(nameof(ServerOptions.HelpRequested))!.SetValue(null, previousHelp);
            Directory.Delete(directory, true);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Stock_and_two_brands_coexist_and_a_rename_keeps_every_root(bool windows)
    {
        // Windows' roaming/local split and Linux's config/data/state split, using rooted host paths
        // so both platform branches run on Linux as well as Windows (RecoveryPath validates them).
        var home = Path.GetFullPath(Path.Combine(Path.GetTempPath(), "user"));
        var config = windows ? Path.Combine(home, "AppData", "Roaming") : Path.Combine(home, ".config");
        var local = windows ? Path.Combine(home, "AppData", "Local") : Path.Combine(home, ".local", "share");
        var state = Path.Combine(home, ".local", "state");
        var identities = new[] { DistributionIdentity.Stock, new DistributionIdentity("acme", "Acme"),
            new DistributionIdentity("widgets", "Widgets"), new DistributionIdentity("acme", "Renamed") };
        var folders = new[] { "HexIDE", "acme", "widgets", "acme" };
        var roots = new List<string[]>();
        for (var i = 0; i < identities.Length; i++)
        {
            var brand = identities[i];
            var settings = new UserDataRoot(() => UserDataPath.For(brand, config));
            var paths = new[] { settings.Directory, brand.LogsDirectoryFor(local),
                RecoveryPath.For(null, windows, local, state, home, brand.StateDirectoryName) };
            paths.Should().Equal(Path.Combine(config, folders[i]), Path.Combine(local, folders[i], "logs"),
                Path.Combine(windows ? local : state, folders[i], "Recovery"));
            roots.Add(paths);
        }
        for (var i = 0; i < 3; i++)
            roots.Take(3).Select(paths => paths[i]).Should().OnlyHaveUniqueItems();
        roots[3].Should().Equal(roots[1]);
    }
}
