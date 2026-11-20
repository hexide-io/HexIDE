using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.Events;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Tools;

namespace HexIDE.Integration.Tests.Views;

public class FindReopenFocusTests
{
    // Reproduction and expected reuse rows: https://github.com/hexide-io/HexIDE/issues/5022
    [AvaloniaTheory]
    [InlineData("find")]
    [InlineData("replace")]
    [InlineData("button")]
    public void Reuse_activates_existing_window_and_focuses_Find_What(string action)
    {
        WithFindWindow((service, ide, dialog, findWhat, editor) =>
        {
            if (action == "button")
            {
                IdeDialogOwnershipTests.Activate(dialog);
                findWhat.Text = "needle";
                Dispatcher.UIThread.RunJobs();
                dialog.GetVisualDescendants().OfType<Button>().First().Focus().Should().BeTrue();
            }
            else
            {
                IdeDialogOwnershipTests.Activate(ide);
                editor.Focus().Should().BeTrue();
            }
            Dispatcher.UIThread.RunJobs();
            findWhat.IsFocused.Should().BeFalse();
            var activations = 0;
            dialog.Activated += (_, _) => activations++;

            if (action == "replace") service.ShowReplace();
            else service.ShowFind();
            Dispatcher.UIThread.RunJobs();

            activations.Should().Be(1);
            findWhat.IsFocused.Should().BeTrue();
            ide.OwnedWindows.Should().ContainSingle().Which.Should().BeSameAs(dialog);
        });
    }

    [AvaloniaFact]
    public void First_open_focuses_Find_What()
    {
        WithFindWindow((_, ide, dialog, findWhat, _) =>
        {
            dialog.Owner.Should().BeSameAs(ide);
            findWhat.IsFocused.Should().BeTrue();
            ide.OwnedWindows.Should().ContainSingle();
        });
    }

    private static void WithFindWindow(Action<FindReplaceService, Window, Window, TextBox, TextBox> check)
    {
        var editor = new TextBox();
        var ide = new Window { Content = editor };
        var desktop = Substitute.For<IClassicDesktopStyleApplicationLifetime>();
        desktop.MainWindow.Returns(ide);
        desktop.Windows.Returns(_ => new[] { ide }.Concat(ide.OwnedWindows).ToArray());
        var localization = Substitute.For<ILocalizationService>();
        var projects = Substitute.For<IProjectManager>();
        var editors = Substitute.For<IEditorService>();
        var explorer = new ProjectToolViewModel(projects, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), editors, localization);
        var service = new FindReplaceService(new WindowManager(localization),
            Substitute.For<IDocumentDockService>(), localization, projects, editors, () => explorer);
        var locator = new ViewLocator();
        Application.Current!.DataTemplates.Add(locator);
        var previousLifetime = Application.Current.ApplicationLifetime;
        var lifetimeField = typeof(Application).GetFields(BindingFlags.Instance | BindingFlags.NonPublic)
            .Single(field => field.FieldType == typeof(IApplicationLifetime));
        lifetimeField.SetValue(Application.Current, desktop);
        try
        {
            ide.Show();
            IdeDialogOwnershipTests.Activate(ide);
            Dispatcher.UIThread.RunJobs();
            service.ShowFind();
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            Dispatcher.UIThread.RunJobs();
            var dialog = ide.OwnedWindows.Single();
            var view = dialog.GetVisualDescendants().OfType<FindReplaceView>().Single();
            var findWhat = view.FindControl<TextBox>("FindWhat")!;
            check(service, ide, dialog, findWhat, editor);
        }
        finally
        {
            foreach (var dialog in ide.OwnedWindows.ToArray()) dialog.Close();
            ide.Close();
            Dispatcher.UIThread.RunJobs();
            lifetimeField.SetValue(Application.Current, previousLifetime);
            Application.Current.DataTemplates.Remove(locator);
        }
    }
}
