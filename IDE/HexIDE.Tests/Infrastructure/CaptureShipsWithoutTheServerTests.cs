using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Holds the capture on the shipping side of the line the automation server is excluded by.
/// </summary>
/// <remarks>
/// <b>The requirement is that a build without the automation server still records a conversation.</b> The
/// inspector's audience is somebody writing a language server against a HexIDE they downloaded; the
/// automation server is a development tool compiled out of distributed builds. Putting the recording beside
/// the tools that read it would tie the shipped feature to the unshipped one.
///
/// <para>
/// <b>It is true today by arrangement, and arrangement is exactly what rots.</b> Nothing else in the tree
/// asserts it, and the failure would be silent in the worst way available: it appears only in a
/// configuration no pull-request job builds, so the first person to meet it would be a user of a release.
/// One <c>#if DEBUG</c> around the wrong line is all it takes.
/// </para>
///
/// <para>
/// Read from source rather than from a compiled Release assembly, deliberately. A test that needed a
/// Release build would either not run in the ordinary Debug suite — which is every run anyone does — or
/// would have to shell out to MSBuild and take a minute doing it. Source is where the mistake is made.
/// </para>
/// </remarks>
public class CaptureShipsWithoutTheServerTests
{
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                             && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;

        return dir?.FullName
            ?? throw new InvalidOperationException("Could not locate the repository root from the test output.");
    }

    private static string[] CaptureSources()
    {
        var root = RepoRoot();
        string[] folders =
        [
            Path.Combine(root, "IDE", "HexIDE.Core", "Conversations"),
            Path.Combine(root, "IDE", "HexIDE.Core", "Redaction"),
        ];

        return [.. folders.Where(Directory.Exists).SelectMany(f => Directory.GetFiles(f, "*.cs"))];
    }

    [Fact]
    public void TheCaptureIsWhereItCanShip()
    {
        var sources = CaptureSources();

        sources.Should().NotBeEmpty(
            "this guard reads the capture's own source, so finding none means it has moved and the guard "
          + "is now asserting nothing");
        sources.Should().HaveCountGreaterThan(5, "the capture is more than a file or two");
    }

    [Fact]
    public void NothingInTheCaptureIsCompiledConditionally()
    {
        // Any directive at all, not only `#if DEBUG`. A capture that varies by configuration is one whose
        // behaviour in the build people download is not the behaviour anyone tested.
        var offenders = CaptureSources()
            .Where(f => File.ReadAllLines(f).Any(l => l.TrimStart().StartsWith('#')
                                                  && !l.TrimStart().StartsWith("#region")
                                                  && !l.TrimStart().StartsWith("#endregion")))
            .Select(Path.GetFileName)
            .ToList();

        offenders.Should().BeEmpty(
            "the capture must behave identically in every configuration, because the build its audience "
          + "downloads is the one nothing here runs");
    }

    [Fact]
    public void TheCaptureDoesNotReachIntoTheAutomationServer()
    {
        string[] forbidden = ["HexIDE.Desktop", "HexIdeTools", "IdeContext", "IdeServer", "Microsoft.AspNetCore"];

        var offenders = new List<string>();
        foreach (var file in CaptureSources())
        {
            var text = File.ReadAllText(file);
            foreach (var name in forbidden)
            {
                if (text.Contains(name, StringComparison.Ordinal))
                    offenders.Add($"{Path.GetFileName(file)} mentions {name}");
            }
        }

        offenders.Should().BeEmpty(
            "the automation server is absent from distributed builds; a reference from the capture to "
          + "anything in it would take the capture with it");
    }

    [Fact]
    public void TheConversationLogIsConstructedInEveryConfiguration()
    {
        var path = Path.Combine(RepoRoot(), "IDE", "HexIDE", "DISetup.cs");
        var lines = File.ReadAllLines(path);

        var binding = IndexOf(lines, "new ConversationLog(");
        binding.Should().BeGreaterThanOrEqualTo(0, "the log is constructed in the composition root");
        InsideConditional(lines, binding).Should().BeFalse(
            "a conditionally-constructed log would mean a distributed build records nothing at all");
    }

    [Fact]
    public void TheLaunchFlagReachesEveryConfiguration()
    {
        // THE trap this guard exists for, and it is a live one: the nearest precedent in the same method
        // is `--developer-mode`, whose Static write IS deliberately wrapped in `#if DEBUG` so a
        // distributed binary cannot enter that state. Copying that shape here would make the capture flag
        // inert in exactly the build its audience runs, and nothing would say so.
        var path = Path.Combine(RepoRoot(), "IDE", "HexIDE.Desktop", "DesktopStartup.cs");
        var lines = File.ReadAllLines(path);

        var assignment = IndexOf(lines, "Static.CaptureLsp");
        assignment.Should().BeGreaterThanOrEqualTo(0,
            "the launch flag has to reach the shell somewhere, and this is where every other one does");

        InsideConditional(lines, assignment).Should().BeFalse(
            "the capture ships and the automation server does not, so a flag that arms it must not be "
          + "compiled out alongside the developer-mode flag beside it");
    }

    [Fact]
    public void TheDeveloperModeFlagIsStillTheOppositeCase()
    {
        // The control for the test above. If this stops being conditional, the one above is no longer
        // asserting a contrast and somebody should decide that deliberately rather than discover it.
        var path = Path.Combine(RepoRoot(), "IDE", "HexIDE.Desktop", "DesktopStartup.cs");
        var lines = File.ReadAllLines(path);

        InsideConditional(lines, IndexOf(lines, "Static.DeveloperMode")).Should().BeTrue(
            "developer mode is deliberately inert in a distributed build, and the capture flag's guard is "
          + "meaningless if the two are not actually different");
    }

    private static int IndexOf(string[] lines, string needle)
    {
        for (var i = 0; i < lines.Length; i++)
        {
            // Skip the comment lines that talk ABOUT these names, which is most mentions in this file.
            var trimmed = lines[i].TrimStart();
            if (trimmed.StartsWith("//") || trimmed.StartsWith("///")) continue;
            if (lines[i].Contains(needle, StringComparison.Ordinal)) return i;
        }

        return -1;
    }

    /// <summary>Whether a line sits inside any preprocessor conditional.</summary>
    private static bool InsideConditional(string[] lines, int index)
    {
        if (index < 0) return false;

        var depth = 0;
        for (var i = 0; i < index; i++)
        {
            var trimmed = lines[i].TrimStart();
            if (Regex.IsMatch(trimmed, @"^#if\b")) depth++;
            else if (Regex.IsMatch(trimmed, @"^#endif\b")) depth--;
        }

        return depth > 0;
    }
}
