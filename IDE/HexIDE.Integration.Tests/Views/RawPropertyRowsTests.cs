using System;
using System.Linq;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using Classic.CommonControls;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Views;

public class RawPropertyRowsTests
{
    private sealed class Sink : IDeserializeErrorSink
    {
        public void LogError(string message) => throw new InvalidOperationException(message);
    }

    [AvaloniaTheory]
    [InlineData("AlphabeticPropertiesTab")]
    [InlineData("CategorizedPropertiesTab")]
    public void Raw_rows_use_grey_text_without_editors(string tab)
    {
        const string source = "VERSION 5.00\r\nBegin VB.Form Form1 \r\n   CarriedBinary = \"Form1.frx\":0000\r\n   CarriedValue = 5\r\nEnd\r\nAttribute VB_Name = \"Form1\"\r\n";
        byte[] companion = [1, 2, 3, 4];
        var form = new FormDeserializer().Deserialize(new ProjectDefinition(VBProjectType.EXE, "P"),
            source, new Sink(), FrxDeserializer.Read(companion, source))!;
        var bus = new EventBus();
        var loc = Substitute.For<ILocalizationService>();
        loc.GetString("Str.Tool.Properties.Binary").Returns("(binary)");
        using var designer = new FormEditViewModel(null!, bus, Substitute.For<IProjectService>(),
            Substitute.For<IEditorService>(), Substitute.For<IWindowManager>(), Substitute.For<ISettingsService>(), loc, new Vb6ProjectFileFormat())
            .Initialize(form);
        var dock = Substitute.For<IDocumentDockService>();
        dock.ActiveDocument.Returns(designer);
        var grid = new PropertiesToolViewModel(dock, bus, Substitute.For<IWindowManager>(), loc, new Vb6ProjectFileFormat());
        var view = new PropertiesToolView { DataContext = grid };
        var window = new Window { Content = view, Width = 400, Height = 400 };
        try
        {
            window.Show();
            view.FindControl<TabItem>(tab)!.IsSelected = true;
            grid.FilterText = "Carried";
            window.UpdateLayout();
            grid.CategorizedProperties.Should().HaveCount(2);
            var row = grid.CategorizedProperties.Single(p => p.AccessibleName == "CarriedValue");
            grid.GetType().GetProperty("SelectedProperty")!.SetValue(grid, row);
            window.UpdateLayout();
            var list = view.FindControl<ListBox>(tab == "AlphabeticPropertiesTab" ? "AlphabeticProperties" : "CategorizedProperties")!;
            var texts = list.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.IsEffectivelyVisible && ReferenceEquals(t.DataContext, row)).ToArray();
            texts.Select(t => t.Text).Should().BeEquivalentTo(["CarriedValue", "5"]);
            view.TryFindResource(SystemColors.GrayTextBrushKey, out var grey).Should().BeTrue();
            texts.Should().OnlyContain(t => Equals(t.Foreground, grey));
            list.GetVisualDescendants().Where(c => grid.CategorizedProperties.Any(r => ReferenceEquals(c.DataContext, r)))
                .Should().NotContain(c => c is TextBox || c is ComboBox || c is Button);
            list.SelectedItem.Should().BeSameAs(row);
            list.GetVisualDescendants().OfType<TextBlock>().Where(t => t.IsEffectivelyVisible)
                .Select(t => t.Text).Should().Contain("(binary)");
            designer.CommitLayout();
            var saved = new FormSerializer().Serialize(form, "Form1.frm");
            Encoding.UTF8.GetBytes(saved.frmText).Should().Equal(Encoding.UTF8.GetBytes(source));
            saved.frxContent.Should().Equal(companion);
        }
        finally { window.Close(); }
    }
}
