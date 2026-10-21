using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Simple;

[assembly: AvaloniaTestApplication(typeof(HexIDE.Integration.Tests.TestApp))]

namespace HexIDE.Integration.Tests;

// A themed test Application: loads SimpleTheme (the IDE's base theme) so templated controls (PathIcon,
// etc.) get a control theme and render in capture tests — a bare Application leaves them template-less.
// The DataGrid control theme is added separately (as in the real App.axaml) so grid-bearing views render.
public class TestApp : Application
{
    public override void Initialize()
    {
        RequestedThemeVariant = ThemeVariant.Light;
        Styles.Add(new SimpleTheme());
        Styles.Add(new Dock.Avalonia.Themes.Simple.DockSimpleTheme());
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Simple.xaml"),
        });
        // As App.axaml does. Without it a TextEditor has no template, so its TextArea is not in the tree at
        // all: a test that walks or resolves a path through an editor was walking a shape the running IDE
        // never has, and one assertion passed only because of it (#611's test named TextEditor as the leaf).
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Simple/AvaloniaEdit.xaml"),
        });
        // As App.axaml does: access-key captions name their controls without the "_" marker (#578).
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://HexIDE/Automation/AccessKeyNames.axaml"),
        });
        // As App.axaml does: the running controls' own templates. Without them a Frame has no template, so the controls
        // it contains are not in the visual tree at all, and real pointer input passes through them (#974).
        foreach (var dictionary in new[] { "VBTimer.axaml", "VBLabel.axaml", "Resources.axaml" })
            Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/"))
            {
                Source = new Uri("avares://HexIDE.Runtime/BuiltinControls/" + dictionary),
            });
        // The driver tracks the windows that open from when its class first loads, beside the lifetime's list, which the
        // headless host has not got. Loaded here, as the running IDE has every window in that list: a window a test showed
        // before its first call to the driver went untracked, and a test that ran first saw it as opening later.
        System.Runtime.CompilerServices.RuntimeHelpers.RunClassConstructor(typeof(HexIDE.Automation.UiAutomationDriver).TypeHandle);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            // UseHeadlessDrawing = false enables the real (Skia) drawing path so render tests can capture
            // an actual rendered frame (CaptureRenderedFrame), not a no-op headless surface.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
}
