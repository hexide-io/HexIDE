using System.Diagnostics;

namespace HexIDE.Tests.Infrastructure;

public class LicenceGuardTests
{
    public static bool IsLinux => OperatingSystem.IsLinux();

    [Fact(Skip = "The CI shell guard runs on Linux", SkipUnless = nameof(IsLinux))]
    public void TrackedMplNoticeIsRejected()
    {
        var root = Path.Combine(RepoTree.Root(), "artifacts", $"licence-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(root, "scripts"));
        try
        {
            foreach (var file in Directory.GetFiles(Path.Combine(RepoTree.Root(), "scripts"), "*licences*"))
                File.Copy(file, Path.Combine(root, "scripts", Path.GetFileName(file)));
            File.Copy(Path.Combine(RepoTree.Root(), "Directory.Packages.props"), Path.Combine(root, "Directory.Packages.props"));
            Run("git", "init").ExitCode.Should().Be(0);
            File.WriteAllText(Path.Combine(root, "planted.cs"), "// This Source Code Form is subject to the terms of the Mozilla Public License, v. 2.0.");
            Run("git", "add", ".").ExitCode.Should().Be(0);
            var rejected = Run("bash", "scripts/check-licences.sh");
            rejected.ExitCode.Should().Be(1, rejected.Output);
            rejected.Output.Should().Contain("planted.cs");
            foreach (var licence in new[] { "MIT", "Apache-2.0", "BSD-2-Clause", "BSD-3-Clause", "ISC", "0BSD", "Unlicense", "MS-PL" })
            {
                File.WriteAllText(Path.Combine(root, "planted.cs"), "// SPDX-License-Identifier: " + licence);
                Run("bash", "scripts/check-licences.sh").ExitCode.Should().Be(0, licence);
            }
            File.WriteAllText(Path.Combine(root, "planted.cs"), "// SPDX-License-Identifier: Unknown-1.0");
            Run("bash", "scripts/check-licences.sh").ExitCode.Should().Be(1);
            File.WriteAllText(Path.Combine(root, "THIRD-PARTY-NOTICES.md"), "Credited file: `planted.cs`.");
            Run("bash", "scripts/check-licences.sh").ExitCode.Should().Be(0);
            File.WriteAllText(Path.Combine(root, "planted.cs"), "// A discussion of the Mozilla Public License is not a notice.");
            File.Delete(Path.Combine(root, "THIRD-PARTY-NOTICES.md"));
            Run("bash", "scripts/check-licences.sh").ExitCode.Should().Be(0);
        }
        finally
        {
            Directory.Delete(root, true);
        }

        (int ExitCode, string Output) Run(string executable, params string[] args)
        {
            var start = new ProcessStartInfo(executable)
            {
                WorkingDirectory = root, RedirectStandardOutput = true, RedirectStandardError = true
            };
            foreach (var arg in args) start.ArgumentList.Add(arg);
            using var process = Process.Start(start)!;
            var output = process.StandardOutput.ReadToEnd() + process.StandardError.ReadToEnd();
            process.WaitForExit();
            return (process.ExitCode, output);
        }
    }
}
