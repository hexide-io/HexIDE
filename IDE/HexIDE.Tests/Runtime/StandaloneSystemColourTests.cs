using System.Xml.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Media;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Tests.Infrastructure;

namespace HexIDE.Tests.Runtime;

/// <summary>
/// A made program runs on the Standalone runner, which has only the dictionaries its <c>App.axaml</c> names. A VB6
/// system colour that none of them defines draws hot pink there, and every one of them did (#3695): the colours were
/// defined only by the IDE's theme, which the IDE's tests always had loaded.
/// </summary>
public class StandaloneSystemColourTests
{
    [AvaloniaFact]
    public void The_Standalone_runner_loads_a_value_for_every_VB6_system_colour()
    {
        var app = XDocument.Load(Path.Join(RepoTree.Root(), "IDE", "HexIDE.Standalone", "App.axaml"));
        var loaded = new ResourceDictionary();
        foreach (var include in app.Descendants().Where(element => element.Name.LocalName == "ResourceInclude"))
            loaded.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE.Standalone/"))
            {
                Source = new Uri((string)include.Attribute("Source")!),
            });

        var unresolved = Enum.GetValues<VbSystemColor>()
            .Where(colour => !(loaded.TryGetResource(colour.GetKey(), null, out var value) && value is ISolidColorBrush))
            .ToList();

        unresolved.Should().BeEmpty("a made program has only the dictionaries the runner's App.axaml names");
    }
}
