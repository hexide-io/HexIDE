using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;

namespace HexIDE.Tests.Infrastructure;

/// <summary>
/// Esc does what Cancel does, as it does in every VB6 dialog (#3382).
/// </summary>
/// <remarks>
/// Avalonia gives Esc to a button only if the button is marked <c>IsCancel</c>, and nothing else in a dialog answers
/// it, so without the mark Esc goes to the focused control and the dialog stays open. This reads the views rather than
/// opening them: a dialog nobody has opened in a test is exactly the one that would be missed.
/// </remarks>
public class DialogCancelButtonTests
{
    [Fact]
    public void Every_Cancel_button_is_the_dialogs_cancel_button()
    {
        var cancels = CancelButtons().ToList();

        // Seventeen at the time of writing. A scan that stopped finding them would pass every assertion below.
        cancels.Should().HaveCountGreaterThanOrEqualTo(15);
        cancels.Where(button => (string?)button.Element.Attribute("IsCancel") != "True").Select(button => button.Place)
            .Should().BeEmpty("a Cancel button without IsCancel=\"True\" leaves Esc with the focused control");
    }

    private static IEnumerable<(XElement Element, string Place)> CancelButtons()
    {
        var ide = IdeDirectory();
        foreach (var file in Directory.EnumerateFiles(ide, "*.axaml", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(ide, file);
            if (relative.Split(Path.DirectorySeparatorChar) is var parts && (parts.Contains("bin") || parts.Contains("obj")))
                continue;
            var document = XDocument.Load(file, LoadOptions.SetLineInfo);
            foreach (var button in document.Descendants().Where(e => e.Name.LocalName == "Button" && IsCancel(e)))
                yield return (button, $"{relative}:{((IXmlLineInfo)button).LineNumber}");
        }
    }

    private static bool IsCancel(XElement button) =>
        button.Attributes().Any(a =>
            (a.Name.LocalName is "Content" && (a.Value == "Cancel" || a.Value.EndsWith(".Cancel}", StringComparison.Ordinal)))
            || (a.Name.LocalName is "Command" or "Click" && CancelAction.IsMatch(a.Value)));

    private static readonly Regex CancelAction = new(@"\bCancel(Click|Command)?\}?$", RegexOptions.CultureInvariant);

    private static string IdeDirectory()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var ide = Path.Combine(dir.FullName, "IDE");
            if (Directory.Exists(Path.Combine(ide, "HexIDE", "Forms", "Views")))
                return ide;
        }
        throw new DirectoryNotFoundException("Could not locate the IDE sources from the test's output directory");
    }
}
