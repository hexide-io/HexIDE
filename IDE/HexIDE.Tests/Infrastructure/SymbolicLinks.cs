namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Whether this machine can make symbolic links, found by making one (#5663). Windows allows it only with Developer Mode or
/// elevation, so the tests that need a link once skipped on every Windows machine, and the packer's and the project
/// writer's refusals of linked files were never run there.
/// <para>
/// Under CI a machine that can't make a link doesn't skip those tests: they run, and fail at the link they can't create, and
/// <see cref="SymbolicLinkProbeTests"/> says why. A probe that broke would otherwise remove security checks without a sound,
/// the way a server that couldn't be fetched once removed the foreign-server proof (HEXIDE_REQUIRE_FOREIGN_LSP).
/// </para>
/// </summary>
internal static class SymbolicLinks
{
    private static readonly Lazy<bool> Probe = new(TryCreate);

    /// <summary>Whether a symbolic link to a file could be made in the temp directory.</summary>
    public static bool CanCreate => Probe.Value;

    /// <summary>Under CI, where the tests that need links must run rather than skip.</summary>
    public static bool Required => string.Equals(Environment.GetEnvironmentVariable("CI"), "true", StringComparison.OrdinalIgnoreCase);

    /// <summary>For a test's <c>SkipUnless</c>: run where a link can be made, and always under CI.</summary>
    public static bool RunTests => CanCreate || Required;

    public const string SkipReason =
        "Needs symbolic links, which this machine can't make (on Windows: turn on Developer Mode, or run elevated)";

    private static bool TryCreate()
    {
        var directory = Path.Combine(Path.GetTempPath(), "hexide-link-probe-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(directory);
            var target = Path.Combine(directory, "target.txt");
            File.WriteAllText(target, "probe");
            var link = Path.Combine(directory, "link.txt");
            File.CreateSymbolicLink(link, target);
            return new FileInfo(link).LinkTarget is not null && File.ReadAllText(link) == "probe";
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or IOException or PlatformNotSupportedException)
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(directory, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
