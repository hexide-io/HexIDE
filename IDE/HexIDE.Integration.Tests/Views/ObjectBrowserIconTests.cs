using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Integration.Tests.Views;

public class ObjectBrowserIconTests
{
    private static SymbolInformation Symbol() => new("Example", SymbolKind.Property,
        new HexIDE.Lsp.Messages.Location("file:///Example.bas", new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 1))));

    private static IImage Icon(object row)
    {
        var property = row.GetType().GetProperty("KindIcon");
        property.Should().NotBeNull("Object Browser rows must expose an image rather than a text glyph (#11)");
        return property!.GetValue(row).Should().BeAssignableTo<IImage>().Subject;
    }

    private static void HasGeometry(IImage icon, string key)
    {
        var drawing = icon.Should().BeOfType<DrawingImage>().Subject.Drawing
            .Should().BeOfType<GeometryDrawing>().Subject;
        Application.Current!.Resources.TryGetResource(key, Application.Current.ActualThemeVariant, out var geometry)
            .Should().BeTrue();
        var expected = geometry.Should().BeAssignableTo<Geometry>().Subject;
        drawing.Geometry.Should().NotBeNull();
        drawing.Geometry!.Bounds.Should().Be(expected.Bounds);
        // Other tests replace the application's resource dictionary; cached images keep the original
        // geometry instance. Compare its silhouette, independent of that dictionary's lifetime.
        Silhouette(drawing.Geometry).Should().Equal(Silhouette(expected));
        drawing.Geometry.Bounds.Width.Should().BeGreaterThan(0);
        drawing.Brush.Should().BeAssignableTo<ISolidColorBrush>();
    }

    private static IEnumerable<bool> Silhouette(Geometry geometry)
    {
        for (var y = 0; y < 40; y++)
        for (var x = 0; x < 40; x++)
            yield return geometry.FillContains(new Point(x / 2.0, y / 2.0));
    }

    [AvaloniaTheory]
    [InlineData(OBClassKind.Form, "Geo.Form")]
    [InlineData(OBClassKind.Module, "Geo.Addmodule")]
    [InlineData(OBClassKind.ClassModule, "Geo.Addclassmodule")]
    [InlineData(OBClassKind.Enum, "Geo.Listconstants")]
    [InlineData((OBClassKind)999, "Geo.Generic")]
    public void ClassKinds_UseCachedThemedGeometry(OBClassKind kind, string key)
    {
        var row = new OBClassViewModel("Example", kind, "Project1");
        var icon = Icon(row);
        HasGeometry(icon, key);
        Icon(new OBClassViewModel("Other", kind, "Project1")).Should().BeSameAs(icon);
    }

    [AvaloniaTheory]
    [InlineData(OBMemberKind.Method, "Geo.Addprocedure")]
    [InlineData(OBMemberKind.Property, "Geo.Properties")]
    [InlineData(OBMemberKind.Event, "Geo.Event")]
    [InlineData(OBMemberKind.Constant, "Geo.Lockcontrols")]
    [InlineData(OBMemberKind.EnumMember, "Geo.Lockcontrols")]
    [InlineData((OBMemberKind)999, "Geo.Generic")]
    public void MemberKinds_AndSearchResults_ShareCachedThemedGeometry(OBMemberKind kind, string key)
    {
        var row = new OBMemberViewModel("Example", kind, "Sub Example()");
        var icon = Icon(row);
        HasGeometry(icon, key);
        Icon(new OBMemberViewModel("Other", kind, "Sub Other()")).Should().BeSameAs(icon);
        Icon(new OBSearchResultViewModel(Symbol(), kind)).Should().BeSameAs(icon);
    }

    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public void ListTemplates_BindImages(int listIndex)
    {
        var view = new ObjectBrowserToolView();
        var window = new Window { Width = 800, Height = 500, Content = view };
        window.Show();
        try
        {
            window.UpdateLayout();
            var list = view.GetVisualDescendants().OfType<ListBox>().ElementAt(listIndex);
            object row = listIndex switch
            {
                0 => new OBSearchResultViewModel(Symbol(), OBMemberKind.Property),
                1 => new OBClassViewModel("Example", OBClassKind.Form, "Project1"),
                _ => new OBMemberViewModel("Example", OBMemberKind.Property, "Property Example As String")
            };
            var control = list.ItemTemplate!.Build(row)!;
            control.DataContext = row;
            window.Content = control;
            window.UpdateLayout();
            var image = control.GetVisualDescendants().OfType<Image>().Should().ContainSingle().Which;
            image.Source.Should().BeSameAs(Icon(row));
            image.Width.Should().Be(16);
            image.Height.Should().Be(16);
            control.GetVisualDescendants().OfType<TextBlock>().Select(t => t.Text).Should().Contain("Example");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DescriptionAndCopiedSignature_ContainOnlyTheSignature()
    {
        var pm = Substitute.For<IProjectManager>();
        pm.LoadedProjects.Returns(new List<ProjectDefinition>());
        var vm = new ObjectBrowserToolViewModel(pm, Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(),
            Substitute.For<ILocalizationService>(), Substitute.For<ILanguageConnectionRegistry>());
        vm.SelectedMember = new OBMemberViewModel("Example", OBMemberKind.Method, "Sub Example()");
        // Copy Signature uses this property verbatim.
        vm.DescriptionSignature.Should().Be("Sub Example()");
        vm.SelectedMember = null;
        vm.DescriptionSignature.Should().BeEmpty();
    }
}
