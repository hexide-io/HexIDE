using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Integration.Tests.Automation;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Guards hexide-io/HexIDE#960: the status bar's position and INS/OVR cells hold their whole text in any language.
/// </summary>
/// <remarks>
/// Both cells had fixed widths sized for English, so a longer translation was cut off at the cell's edge. The
/// longest shipped labels (Urdu's overtype, Indonesian's, Hindi's) were never seen on screen, and Pseudo's INS
/// showed the cut. The cells now grow to their text, and the message cell beside them gives way, as it trims.
/// The text is set directly because the view has no view model here; the layout is what is under test.
/// </remarks>
public class StatusBarFitsItsTextTests
{
    [AvaloniaFact]
    public void A_long_label_widens_its_cell_instead_of_being_cut_off()
    {
        var window = ToolbarAutomationNameTests.ShowMainView();
        try
        {
            var statusBar = window.GetVisualDescendants().OfType<Control>()
                .Single(c => Avalonia.Automation.AutomationProperties.GetAutomationId(c) == "StatusBar");
            var labels = statusBar.GetVisualDescendants().OfType<TextBlock>().ToList();
            labels.Should().HaveCount(3, "the status bar holds the message, the caret's position and INS/OVR");

            // The message cell trims by design; the other two must show all of what they are given.
            foreach (var label in labels.Where(l => l.TextTrimming == TextTrimming.None))
                label.Text = "⟦Ŀóñĝ łábéł ŵíţħ ŕóóm ţó ŝpáŕé⟧";
            Dispatcher.UIThread.RunJobs();

            foreach (var label in labels.Where(l => l.TextTrimming == TextTrimming.None))
            {
                var probe = new TextBlock { Text = label.Text, FontSize = label.FontSize, FontFamily = label.FontFamily };
                probe.Measure(Size.Infinity);
                label.Bounds.Width.Should().BeGreaterThanOrEqualTo(probe.DesiredSize.Width - 0.5,
                    "a cell narrower than its text cuts the end off, which a translation longer than English hits");
            }
        }
        finally
        {
            window.Close();
        }
    }
}
