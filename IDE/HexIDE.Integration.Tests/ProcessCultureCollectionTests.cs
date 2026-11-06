using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;

namespace HexIDE.Integration.Tests;

public class ProcessCultureCollectionTests
{
    // An assignment to either default, or App.Initialize, which makes both. Written so this file does not match itself.
    private static readonly Regex SetsADefault = new(
        "DefaultThread" + @"Current(UI)?Culture\s*=(?!=)|new\s+App\(\)\s*\.\s*" + "Initialize\\(");

    private const string InTheCollection = "[Collection(typeof(ProcessCultureCollection))]";

    [Fact]
    public void The_collection_runs_alone()
    {
        typeof(ProcessCultureCollection).GetCustomAttribute<CollectionDefinitionAttribute>()!
            .DisableParallelization.Should().BeTrue();
    }

    [Fact]
    public void Every_test_that_sets_a_process_wide_culture_default_runs_in_it()
    {
        var project = Path.Combine(RepoRoot(), "IDE", "HexIDE.Integration.Tests");
        var setters = Directory.EnumerateFiles(project, "*.cs", SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(project, file).Split('/', '\\').Any(part => part is "bin" or "obj"))
            .Select(file => (Name: Path.GetRelativePath(project, file).Replace('\\', '/'), Text: File.ReadAllText(file)))
            .Where(file => SetsADefault.IsMatch(file.Text))
            .ToList();

        // The two known today, so the scan is not passing because it read nothing.
        setters.Select(file => file.Name).Should().Contain(["CultureGuardTests.cs", "Controls/DockOpacityTests.cs"]);
        setters.Where(file => !file.Text.Contains(InTheCollection)).Select(file => file.Name).Should().BeEmpty(
            "a culture default is process-wide, so a test that sets one while others run makes the guard name them (#2500)");
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !(Directory.Exists(Path.Combine(dir.FullName, "IDE"))
                                && Directory.Exists(Path.Combine(dir.FullName, "LspServer"))))
            dir = dir.Parent;
        return dir!.FullName;
    }
}
