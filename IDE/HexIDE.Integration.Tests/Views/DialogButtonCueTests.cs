using System.IO;
using Avalonia;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.LogicalTree;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Views;

public class DialogButtonCueTests
{
    [AvaloniaTheory]
    [InlineData("Classic")]
    [InlineData("Dark")]
    [InlineData("Abyss")]
    public async Task ServerPromptKeyboardFocusChangesRenderedFrame(string theme)
    {
        var themes = new ThemeService();
        themes.Apply(theme);
        var owner = new Window();
        owner.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var pending = ServerMessagePrompt.Show(owner, "Server", new(3, "Choose", [new("First"), new("Second")]), CancellationToken.None);
            Dispatcher.UIThread.RunJobs();
            if (pending.IsFaulted) await pending;
            var prompt = owner.OwnedWindows.Single();
            try
            {
                var button = prompt.GetVisualDescendants().OfType<Button>().First();
                button.IsFocused.Should().BeTrue();
                prompt.GetVisualDescendants().OfType<AdornerLayer>().Single()
                    .Children.OfType<Rectangle>().Should().ContainSingle();
                prompt.GetVisualDescendants().OfType<Button>().Last().Focus(NavigationMethod.Pointer);
                button.Focus(NavigationMethod.Pointer);
                var pointer = Frame(prompt);
                prompt.GetVisualDescendants().OfType<Button>().Last().Focus(NavigationMethod.Pointer);
                var unfocused = Frame(prompt);
                button.Focus(NavigationMethod.Tab);
                var keyboard = Frame(prompt);
                pointer.Should().Equal(unfocused);
                keyboard.Should().NotEqual(pointer);
                keyboard.Should().NotEqual(unfocused);
                var cue = prompt.GetVisualDescendants().OfType<AdornerLayer>().Single()
                    .Children.OfType<Rectangle>().Single();
                cue.Margin.Should().Be(new Avalonia.Thickness(4));
                cue.Stroke.Should().Be(button.FindResource(Classic.CommonControls.SystemColors.ControlTextBrushKey)
                    .Should().BeAssignableTo<IBrush>().Subject);
            }
            finally { prompt.Close(); }
            await pending;
        }
        finally { owner.Close(); themes.Apply("Classic"); }
    }

    [AvaloniaTheory]
    [InlineData("Classic")]
    [InlineData("Dark")]
    [InlineData("Abyss")]
    public void MessageBoxDefaultButtonHasHeavierRenderedBorder(string theme)
    {
        var themes = new ThemeService();
        themes.Apply(theme);
        var box = new HexIDE.Runtime.Dialogs.MessageBox { Text = "Choose", Buttons = MessageBoxButtons.YesNo };
        var window = new Window { Content = box, Width = 360, Height = 180 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = box.GetVisualDescendants().OfType<Button>().First();
            button.IsDefault.Should().BeTrue();
            button.BorderThickness.Should().Be(new Avalonia.Thickness(2));
            var defaultFrame = Frame(window);
            button.IsDefault = false;
            Frame(window).Should().NotEqual(defaultFrame);
        }
        finally { window.Close(); themes.Apply("Classic"); }
    }

    public static System.Collections.Generic.IEnumerable<object[]> HostedDialogs()
    {
        var views = new[]
        {
            "SaveChangesView", "FindReplaceView", "AddWatchDialog", "ComponentsView",
            "RecoveryDialogView", "RuntimeErrorView", "ReferencesView", "ProjectPropertiesView",
            "FileConflictView", "AddProcedureView", "LanguageRevertGateView", "AddinConsentDialog",
            "WorkspaceArtifactConsentView", "ExportPreviewDialog", "TrustChainView", "MenuEditorView",
            "AboutDialog", "FontDialog", "ColorDialog", "PrintDialog", "InputBox"
        };
        foreach (var view in views)
        foreach (var theme in new[] { "Classic", "Dark", "Abyss" })
            yield return new object[] { view, theme };
    }

    [AvaloniaTheory]
    [MemberData(nameof(HostedDialogs))]
    public void HostedDialogButtonsRenderThemeCues(string view, string theme)
    {
        var themes = new ThemeService();
        themes.Apply(theme);
        var runtime = view is "ColorDialog" or "PrintDialog" or "InputBox";
        var assembly = runtime ? typeof(HexIDE.Runtime.Dialogs.InputBox).Assembly : typeof(DialogWindow).Assembly;
        var prefix = runtime ? "HexIDE.Runtime.Dialogs." : view == "MenuEditorView"
            ? "HexIDE.VisualDesigner." : "HexIDE.Forms.Views.";
        var content = (Control)System.Activator.CreateInstance(assembly.GetType(prefix + view)!)!;
        var factory = runtime || view is "AboutDialog" or "FontDialog";
        var window = factory
            ? new WindowManager(Substitute.For<HexIDE.Localization.ILocalizationService>()).MakeDialogWindow("Title", content)
            : new DialogWindow { Content = content };
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 800;
        window.Height = 600;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var button = content.GetLogicalDescendants().OfType<Button>().Last(button => button.GetType() == typeof(Button));
            // Exercise the host's styling independently of each view model's command state/default policy.
            var command = Substitute.For<System.Windows.Input.ICommand>();
            command.CanExecute(Arg.Any<object?>()).Returns(true);
            button.Command = command;
            button.Focusable = true;
            button.IsEnabled = true;
            button.IsVisible = true;
            foreach (var parent in button.GetVisualAncestors().OfType<Control>())
            {
                parent.IsEnabled = true;
                parent.IsVisible = true;
            }
            button.IsDefault = true;
            Dispatcher.UIThread.RunJobs();
            button.IsEffectivelyEnabled.Should().BeTrue();
            button.IsEffectivelyVisible.Should().BeTrue();
            button.BorderThickness.Should().Be(new Thickness(2));
            button.Focus(NavigationMethod.Pointer);
            button.IsFocused.Should().BeTrue();
            var pointer = Frame(window);
            window.Focusable = true;
            window.Focus(NavigationMethod.Pointer);
            button.Focus(NavigationMethod.Tab);
            button.IsFocused.Should().BeTrue();
            var keyboard = Frame(window);
            keyboard.Should().NotEqual(pointer);
            var cue = window.GetVisualDescendants().OfType<AdornerLayer>()
                .SelectMany(layer => layer.Children).OfType<Rectangle>().Single();
            cue.Stroke.Should().Be(button.FindResource(Classic.CommonControls.SystemColors.ControlTextBrushKey)
                .Should().BeAssignableTo<IBrush>().Subject);
            button.IsDefault = false;
            Frame(window).Should().NotEqual(keyboard);
        }
        finally { window.Close(); themes.Apply("Classic"); }
    }

    [AvaloniaFact]
    public void DialogHostOpensWithKeyboardFocusOnExistingDefault()
    {
        var first = new Button { Content = "First" };
        var target = new Button { Content = "Default", IsDefault = true };
        var window = new DialogWindow { Content = new StackPanel { Children = { first, target } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try { target.IsFocused.Should().BeTrue(); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void DialogHostOpensWithKeyboardFocusOnFirstAvailableControlWithoutDefault()
    {
        var disabled = new Button { Content = "Disabled", IsEnabled = false };
        var target = new Button { Content = "First available" };
        var window = new DialogWindow { Content = new StackPanel { Children = { disabled, target } } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            target.IsFocused.Should().BeTrue();
            window.GetVisualDescendants().OfType<AdornerLayer>().Single()
                .Children.OfType<Rectangle>().Should().ContainSingle();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void EveryIdeWindowTypeUsesDialogChromeExceptMainWindow()
    {
        var windows = typeof(DialogWindow).Assembly.GetTypes()
            .Where(type => typeof(Window).IsAssignableFrom(type) && !type.IsAbstract
                && type != typeof(HexIDE.MainWindow)).ToArray();
        windows.Should().NotBeEmpty();
        foreach (var type in windows)
        {
            var window = (Window)System.Activator.CreateInstance(type)!;
            try { window.Classes.Should().Contain("ideDialog", type.FullName); }
            finally { window.Close(); }
        }
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void DialogFactoryExcludesProgramDialogs(bool program)
    {
        var manager = new WindowManager(Substitute.For<HexIDE.Localization.ILocalizationService>());
        var window = manager.MakeDialogWindow("Title", new HexIDE.Runtime.Dialogs.MessageBox
        {
            IsProgramDialog = program
        });
        try { window.Classes.Contains("ideDialog").Should().Be(!program); }
        finally { window.Close(); }
    }

    public static System.Collections.Generic.IEnumerable<object[]> DialogPolicies()
    {
        // Defaults already present are preservation checks. New choices come from #3117;
        // Runtime Error's Debug is also recorded in the unhandled-run-time-error oracle.
        var policies = new (string View, string? Default, string Focus)[]
        {
            ("SaveChangesView", "Str.Dialog.SaveChanges.Button.Yes", "default"),
            ("NewProjectView", null, "first"),
            ("NewProjectExisting", null, "text"),
            ("NewProjectRecent", null, "first"),
            ("FindReplaceView", "Str.Dialog.FindReplace.Button.FindNext", "text"),
            ("AddWatchDialog", "Str.Dialog.AddWatch.Ok", "text"),
            ("ComponentsView", "Str.Dialog.Components.Ok", "default"),
            ("OptionsView", "Str.Options.Shell.Buttons.Ok", "default"),
            ("RecoveryDialogView", "Str.Dialog.Recovery.Button.Restore", "default"),
            ("RuntimeErrorView", "Str.Dialog.RuntimeError.Button.Debug", "default"),
            ("ReferencesView", null, "first"),
            ("ProjectPropertiesView", null, "text"),
            ("FileConflictView", null, "first"),
            ("AddProcedureView", null, "text"),
            ("LanguageRevertGateView", null, "first"),
            ("AddinConsentDialog", null, "first"),
            ("WorkspaceArtifactConsentView", null, "first"),
            ("ExportPreviewDialog", null, "first"),
            ("TrustChainView", null, "first"),
            ("MenuEditorView", null, "text"),
            ("AboutDialog", null, "first"),
            ("FontDialog", null, "first"),
            ("ColorDialog", "Str.Dialog.MessageBox.Button.Ok", "default"),
            ("PrintDialog", "Str.Dialog.MessageBox.Button.Ok", "default"),
            ("InputBox", null, "text"),
            ("MessageBox", "Str.Dialog.MessageBox.Button.Ok", "default")
        };
        foreach (var (view, target, focus) in policies)
        foreach (var theme in new[] { "Classic", "Dark", "Abyss" })
            yield return new object[] { view, target!, focus, theme };
    }

    [AvaloniaTheory]
    [MemberData(nameof(DialogPolicies))]
    public void DialogOpensWithDeclaredDefaultCueAndFocus(string view, string? defaultKey, string focus, string theme)
    {
        var themes = new ThemeService();
        themes.Apply(theme);
        var runtime = view is "ColorDialog" or "PrintDialog" or "InputBox" or "MessageBox";
        var assembly = runtime ? typeof(HexIDE.Runtime.Dialogs.InputBox).Assembly : typeof(DialogWindow).Assembly;
        var prefix = runtime ? "HexIDE.Runtime.Dialogs." : view == "MenuEditorView"
            ? "HexIDE.VisualDesigner." : "HexIDE.Forms.Views.";
        var viewType = view is "NewProjectExisting" or "NewProjectRecent" ? "NewProjectView" : view;
        var content = (Control)System.Activator.CreateInstance(assembly.GetType(prefix + viewType)!)!;
        if (defaultKey is not null)
            content.Resources[defaultKey] = new HexIDE.Localization.LocalizationService().GetString(defaultKey);
        if (view == "TrustChainView")
            content.DataContext = new HexIDE.Forms.ViewModels.TrustChainViewModel(null, null, false, asDialog: true);
        // Isolate the chrome/focus policy from view-model command availability, as the rendering matrix does.
        // Do not assign IsDefault: that is the view's contract under test.
        var command = Substitute.For<System.Windows.Input.ICommand>();
        command.CanExecute(Arg.Any<object?>()).Returns(true);
        foreach (var button in content.GetLogicalDescendants().OfType<Button>().Where(button => button.GetType() == typeof(Button)))
            button.Command = command;
        if (view is "NewProjectExisting" or "NewProjectRecent")
            content.GetLogicalDescendants().OfType<TabControl>().Single().SelectedIndex = view == "NewProjectExisting" ? 1 : 2;
        var window = new WindowManager(Substitute.For<HexIDE.Localization.ILocalizationService>())
            .MakeDialogWindow("Title", content);
        window.SizeToContent = SizeToContent.Manual;
        window.Width = 800;
        window.Height = 600;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var controls = content.GetVisualDescendants().OfType<Control>()
                .Where(control => control.IsEffectivelyVisible && control.IsEffectivelyEnabled).ToArray();
            var defaults = controls.OfType<Button>().Where(button => button.IsDefault).ToArray();
            Button? target = null;
            if (defaultKey is null)
                defaults.Should().BeEmpty();
            else
            {
                target = defaults.Should().ContainSingle().Subject;
                target.Content.Should().Be(content.FindResource(defaultKey));
                target.BorderThickness.Should().Be(new Thickness(2));
            }
            var opening = focus switch
            {
                "default" => target!,
                "text" => controls.OfType<TextBox>().First(box => !box.IsReadOnly),
                _ => controls.First(control => control.Focusable && control.IsTabStop)
            };
            opening.IsFocused.Should().BeTrue($"{view} should open on {opening.GetType().Name}");
            if (opening is Button)
                window.GetVisualDescendants().OfType<AdornerLayer>()
                    .SelectMany(layer => layer.Children).OfType<Rectangle>().Should().ContainSingle();
            if (target is not null)
            {
                var withDefault = Frame(window);
                target.IsDefault = false;
                Frame(window).Should().NotEqual(withDefault);
            }
        }
        finally { window.Close(); themes.Apply("Classic"); }
    }

    private static byte[] Frame(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        using var bitmap = window.CaptureRenderedFrame();
        bitmap.Should().NotBeNull();
        using var stream = new MemoryStream();
        bitmap!.Save(stream);
        return stream.ToArray();
    }
}
