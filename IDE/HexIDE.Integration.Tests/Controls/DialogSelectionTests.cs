using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.VisualTree;
using Classic.CommonControls;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Controls;

public class DialogSelectionTests
{
    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void Selected_file_uses_theme_selection_colours(bool conflict, bool focused)
    {
        UserControl view;
        if (conflict)
        {
            var model = new FileConflictViewModel();
            model.Add("Module1.bas");
            view = new FileConflictView { DataContext = model };
        }
        else
        {
            var model = new SaveChangesViewModel();
            model.ChangedFiles.Add(new ChangedFileViewModel(new ProjectDefinition(VBProjectType.EXE, "Project1")));
            view = new SaveChangesView { DataContext = model };
        }

        var window = new Window { Content = view, Width = 500, Height = 350 };
        var background = new SolidColorBrush(Colors.DarkBlue);
        var foreground = new SolidColorBrush(Colors.Yellow);
        window.Resources[SystemColors.HighlightBrushKey] = background;
        window.Resources[SystemColors.HighlightTextBrushKey] = foreground;
        try
        {
            window.Show();
            window.UpdateLayout();
            var list = view.GetVisualDescendants().OfType<ListBox>().Single();
            list.SelectedIndex = 0;
            window.UpdateLayout();
            var row = (ListBoxItem)list.ContainerFromIndex(0)!;
            if (focused)
                row.Focus().Should().BeTrue();
            else
                view.GetVisualDescendants().OfType<Button>().First().Focus().Should().BeTrue();
            row.IsKeyboardFocusWithin.Should().Be(focused);
            var presenter = row.GetVisualDescendants().OfType<ContentPresenter>()
                .Single(p => p.Name == "PART_ContentPresenter");
            row.Foreground.Should().BeSameAs(foreground);
            presenter.Background.Should().BeSameAs(background);
        }
        finally { window.Close(); }
    }
}
