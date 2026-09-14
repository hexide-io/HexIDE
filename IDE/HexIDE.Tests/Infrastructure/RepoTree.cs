namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Where the repository is, for tests that read the tree as source rather than the built assemblies.
/// </summary>
internal static class RepoTree
{
    /// <summary>
    /// The directory holding both <c>IDE/</c> and <c>LspServer/</c>, found by walking up from the test
    /// binaries. Throws rather than guessing, so a test run from an unexpected layout fails loudly.
    /// </summary>
    public static string Root()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            if (Directory.Exists(Path.Combine(dir.FullName, "IDE")) &&
                Directory.Exists(Path.Combine(dir.FullName, "LspServer")))
            {
                return dir.FullName;
            }

            dir = dir.Parent;
        }

        throw new InvalidOperationException("no ancestor holds both IDE/ and LspServer/");
    }
}
