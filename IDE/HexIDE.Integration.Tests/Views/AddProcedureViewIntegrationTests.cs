using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;

namespace HexIDE.Integration.Tests.Views;

public class AddProcedureViewIntegrationTests
{
    [AvaloniaFact]
    public void View_IsUserControl()
    {
        var view = new AddProcedureView();

        view.Should().NotBeNull();
        view.Should().BeAssignableTo<UserControl>();
    }

    [AvaloniaFact]
    public void View_WithDataContext_RendersWithoutErrors()
    {
        var vm = new AddProcedureViewModel();
        var view = new AddProcedureView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void View_SettingNameOnViewModel_DoesNotThrow()
    {
        var vm = new AddProcedureViewModel();
        var view = new AddProcedureView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.Name = "TestProcedure";

        view.InvalidateMeasure();
        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.Name.Should().Be("TestProcedure");
    }

    [AvaloniaFact]
    public void View_DefaultViewModel_HasExpectedDefaults()
    {
        var vm = new AddProcedureViewModel();
        var view = new AddProcedureView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.IsSub.Should().BeTrue();
        vm.IsPublic.Should().BeTrue();
        vm.Name.Should().BeEmpty();
    }
    [AvaloniaFact]
    public void Tab_walk_visits_selected_options_then_statics_and_buttons()
    {
        var view = new AddProcedureView { DataContext = new AddProcedureViewModel { Name = "Example" } };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var radios = view.GetVisualDescendants().OfType<RadioButton>().ToArray();
            view.FindControl<TextBox>("NameBox")!.Focus();
            var expected = new Control[] { radios[0], radios[4],
                view.GetVisualDescendants().OfType<CheckBox>().Single() }
                .Concat(view.GetVisualDescendants().OfType<Button>().Where(b => b is not RadioButton && b is not CheckBox));
            foreach (var control in expected)
            {
                Press(window, Key.Tab);
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(control);
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Arrows_select_within_group_and_Tab_reenters_at_selection()
    {
        var view = new AddProcedureView { DataContext = new AddProcedureViewModel { Name = "Example" } };
        var window = new Window { Content = view };
        window.Show();
        try
        {
            Dispatcher.UIThread.RunJobs();
            var radios = view.GetVisualDescendants().OfType<RadioButton>().ToArray();
            radios[0].Focus();
            Press(window, Key.Right);
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(radios[1]);
            radios[1].IsChecked.Should().BeTrue();
            Press(window, Key.Left);
            radios[0].IsChecked.Should().BeTrue();
            Press(window, Key.Down);
            radios[1].IsChecked.Should().BeTrue();
            Press(window, Key.Up);
            radios[0].IsChecked.Should().BeTrue();
            radios[5].IsChecked = true;
            Press(window, Key.Tab);
            window.FocusManager.GetFocusedElement().Should().BeSameAs(radios[5]);
            Press(window, Key.Left);
            radios[4].IsChecked.Should().BeTrue();
            view.FindControl<TextBox>("NameBox")!.Focus();
            radios[2].IsChecked = true;
            Press(window, Key.Tab);
            window.FocusManager.GetFocusedElement().Should().BeSameAs(radios[2]);
        }
        finally { window.Close(); }
    }

    private static void Press(Window window, Key key)
    {
        window.KeyPress(key, RawInputModifiers.None, Physical(key), null);
        window.KeyRelease(key, RawInputModifiers.None, Physical(key), null);
        Dispatcher.UIThread.RunJobs();
    }
    private static PhysicalKey Physical(Key key) => key switch
    {
        Key.Tab => PhysicalKey.Tab,
        Key.Left => PhysicalKey.ArrowLeft,
        Key.Right => PhysicalKey.ArrowRight,
        Key.Up => PhysicalKey.ArrowUp,
        Key.Down => PhysicalKey.ArrowDown,
        _ => PhysicalKey.None,
    };
}
