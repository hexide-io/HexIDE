using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Automation;

public class ReferencesAutomationNameTests
{
    [AvaloniaTheory]
    [InlineData(0, "MoveUp", "Move selected reference up")]
    [InlineData(1, "MoveDown", "Move selected reference down")]
    public void Priority_buttons_have_localized_directional_names(int index, string action, string expected)
    {
        var window = ToolbarAutomationNameTests.ShowMainView();
        try
        {
            var view = new ReferencesView();
            window.Content = view;
            Dispatcher.UIThread.RunJobs();
            var buttons = view.GetVisualDescendants().OfType<Button>()
                .Where(b => b.Content is Avalonia.Controls.Shapes.Path).ToList();
            buttons.Should().HaveCount(2);
            var peer = ControlAutomationPeer.CreatePeerForElement(buttons[index]);
            peer.GetName().Should().Be(expected);

            window.Resources["Str.Dialog.References." + action] = "⟦" + action + "⟧";
            Dispatcher.UIThread.RunJobs();
            peer.GetName().Should().Be("⟦" + action + "⟧");
        }
        finally { window.Close(); }
    }
}
