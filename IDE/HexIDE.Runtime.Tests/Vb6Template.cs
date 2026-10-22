using System.Runtime.CompilerServices;

namespace HexIDE.TestSupport;

/// <summary>
/// VB6's own Template tree, files Microsoft wrote, for a test that reads named files from it (#1440).
/// </summary>
/// <remarks>
/// <para>
/// <c>VB6_TEMPLATES</c> may name the Template folder or the folder it was extracted into, as the corpus scan, which
/// searches every subfolder, accepts either. Five classes joined their subfolders straight onto it, so naming the
/// parent found no file, and each returned at its guard and passed, having asserted nothing.
/// </para>
/// <para>
/// <b>Set, the variable is a promise the files are there.</b> A file it cannot find fails the test, naming every
/// path tried, rather than letting it return. A guard that skips silently is how those five stopped checking.
/// Unset, the tree is looked for where VB6 installs it, and a test skips, visibly, only where there is none: CI is
/// Linux and never has VB6. Where VB6 is installed, a missing file fails too.
/// </para>
/// <para>
/// Compiled into <c>HexIDE.Tests</c> as well, by link, so the two test projects resolve the tree one way.
/// </para>
/// </remarks>
internal static class Vb6Template
{
    public const string Variable = "VB6_TEMPLATES";
    public const string InstallPath = @"C:\Program Files (x86)\Microsoft Visual Studio\VB98\Template";

    /// <summary>Why a test reading the tree is skipped here, or null when it runs.</summary>
    public static string? SkipReason => SkipReasonFor(Environment.GetEnvironmentVariable(Variable), InstallPath);

    /// <summary>
    /// The file at <paramref name="relative"/> in the tree, such as <c>("Forms", "Web Browser.frm")</c>.
    /// </summary>
    /// <exception cref="FileNotFoundException">It is not there; the message names every path tried.</exception>
    public static string File(params string[] relative) =>
        FileFor(Environment.GetEnvironmentVariable(Variable), InstallPath, relative);

    internal static string? SkipReasonFor(string? configured, string installPath) =>
        !string.IsNullOrEmpty(configured) || Directory.Exists(installPath)
            ? null
            : $"No VB6 Template tree here: {Variable} is not set, and VB6 is not installed at '{installPath}'.";

    internal static string FileFor(string? configured, string installPath, string[] relative)
    {
        var root = string.IsNullOrEmpty(configured) ? installPath : configured;
        // The folder itself, then the Template folder inside it.
        string[] tried = [Path.Join([root, .. relative]), Path.Join([root, "Template", .. relative])];
        foreach (var path in tried)
        {
            if (System.IO.File.Exists(path))
                return path;
        }

        var where = string.IsNullOrEmpty(configured)
            ? $"VB6 is installed at '{installPath}'"
            : $"{Variable} is '{configured}'";
        throw new FileNotFoundException(
            $"{where}, but '{Path.Join(relative)}' is in neither the folder nor a Template folder inside it. Tried "
          + $"'{tried[0]}' and '{tried[1]}'. Point {Variable} at a complete copy of VB6's Template tree, or at the "
          + "folder it was extracted into.", tried[0]);
    }
}

/// <summary>A <see cref="FactAttribute"/> over VB6's Template tree, which skips, visibly, where there is none.</summary>
/// <remarks>The two <c>Caller*</c> parameters keep the test's source location, as on <c>WindowsOnlyFactAttribute</c>.</remarks>
public sealed class Vb6TemplateFactAttribute : FactAttribute
{
    public Vb6TemplateFactAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        Skip = Vb6Template.SkipReason;
    }
}

/// <summary>A <see cref="TheoryAttribute"/> over VB6's Template tree, which skips, visibly, where there is none.</summary>
public sealed class Vb6TemplateTheoryAttribute : TheoryAttribute
{
    public Vb6TemplateTheoryAttribute(
        [CallerFilePath] string? sourceFilePath = null,
        [CallerLineNumber] int sourceLineNumber = -1)
            : base(sourceFilePath, sourceLineNumber)
    {
        Skip = Vb6Template.SkipReason;
    }
}
