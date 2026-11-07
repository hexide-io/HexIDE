using System.IO;
using System.Xml.Linq;

namespace HexIDE.Tests;

public class HelpEntryPointTests
{
    [Theory]
    [InlineData("MainView.axaml", "Str.Menu.Help.Contents")]
    [InlineData("MainView.axaml", "Str.Menu.Help.Index")]
    [InlineData("MainView.axaml", "Str.Menu.Help.Search")]
    [InlineData("MainView.axaml", "Str.Menu.Help.TechnicalSupport")]
    [InlineData("Forms/Views/NewProjectView.axaml", "Str.Dialog.NewProject.Button.Help")]
    [InlineData("Forms/Views/ReferencesView.axaml", "Str.Dialog.References.Help")]
    public void UnsupportedHelpEntryPoint_IsNotOffered(string view, string key)
    {
        var document = XDocument.Load(Path.Combine(ViewDirectory(), view));
        document.Descendants().SelectMany(element => element.Attributes())
            .Should().NotContain(attribute => attribute.Value == "{DynamicResource " + key + "}");
    }

    private static string ViewDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var views = Path.Combine(dir.FullName, "IDE", "HexIDE");
            if (File.Exists(Path.Combine(views, "MainView.axaml"))) return views;
        }
        throw new DirectoryNotFoundException("Could not locate IDE views");
    }
}
