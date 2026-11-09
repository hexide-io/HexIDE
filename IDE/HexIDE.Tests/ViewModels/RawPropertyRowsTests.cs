using System.Text;
using Avalonia.Headless.XUnit;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;

namespace HexIDE.Tests.ViewModels;

public class RawPropertyRowsTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Raw_rows_follow_modelled_rows_filter_in_both_tabs_and_leave_files_unchanged(bool binary)
    {
        var project = TestHelpers.CreateProjectWithForm("P", "Form1");
        var original = project.Forms.First();
        var root = original.Components.First();
        root.UnknownRawProperties.Add(new("CarriedValue", [binary
            ? "   CarriedValue = \"Form1.frx\":0000"
            : "   CarriedValue = &H8000000F&"], binary ? new Dictionary<int, byte[]> { [0] = [1, 2, 3, 4] } : null));
        var saved = new FormSerializer().Serialize(original, "Form1.frm");
        var sourceBytes = Encoding.UTF8.GetBytes(saved.frmText);
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            saved.frmText, new Sink(), binary ? FrxDeserializer.Read(saved.frxContent!, saved.frmText) : null)!;
        var bus = new EventBus();
        var dock = Substitute.For<IDocumentDockService>();
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.Tool.Properties.Binary").Returns("(binary)");
        using var designer = new FormEditViewModel(null!, bus, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(), loc)
            .Initialize(form);
        dock.ActiveDocument.Returns(designer);
        var grid = new PropertiesToolViewModel(dock, bus, Substitute.For<IWindowManager>(), loc);

        var row = grid.CategorizedProperties.Should().ContainSingle(p => p.AccessibleName == "CarriedValue").Which;
        row.Should().NotBeOfType<PropertyViewModel>();
        grid.Properties.Last().Should().BeSameAs(row);
        grid.CategorizedProperties.Last().Should().BeSameAs(row);
        var value = row.GetType().GetProperty("Value")!;
        value.CanWrite.Should().BeFalse();
        value.GetValue(row).Should().Be(binary ? "(binary)" : "&H8000000F&");
        grid.GetType().GetProperty("SelectedProperty")!.SetValue(grid, row);
        grid.FilterText = "cArRiEd";
        grid.Properties.Should().ContainSingle().Which.Should().BeSameAs(row);
        grid.CategorizedProperties.Should().ContainSingle().Which.Should().BeSameAs(row);
        grid.FilterText = "Caption";
        grid.SelectedProperty.Should().BeNull();
        grid.Properties.Cast<object>().Should().NotContain(row);
        grid.FilterText = "";
        grid.Properties.Last().Should().BeSameAs(row);
        var notifications = new List<string?>();
        row.PropertyChanged += (_, e) => notifications.Add(e.PropertyName);
        loc.GetString("Str.Tool.Properties.Binary").Returns("(binaire)");
        loc.LanguageChanged += Raise.Event<Action>();
        value.GetValue(row).Should().Be(binary ? "(binaire)" : "&H8000000F&");
        notifications.Should().Contain("Value");

        designer.CommitLayout();
        var after = new FormSerializer().Serialize(form, "Form1.frm");
        Encoding.UTF8.GetBytes(after.frmText).Should().Equal(sourceBytes);
        if (binary)
            after.frxContent.Should().Equal(saved.frxContent);
        else
            after.frxContent.Should().BeNull();
    }
}
