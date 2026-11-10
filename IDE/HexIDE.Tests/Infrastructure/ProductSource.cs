using System.Text.RegularExpressions;

namespace HexIDE.Tests.Infrastructure;

/// <summary>Reads the product's own source, for a test that holds a line the compiler can't.</summary>
internal static class ProductSource
{
    private static readonly string[] Projects = ["HexIDE", "HexIDE.Core", "HexIDE.Lsp", "HexIDE.Desktop", "HexIDE.Browser"];
    private static readonly Regex Comments = new(@"//[^\n]*|/\*.*?\*/", RegexOptions.Singleline);

    /// <summary>The product files whose code, comments aside, matches <paramref name="names"/>, apart from those named.</summary>
    public static List<string> FilesMatching(Regex names, params string[] allowed)
    {
        var root = RepoRoot();
        var obj = $"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}";
        var bin = $"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}";

        return Projects
            .SelectMany(project => Directory.EnumerateFiles(Path.Join(root, "IDE", project), "*.cs", SearchOption.AllDirectories))
            .Where(file => !file.Contains(obj, StringComparison.Ordinal) && !file.Contains(bin, StringComparison.Ordinal))
            .Where(file => !allowed.Contains(Path.GetFileName(file)))
            .Where(file => names.IsMatch(Comments.Replace(File.ReadAllText(file), "")))
            .Select(file => Path.GetRelativePath(root, file))
            .ToList();
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                                && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("Could not locate the repository root from the test output.");
    }
}
