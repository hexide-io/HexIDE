using System.IO;
using System.Linq;
using System.Xml.Linq;
using HexIDE.Integration.Tests;

namespace HexIDE.Runtime.Integration.Tests;

public class TestAppTests
{
    private const string BuiltinControls = "avares://HexIDE.Runtime/BuiltinControls/";

    // The test application stands in for the runner, so it loads what the runner loads: a dictionary the runner
    // stopped loading, or started to, would otherwise leave these tests testing a program nobody runs.
    [Fact]
    public void The_test_application_loads_the_dictionaries_the_Standalone_runner_loads()
    {
        var app = XDocument.Load(Path.Join(RepoTree.Root(), "IDE", "HexIDE.Standalone", "App.axaml"));
        var runner = app.Descendants()
            .Where(element => element.Name.LocalName == "ResourceInclude")
            .Select(element => (string)element.Attribute("Source")!)
            .ToList();

        runner.Should().OnlyContain(source => source.StartsWith(BuiltinControls),
            "the runner loads the runtime's own dictionaries and no other project's");
        runner.Select(source => source[BuiltinControls.Length..]).Should().Equal(TestApp.RunnerDictionaries);
    }
}
