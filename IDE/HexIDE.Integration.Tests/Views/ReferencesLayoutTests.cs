using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Integration.Tests.Views;

public class ReferencesLayoutTests
{
    [AvaloniaTheory]
    [InlineData(0)]
    [InlineData(200)]
    public void References_height_is_independent_of_library_count(int libraryCount)
    {
        var libraries = Substitute.For<IReferenceLibraryService>();
        libraries.GetAvailableReferences().Returns(Enumerable.Range(0, libraryCount)
            .Select(i => new AvailableReference($"library-{i}", "1.0", $"Library {i}", null)).ToArray());
        var vm = new ReferencesViewModel(new ProjectDefinition(VBProjectType.EXE, "P"),
            libraries, Substitute.For<IWindowManager>());
        var view = new ReferencesView { DataContext = vm };
        var window = new DialogWindow
        {
            Content = view, DataContext = vm, SizeToContent = SizeToContent.WidthAndHeight,
        };
        window.Show();
        try
        {
            window.UpdateLayout();
            view.Bounds.Height.Should().Be(400);
            if (libraryCount == 0)
                return;

            var list = view.GetVisualDescendants().OfType<ListBox>().Single();
            var scroll = list.GetVisualDescendants().OfType<ScrollViewer>().Single();
            scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height);
            list.ScrollIntoView(list.Items[^1]!);
            window.UpdateLayout();
            scroll.Offset.Y.Should().BeGreaterThan(0);
            view.Bounds.Height.Should().Be(400);
        }
        finally { window.Close(); }
    }
}
