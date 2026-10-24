using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// A double click on a designer's background writes the root's own event: a form's Form_Load (#1532), a UserControl's
/// UserControl_Initialize and a PropertyPage's PropertyPage_SelectionChanged (#1632). The designer wrote Form_Load for all
/// three. Measured in VB6's IDE on the oracle guest's console, on the release build and on SP6 alike (the oracle's "A
/// UserControl's and a PropertyPage's own events").
/// </summary>
public class RootDoubleClickTests
{
    private sealed class NullSink : IDeserializeErrorSink { public void LogError(string _) { } }

    private static FormEditViewModel Open(string kind, string name)
    {
        var file = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            $"VERSION 5.00\r\nBegin VB.{kind} {name} \r\n   ClientHeight    =   3600\r\n   ClientLeft      =   0\r\n"
          + $"   ClientTop       =   0\r\n   ClientWidth     =   4800\r\nEnd\r\nAttribute VB_Name = \"{name}\"\r\n", new NullSink())!;
        var localization = Substitute.For<ILocalizationService>();
        localization.GetString(Arg.Any<string>()).Returns(call => call.Arg<string>());
        return new FormEditViewModel(null!, Substitute.For<IEventBus>(), Substitute.For<HexIDE.Projects.IProjectService>(),
            null!, null!, Substitute.For<ISettingsService>(), localization).Initialize(file);
    }

    [AvaloniaTheory]
    [InlineData("Form", "Form1", "Form_Load")]
    [InlineData("UserControl", "UserControl1", "UserControl_Initialize")]
    [InlineData("PropertyPage", "PropertyPage1", "PropertyPage_SelectionChanged")]
    public void A_double_click_on_the_background_writes_the_roots_own_event(string kind, string name, string procedure) =>
        Open(kind, name).RootDoubleClickProcedure.Should().Be(procedure);
}
