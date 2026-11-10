using System.IO;
using System.Xml.Linq;
using HexIDE.Menus;

namespace HexIDE.Tests;

public class HelpEntryPointTests
{
    [Theory]
    [InlineData("Forms/Views/NewProjectView.axaml", "Str.Dialog.NewProject.Button.Help")]
    [InlineData("Forms/Views/ReferencesView.axaml", "Str.Dialog.References.Help")]
    public void UnsupportedHelpEntryPoint_IsNotOffered(string view, string key)
    {
        var document = XDocument.Load(Path.Combine(ViewDirectory(), view));
        document.Descendants().SelectMany(element => element.Attributes())
            .Should().NotContain(attribute => attribute.Value == "{DynamicResource " + key + "}");
    }

    /// <summary>
    /// The Help menu's retired entries left their strings behind in every pack (#3387). A key nothing references
    /// is a translation every later language pays for, so these stay out.
    /// </summary>
    [Theory]
    [InlineData("Str.Menu.Help.Contents")]
    [InlineData("Str.Menu.Help.Index")]
    [InlineData("Str.Menu.Help.Search")]
    [InlineData("Str.Menu.Help.TechnicalSupport")]
    [InlineData("Str.Menu.Help.AvaloniaOnWeb")]
    public void RetiredHelpString_IsInNoLanguagePack(string key)
    {
        foreach (var pack in Directory.EnumerateFiles(Path.Combine(ViewDirectory(), "Localization", "Packs"), "*.json"))
            File.ReadAllText(pack).Should().NotContain('"' + key + '"', Path.GetFileName(pack));
    }

    [Theory]
    [InlineData("BrandHome", "BrandHomeMenuHeader", "HasBrandHome", "OpenBrandHomeCommand")]
    [InlineData("BrandSupport", "BrandSupportMenuHeader", "HasBrandSupport", "OpenBrandSupportCommand")]
    public void OptionalBrandLink_IsOfferedByHelp(string name, string header, string visible, string command)
    {
        var help = MainMenuDeclaration.Load().Single(item => item.Id == "help");
        var item = help.Children!.SingleOrDefault(item => item.LabelBinding == header);
        item.Should().NotBeNull(name);
        item!.VisibilityBinding.Should().Be(visible);
        item.Command.Should().Be("shell." + command);
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
