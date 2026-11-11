using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class ProjectPropertiesViewIntegrationTests
{

    /// <summary>
    /// A validator that objects to nothing. These tests are about the dialog's other fields; the naming
    /// rules have their own, in <c>ProjectNameRefusalTests</c>.
    /// </summary>
    private static string? NoNameObjection(string _) => null;
    [AvaloniaFact]
    public void GeneralPropertiesOnly_DisablesTheVb6Fields()
    {
        var vm = new ProjectPropertiesViewModel(new ProjectDefinition(VBProjectType.EXE, "Ledger"),
            NoNameObjection, generalPropertiesOnly: true);
        var view = new ProjectPropertiesView { DataContext = vm };
        var window = new Window { Content = view, Width = 500, Height = 500 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var controls = view.GetLogicalDescendants().OfType<Control>().Distinct().ToArray();
            controls.OfType<ComboBox>().Where(c => c.ItemsSource == vm.ProjectTypes || c.ItemsSource == vm.StartupObjects)
                .Should().HaveCount(2).And.OnlyContain(c => !c.IsEffectivelyEnabled);
            controls.OfType<TextBox>().Where(t => t.Text == vm.ProjectName || t.Text == vm.ProjectDescription)
                .Should().HaveCount(2).And.OnlyContain(t => t.IsEffectivelyEnabled);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Tab_WrapsThroughEveryEnabledGeneralControl(bool reverse)
    {
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var vm = new ProjectPropertiesViewModel(projectDef, NoNameObjection);
        var view = new ProjectPropertiesView { DataContext = vm };
        var window = new Window { Content = view, Width = 500, Height = 500 };
        window.Show();
        try
        {
            window.UpdateLayout();
            var controls = view.GetLogicalDescendants().OfType<Control>().Distinct().ToArray();
            var name = controls.OfType<TextBox>().Single(t => t.Text == vm.ProjectName);
            var description = controls.OfType<TextBox>().Single(t => t.IsEffectivelyEnabled && t != name);
            var ok = controls.OfType<Button>().Single(b => b.Command == vm.OkCommand);
            var cancel = controls.OfType<Button>().Single(b => b.Command == vm.CancelCommand);
            var general = controls.OfType<TabItem>().First();
            var combos = controls.OfType<ComboBox>().Where(c => c.IsEffectivelyEnabled).ToArray();
            var sequence = reverse
                ? new Control[] { combos[1], combos[0], general, cancel, ok, description, name }
                : new Control[] { description, ok, cancel, general, combos[0], combos[1], name };
            name.Focus();

            // Two laps catch the remembered last control that previously trapped the wrap (#3391).
            for (var lap = 0; lap < 2; lap++)
            {
                foreach (var expected in sequence)
                {
                    window.KeyPressQwerty(PhysicalKey.Tab, reverse ? RawInputModifiers.Shift : RawInputModifiers.None);
                    window.FocusManager!.GetFocusedElement().Should().BeSameAs(expected);
                }
            }
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void View_WithProjectDefinition_RendersWithoutErrors()
    {
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var vm = new ProjectPropertiesViewModel(projectDef, NoNameObjection);
        var view = new ProjectPropertiesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        view.Should().NotBeNull();
        view.Should().BeAssignableTo<UserControl>();
        view.IsMeasureValid.Should().BeTrue();
        view.IsArrangeValid.Should().BeTrue();
    }

    [AvaloniaFact]
    public void View_WithProjectContainingForms_RendersWithoutErrors()
    {
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "TestProject");
        var form1 = new FormDefinition(projectDef, FormComponentClass.Instance, "Form1");
        var form2 = new FormDefinition(projectDef, FormComponentClass.Instance, "Form2");
        projectDef.AddForm(form1);
        projectDef.AddForm(form2);

        var vm = new ProjectPropertiesViewModel(projectDef, NoNameObjection);
        var view = new ProjectPropertiesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.ProjectName.Should().Be("TestProject");
        // Sub Main plus the two forms. The list gained a non-form entry when Sub Main became a
        // selectable startup object (#210); this test is about the view rendering, so what matters here
        // is that every entry is present — ProjectPropertiesViewModelTests and SubMainStartupObjectTests
        // own the ordering and selection rules.
        vm.StartupObjects.Should().HaveCount(3);
        vm.StartupObjects.Select(o => o.Header).Should().Equal("Sub Main", "Form1", "Form2");
    }

    [AvaloniaFact]
    public void View_ProjectNameBindsCorrectly()
    {
        var projectDef = new ProjectDefinition(VBProjectType.EXE, "OriginalName");
        var vm = new ProjectPropertiesViewModel(projectDef, NoNameObjection);
        var view = new ProjectPropertiesView { DataContext = vm };

        view.Measure(new Size(800, 600));
        view.Arrange(new Rect(0, 0, 800, 600));

        vm.ProjectName.Should().Be("OriginalName");
        vm.SelectedProjectType.Should().Be(VBProjectType.EXE);
    }
}
