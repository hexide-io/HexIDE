using HexIDE.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Raw;
using Avalonia.Labs.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using AvaloniaEdit;
using RoutedCommand = Avalonia.Labs.Input.RoutedCommand;
using HexIDE.Bookmarks;
using HexIDE.Debugging;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Integration.Tests.Views;

public class ScrollingCommandMenuTests
{
    [AvaloniaTheory]
    [InlineData(false, false, Key.Down)]
    [InlineData(false, true, Key.Down)]
    [InlineData(true, false, Key.Down)]
    [InlineData(true, true, Key.Down)]
    [InlineData(false, true, Key.Up)]
    [InlineData(true, true, Key.Up)]
    public void ContextRequestSelectsItemsAccordingToOpeningInput(bool contextMenu, bool pointer, Key arrow)
    {
        var owner = new TextBox { Text = "target" };
        var window = new Window { Content = owner, Width = 400, Height = 240 };
        var command = new RoutedCommand("OpeningInputTest");
        var executions = 0;
        CommandManager.SetCommandBindings(owner, new List<CommandBinding>
        {
            new(command, (_, e) => { executions++; e.Handled = true; },
                (_, e) => { e.CanExecute = true; e.Handled = true; })
        });
        var items = new[]
        {
            new MenuItem { Header = "Hidden", IsVisible = false },
            new MenuItem { Header = "Disabled", IsEnabled = false },
            new MenuItem { Header = "First", Command = command },
            new MenuItem { Header = "Last", Command = command }
        };
        var menu = new ContextMenu();
        var flyout = new MenuFlyout();
        foreach (var item in items)
            if (contextMenu) menu.Items.Add(item); else flyout.Items.Add(item);
        if (contextMenu) owner.ContextMenu = menu; else owner.ContextFlyout = flyout;
        try
        {
            window.Show();
            Open(pointer);
            var presenter = items[2].GetVisualAncestors().OfType<MenuBase>().First();
            if (pointer)
            {
                items.Should().OnlyContain(i => !i.IsSelected);
                window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
                executions.Should().Be(0);
                presenter.IsVisible.Should().BeTrue();
                window.KeyPress(arrow, RawInputModifiers.None, PhysicalKey.None, null);
                Dispatcher.UIThread.RunJobs();
            }
            var selected = pointer && arrow == Key.Up ? items[3] : items[2];
            selected.IsSelected.Should().BeTrue();
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(selected);
            items[2].IsEffectivelyEnabled.Should().BeTrue();
            items[3].IsEffectivelyEnabled.Should().BeTrue();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            executions.Should().Be(1);
            Open(true);
            items.Should().OnlyContain(i => !i.IsSelected);
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            executions.Should().Be(1);
            menu.Close();
            flyout.Hide();
            Open(false);
            items[2].IsSelected.Should().BeTrue();
            window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
            executions.Should().Be(2);
        }
        finally
        {
            menu.Close();
            flyout.Hide();
            window.Close();
        }

        void Open(bool byPointer)
        {
            owner.Focus();
            Dispatcher.UIThread.RunJobs();
            if (byPointer)
            {
                window.MouseDown(new Point(200, 120), MouseButton.Right);
                window.MouseUp(new Point(200, 120), MouseButton.Right);
            }
            else
                owner.RaiseEvent(new ContextRequestedEventArgs());
            Dispatcher.UIThread.RunJobs();
        }
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void CodeMenuOpensOnFirstEnabledItemAndKeepsEditorCommandTarget(bool openingFocus)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var settings = Substitute.For<ISettingsService>();
        settings.TabWidth.Returns(4);
        var bookmarks = new BookmarkService();
        var client = Substitute.For<ILspClient>();
        client.IsRunning.Returns(true);
        client.SupportsCallHierarchy(Arg.Any<string>()).Returns(true);
        var vm = new CodeEditorViewModel(Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), Substitute.For<IEventBus>(), client, settings,
            Substitute.For<IStatusBarService>(), bookmarks, new BreakpointService(),
            Substitute.For<IDebugController>(), new RunScope(), localization, new Vb6ProjectFileFormat(), new Vb6ReadOnlyRegionReader());
        var module = new ModuleDefinition(new ProjectDefinition(VBProjectType.EXE, "P"), "Module1", ModuleKind.StandardModule);
        module.AbsolutePath = Path.Combine(Path.GetTempPath(), "3155", "Module1.bas");
        module.UpdateCode("Sub Main()\r\nEnd Sub\r\n");
        vm.Initialize(module);
        var view = new CodeEditorView { DataContext = vm };
        var window = new Window { Content = view, Width = 800, Height = 240 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var editor = view.FindControl<TextEditor>("TextEditor")!;
            var flyout = (MenuFlyout)editor.ContextFlyout!;
            // Popups use the screen's bounds rather than the host's. Constrain the presenter to the
            // short host's height to exercise the same scrolling template on the headless screen.
            flyout.FlyoutPresenterTheme = new ControlTheme(typeof(MenuFlyoutPresenter))
            {
                BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(MenuFlyoutPresenter))!,
                Setters = { new Setter(Layoutable.MaxHeightProperty, 180d) }
            };
            editor.TextArea.Focus().Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            vm.CanShowCallHierarchy.Should().BeTrue();
            ((System.Windows.Input.ICommand)ApplicationCommands.CutCommand).CanExecute(null).Should().BeTrue(
                "the focused target is {0}", CommandManager.FocusedElement?.GetType().Name);
            flyout.ShowAt(editor);
            Dispatcher.UIThread.RunJobs();
            var items = flyout.Items.OfType<MenuItem>().ToArray();
            var presenter = items[0].GetVisualAncestors().OfType<MenuFlyoutPresenter>().Single();
            var scroll = presenter.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height);
            var expected = items.Where(i => i.Command is RoutedCommand command &&
                command != ApplicationCommands.DisabledCommand && command.CanExecute(i.CommandParameter, editor)).ToArray();
            expected.Length.Should().BeGreaterThan(10);
            if (openingFocus)
            {
                window.FocusManager!.GetFocusedElement().Should().BeSameAs(items.First(i => i.IsEffectivelyEnabled));
                return;
            }
            expected.Where(i => !i.IsEffectivelyEnabled).Select(i => ((RoutedCommand)i.Command!).Name)
                .Should().BeEmpty("the focused target is {0}", CommandManager.FocusedElement?.GetType().Name);
            var button = presenter.GetVisualDescendants().OfType<RepeatButton>().First(b => b.IsVisible && b.IsEffectivelyEnabled);
            button.Focus();
            Dispatcher.UIThread.RunJobs();
            expected.Where(i => !i.IsEffectivelyEnabled).Select(i => ((RoutedCommand)i.Command!).Name)
                .Should().BeEmpty("the focused target is {0}", CommandManager.FocusedElement?.GetType().Name);
            var bookmark = items.Single(i => i.Items.OfType<MenuItem>().Any(child => child.Command == ApplicationCommands.ToggleBookmarkCommand))
                .Items.OfType<MenuItem>().First();
            bookmark.Command!.Execute(bookmark.CommandParameter);
            bookmarks.GetBookmarks(vm.Identity).Should().NotBeEmpty();
        }
        finally
        {
            view.FindControl<TextEditor>("TextEditor")!.ContextFlyout!.Hide();
            window.Close();
            vm.Dispose();
        }
    }
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ScrollingMenusKeepTheirRoutedTargetWhenAScrollButtonGetsFocus(bool contextMenu)
    {
        var owner = new TextBox { Text = "target" };
        var window = new Window { Content = owner, Width = 800, Height = 240 };
        var command = new RoutedCommand("ScrollingMenuTest");
        var executions = 0;
        CommandManager.SetCommandBindings(owner, new List<CommandBinding>
        {
            new(command, (_, e) => { executions++; e.Handled = true; },
                (_, e) => { e.CanExecute = true; e.Handled = true; })
        });
        var items = Enumerable.Range(0, 30).Select(i => new MenuItem { Header = $"Item {i}", Command = command }).ToArray();
        var menu = new ContextMenu { MaxHeight = 180 };
        var flyout = new MenuFlyout
        {
            FlyoutPresenterTheme = new ControlTheme(typeof(MenuFlyoutPresenter))
            {
                BasedOn = (ControlTheme)Application.Current!.FindResource(typeof(MenuFlyoutPresenter))!,
                Setters = { new Setter(Layoutable.MaxHeightProperty, 180d) }
            }
        };
        foreach (var item in new[]
                 {
                     new MenuItem { Header = "Hidden", Command = command, IsVisible = false },
                     new MenuItem { Header = "Disabled", Command = command, IsEnabled = false }
                 }.Concat(items))
            if (contextMenu) menu.Items.Add(item); else flyout.Items.Add(item);
        var children = Enumerable.Range(0, 100).Select(i => new MenuItem { Header = $"Child {i}", Command = command }).ToArray();
        var submenu = new MenuItem { Header = "Submenu" };
        foreach (var child in children) submenu.Items.Add(child);
        if (contextMenu) menu.Items.Add(submenu); else flyout.Items.Add(submenu);
        try
        {
            window.Show();
            owner.Focus();
            Dispatcher.UIThread.RunJobs();
            if (contextMenu) menu.Open(owner); else flyout.ShowAt(owner);
            Dispatcher.UIThread.RunJobs();
            var presenter = items[0].GetVisualAncestors().OfType<MenuBase>().First();
            var scroll = presenter.GetVisualDescendants().OfType<ScrollViewer>().First();
            scroll.Extent.Height.Should().BeGreaterThan(scroll.Viewport.Height);
            window.FocusManager!.GetFocusedElement().Should().BeSameAs(items[0]);
            presenter.GetVisualDescendants().OfType<RepeatButton>().First(b => b.IsVisible).Focus().Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            items.Should().OnlyContain(i => i.IsEffectivelyEnabled);
            items[0].Command!.Execute(null);
            executions.Should().Be(1);
            submenu.IsSubMenuOpen = true;
            Dispatcher.UIThread.RunJobs();
            var submenuScroll = children[0].GetVisualAncestors().OfType<ScrollViewer>().First();
            submenuScroll.Extent.Height.Should().BeGreaterThan(submenuScroll.Viewport.Height);
            submenuScroll.GetVisualDescendants().OfType<RepeatButton>().First(b => b.IsVisible).Focus().Should().BeTrue();
            Dispatcher.UIThread.RunJobs();
            children.Should().OnlyContain(i => i.IsEffectivelyEnabled);
            children[0].Command!.Execute(null);
            executions.Should().Be(2);
        }
        finally
        {
            menu.Close();
            flyout.Hide();
            window.Close();
        }
    }

}
