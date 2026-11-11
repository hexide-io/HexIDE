using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE;
using HexIDE.Addins;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views;
using HexIDE.Keymaps;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Themes;

namespace HexIDE.Integration.Tests.Views;

public class FocusRingSpacingTests
{
    public static IEnumerable<object[]> Surfaces()
    {
        var assembly = typeof(AddProcedureView).Assembly;
        var types = assembly.GetTypes().Where(t => t.Namespace == "HexIDE.Forms.Views.Options"
            && typeof(UserControl).IsAssignableFrom(t) && !t.IsAbstract)
            .Concat(new[] { typeof(OptionsView), typeof(AddProcedureView), typeof(FindReplaceView), typeof(ReferencesView),
                typeof(ComponentsView), typeof(ProjectPropertiesView), typeof(AboutDialog),
                typeof(HexIDE.Dialogs.MessageBox), typeof(HexIDE.Dialogs.InputBox) });
        foreach (var type in types)
        foreach (var theme in new[] { "HighContrastLight", "HighContrastDark" })
            yield return new object[] { type.FullName!, theme };
    }

    [AvaloniaTheory]
    [MemberData(nameof(Surfaces))]
    public void FocusRingsHaveRoom(string typeName, string theme)
    {
        var themes = new ThemeService();
        var localization = new LocalizationService();
        var previousLanguage = localization.ActiveLanguage;
        localization.Apply("en");
        var content = (Control)Activator.CreateInstance(typeof(AddProcedureView).Assembly.GetType(typeName)!)!;
        OptionsViewModel? options = null;
        if (content is OptionsView || typeName.Contains(".Options."))
        {
            options = Options(localization, themes, theme);
            content.DataContext = content is OptionsView ? options : Nodes(options.RootNodes)
                .Select(n => n.Page).FirstOrDefault(page => page is not null
                    && page.GetType().Name == content.GetType().Name + "Model");
            content.DataTemplates.Add(new ViewLocator());
        }
        if (content is HexIDE.Dialogs.MessageBox message)
        {
            message.Text = "A message with three answers";
            message.Buttons = MessageBoxButtons.YesNoCancel;
        }
        if (content is HexIDE.Dialogs.InputBox input) input.Prompt = "Enter a value";
        if (content is AddProcedureView) content.DataContext = new AddProcedureViewModel();
        var window = new WindowManager(localization).MakeDialogWindow("Spacing", content);
        window.SetRenderScaling(1.5);
        // Options pages occupy the right pane of the 720 DIP shell.
        if (typeName.Contains(".Options."))
        {
            window.SizeToContent = SizeToContent.Manual;
            window.Width = 480;
            window.Height = 650;
            content.Margin = new Thickness(8);
        }
        try
        {
            themes.Apply(theme);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var tabs = content.GetLogicalDescendants().OfType<TabControl>().ToArray();
            var errors = new List<string>();
            Check(content, window, errors);
            foreach (var tab in tabs)
                for (var i = 1; i < tab.ItemCount; i++)
                {
                    tab.SelectedIndex = i;
                    window.UpdateLayout();
                    Check(content, window, errors);
                }
            if (content is OptionsView && options is not null)
                foreach (var node in Nodes(options.RootNodes).Where(n => n.Page is not null))
                {
                    options.SelectedNode = node;
                    window.UpdateLayout();
                    Check(content, window, errors);
                }
            string.Join(Environment.NewLine, errors.Distinct()).Should().BeEmpty(typeName);
        }
        finally { window.Close(); options?.CancelCommand.Execute(null); themes.Apply("Classic"); localization.Apply(previousLanguage); }
    }

    [AvaloniaTheory]
    [InlineData("HighContrastLight", false)]
    [InlineData("HighContrastDark", false)]
    [InlineData("HighContrastLight", true)]
    [InlineData("HighContrastDark", true)]
    public void PropertiesSelectorHasRoom(string theme, bool maximized)
    {
        var themes = new ThemeService();
        var view = new HexIDE.VisualDesigner.Views.PropertiesToolView();
        var window = new Window { Content = view, Width = 300, Height = 600 };
        window.SetRenderScaling(1.5);
        try
        {
            themes.Apply(theme);
            window.Show();
            if (maximized) window.WindowState = WindowState.Maximized;
            window.UpdateLayout();
            var box = view.FindControl<ComboBox>("ObjectSelector")!;
            var rect = Bounds(box, window);
            rect.Left.Should().BeGreaterThanOrEqualTo(4);
            rect.Top.Should().BeGreaterThanOrEqualTo(4);
            (window.Bounds.Width - rect.Right).Should().BeGreaterThanOrEqualTo(4);
            var filter = Bounds(view.FindControl<TextBox>("PropertyFilter")!, window);
            (filter.Top - rect.Bottom).Should().BeGreaterThanOrEqualTo(4);
            // The filter sat flush with the pane's edge one row down, and in Classic the two no longer lined up (#3659).
            (window.Bounds.Width - filter.Right).Should().BeGreaterThanOrEqualTo(4);
            filter.Left.Should().Be(rect.Left);
            filter.Right.Should().Be(rect.Right);
            var errors = new List<string>();
            Check(view, window, errors);
            string.Join(Environment.NewLine, errors.Distinct()).Should().BeEmpty();
        }
        finally { window.Close(); themes.Apply("Classic"); }
    }

    [AvaloniaFact]
    public void PropertiesTabsKeepThePanesFullWidth()
    {
        // At a normal window size the docked pane is about 141 DIP wide. An inset round the whole pane wrapped
        // Categorized under Alphabetic there and took a row from the property list (#3659's live check). Headless text
        // measures narrower than the IDE's, so the wrap itself can't be reproduced here; the width it needs can.
        var view = new HexIDE.VisualDesigner.Views.PropertiesToolView();
        var window = new Window { Content = view, Width = 141, Height = 600 };
        try
        {
            window.Show();
            window.UpdateLayout();
            var tabs = Bounds(view.FindControl<TabControl>("TabProperties")!, window);
            tabs.Left.Should().Be(0);
            tabs.Width.Should().Be(window.Bounds.Width);
        }
        finally { window.Close(); }
    }

    private static IEnumerable<OptionsNodeViewModel> Nodes(IEnumerable<OptionsNodeViewModel> nodes)
    {
        foreach (var node in nodes)
        {
            yield return node;
            foreach (var child in Nodes(node.Children)) yield return child;
        }
    }

    private static OptionsViewModel Options(LocalizationService localization, ThemeService themes, string theme)
    {
        var settings = Substitute.For<ISettingsService>();
        settings.ActiveTheme.Returns(theme);
        settings.ActiveKeymap.Returns("Default");
        settings.ActiveLanguage.Returns("en");
        settings.TabWidth.Returns(4);
        var keymaps = Substitute.For<IKeymapService>();
        keymaps.AvailableKeymaps.Returns(["Default"]);
        keymaps.ActiveKeymap.Returns("Default");
        var languages = Substitute.For<ILanguageSwitchService>();
        languages.ActiveLanguage.Returns("en");
        languages.AvailableLanguages.Returns([new LanguageInfo("en", "English", "ltr")]);
        var developer = Substitute.For<IDeveloperModeService>();
        developer.IsEnabled.Returns(true);
        return new OptionsViewModel(settings, themes, keymaps, languages, Substitute.For<IAddinRegistry>(),
            new AddinOptionsService(), developer, localization, Substitute.For<IEventBus>());
    }

    private static void Check(Control content, Window window, List<string> errors)
    {
        // Walk layout controls, not the implementation parts inside their templates.
        var controls = content.GetLogicalDescendants().OfType<Control>()
            .Where(c => TopLevel.GetTopLevel(c) == window && c.IsEffectivelyVisible && c.Bounds.Width > 0 && c.Bounds.Height > 0).ToArray();
        // Only controls given the pack's outside focus ring need the clearance. Items, tabs and tree rows draw focus
        // inside their own reserved border, and anything else keeps the theme's inset adorner (ChromeBorders.axaml).
        var ring = window.FindResource("ChromeFocusAdorner");
        ring.Should().NotBeNull("the high-contrast pack adds the outside focus ring");
        foreach (var control in controls.Where(c => c.Focusable && ReferenceEquals(c.FocusAdorner, ring)))
        {
            if (control.IsEffectivelyEnabled) control.Focus();
            var rect = Bounds(control, window);
            if (rect.Left < 2 - 0.000001 || rect.Top < 2 - 0.000001 || rect.Right > window.Bounds.Width - 2 + 0.000001 || rect.Bottom > window.Bounds.Height - 2 + 0.000001)
                errors.Add($"{Name(control)} at {rect} touches window {window.Bounds.Size}");
            // Template children belong to the control, rather than being its layout neighbours.
            foreach (var sibling in controls.Where(c => c != control && c.Parent == control.Parent))
            {
                var other = Bounds(sibling, window);
                // Ignore floating-point noise at an exactly 4 DIP gap (not device-pixel rounding).
                if (rect.Inflate(2 - 0.000001).Intersects(other.Inflate(2 - 0.000001)))
                    errors.Add($"{Name(control)} {rect} crowds {Name(sibling)} {other}");
            }
        }
    }

    private static Rect Bounds(Control control, Window window) =>
        new(control.TranslatePoint(default, window)!.Value, control.Bounds.Size);

    private static string Name(Control control) => control.GetType().Name + ":" +
        (control.Name ?? (control as ContentControl)?.Content?.ToString() ?? (control as TextBlock)?.Text);
}
