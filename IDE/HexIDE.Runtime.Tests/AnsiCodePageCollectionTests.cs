using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;

namespace HexIDE.Runtime.Tests;

public class AnsiCodePageCollectionTests
{
    [Fact]
    public void Code_page_switchers_share_a_nonparallel_collection()
    {
        var files = Directory.EnumerateFiles(SourceDirectory(), "*.cs", SearchOption.AllDirectories)
            .Where(file => Path.GetFileName(file) != nameof(AnsiCodePageCollectionTests) + ".cs")
            .Where(file => File.ReadAllText(file).Contains("SimulateHostCodePage(", StringComparison.Ordinal))
            .ToArray();
        files.Should().NotBeEmpty("the guard must find the process-wide ANSI code page switchers (#5070)");

        var testClasses = typeof(AnsiCodePageCollectionTests).Assembly.GetTypes()
            .Where(type => type.GetMethods().Any(method => method.GetCustomAttributes<FactAttribute>().Any()))
            .ToArray();
        foreach (var file in files)
        {
            var source = File.ReadAllText(file);
            var classes = testClasses.Where(type => Regex.IsMatch(source, @"\bclass\s+" + type.Name + @"\b")).ToArray();
            classes.Should().NotBeEmpty("{0} calls the code page switcher and must have a test class", file);
            foreach (var testClass in classes)
            {
                var collection = testClass.GetCustomAttributes().OfType<Xunit.v3.ICollectionAttribute>().SingleOrDefault();
                collection.Should().NotBeNull("{0} switches the process-wide ANSI code page (#5070)", testClass.Name);
                collection.Type.Should().Be(typeof(AnsiCodePageCollection),
                    "{0} switches the process-wide ANSI code page (#5070)", testClass.Name);
            }
        }

        typeof(AnsiCodePageCollection).GetCustomAttribute<CollectionDefinitionAttribute>()!
            .DisableParallelization.Should().BeTrue();
    }

    // Follow UnavailableTestGuardTests to locate the suite's sources.
    private static string SourceDirectory([CallerFilePath] string source = "") => Path.GetDirectoryName(source)!;
}

// Classes in this collection switch the process-wide ANSI code page (#5070).
[CollectionDefinition(DisableParallelization = true)]
public sealed class AnsiCodePageCollection;
