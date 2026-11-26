using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Integration.Tests.Views;

public class TabStripKeyboardTests
{
    [AvaloniaFact]
    public void TabStrip_is_excluded_from_designer_container_classes()
    {
        TabStripComponentClass.Instance.IsContainer.Should().BeFalse();
    }
}
