using System;
using Avalonia;
using Avalonia.Headless;
using Avalonia.Markup.Xaml.Styling;
using Avalonia.Styling;
using Avalonia.Themes.Simple;
using HexIDE.Runtime.Components;

[assembly: AvaloniaTestApplication(typeof(HexIDE.Integration.Tests.TestApp))]

namespace HexIDE.Integration.Tests;

// A themed test Application: loads SimpleTheme (the IDE's base theme) so templated controls (PathIcon,
// etc.) get a control theme and render in capture tests — a bare Application leaves them template-less.
// The DataGrid control theme is added separately (as in the real App.axaml) so grid-bearing views render.
public class TestApp : Application
{
    public override void Initialize()
    {
        Console.Error.WriteLine("1085 probe: Initialize entered");
        HexIDE.Automation.WaylandToolTips.Initialize();
        HeadlessFontWarmUp.Run();
        Console.Error.WriteLine("1085 probe: font warmup finished");
        RequestedThemeVariant = ThemeVariant.Light;
        Console.Error.WriteLine("1085 probe: theme start");
        Styles.Add(new SimpleTheme());
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/DialogButtons.axaml"),
        });
        Console.Error.WriteLine("1085 probe: theme end");
        Styles.Add(new Dock.Avalonia.Themes.Simple.DockSimpleTheme());
        Console.Error.WriteLine("1085 probe: dock theme end");
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/ToolChrome.axaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/DragPreview.axaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://Avalonia.Controls.DataGrid/Themes/Simple.xaml"),
        }) ; Console.Error.WriteLine("1085 probe: resource 1");
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/IconGeometry.axaml"),
        });
        // As App.axaml does. Without it a TextEditor has no template, so its TextArea is not in the tree at
        // all: a test that walks or resolves a path through an editor was walking a shape the running IDE
        // never has, and one assertion passed only because of it (#611's test named TextEditor as the leaf).
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://AvaloniaEdit/Themes/Simple/AvaloniaEdit.xaml"),
        }) ; Console.Error.WriteLine("1085 probe: resource 2");
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/CompletionDescriptions.axaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://HexIDE/Themes/EditorLinks.axaml"),
        });
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE/"))
        {
            Source = new Uri("avares://HexIDE/Themes/TabSelection.axaml"),
        });
        // As App.axaml does: code reads left to right under a right-to-left language (#1944).
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://HexIDE/Themes/CodeDirection.axaml"),
        }) ; Console.Error.WriteLine("1085 probe: resource 3");
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://HexIDE/Themes/MenuNavigation.axaml"),
        });
        // As App.axaml does: access-key captions name their controls without the "_" marker (#578).
        Styles.Add(new StyleInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://HexIDE/Automation/AccessKeyNames.axaml"),
        }) ; Console.Error.WriteLine("1085 probe: resource 4");
        // As App.axaml does: the IDE's colours, the 25 VB6 system colours among them. A system colour is looked up on the
        // application, not on a window, so without this every one fell back to DeepPink here, and a test comparing a
        // painted pixel with vbButtonFace compared pink with pink (#908).
        Resources.MergedDictionaries.Add(new ResourceInclude(new Uri("avares://HexIDE.Integration.Tests/"))
        {
            Source = new Uri("avares://HexIDE/Themes/Classic.axaml"),
        }) ; Console.Error.WriteLine("1085 probe: resource 5");
        // As the IDE does: the running controls' own templates. Without them a Frame has no template, so the controls it
        // contains are not in the visual tree at all, and real pointer input passes through them (#974). The provider registers
        // them itself, through a host whose resources go through the IDE's checks, under the origin the IDE gives it (#5396).
        RegisterProvider();
        // The running IDE has every window in its lifetime's list, which the headless host has not got: a window a test showed
        // before its first call to the driver went untracked, and a test that ran first saw it as opening later.
        HexIDE.IDE.RunningProgram.Current = new HexIDE.Runtime.Running.InterpreterRunningProgram();
        HexIDE.Automation.UiAutomationDriver.TrackWindows();
        WindowLeakGuard.Track();
        Console.Error.WriteLine("1085 probe: Initialize finished");
    }

    /// <summary>
    /// The service the provider's themes went through, so a test can see what it holds. One per application: the headless
    /// harness makes more than one in a run, and each merges the provider's themes afresh.
    /// </summary>
    internal HexIDE.Addins.AddinResourceService ProviderResources { get; } = new();

    private void RegisterProvider()
    {
        var origin = HexIDE.Desktop.BasicProviderComposition.Origin;
        var host = Substitute.For<HexIDE.Addins.IHexIdeHost>();
        host.RegisterResources(Arg.Any<Uri>()).Returns(call =>
            ProviderResources.Register(call.Arg<Uri>(), origin, typeof(HexIDE.Runtime.BasicProvider).Assembly));
        // What draws the designer's controls (#5591): through the real service, so every designer here draws as the IDE's do.
        host.RegisterDesignerPictures(Arg.Any<Func<IDesignerDrawingSurface, IDesignerPictures>>()).Returns(call =>
            new HexIDE.Addins.AddinDesignerPictureService().Register(call.Arg<Func<IDesignerDrawingSurface, IDesignerPictures>>(), origin));
        // VB6's scale rules (#5758), through the real service, so every designer and Properties window here shows and moves
        // positions as the IDE's do.
        host.RegisterDesignPositions(Arg.Any<IDesignPositions>()).Returns(call =>
            new HexIDE.Addins.AddinDesignPositionService().Register(call.Arg<IDesignPositions>(), origin));
        new HexIDE.Runtime.BasicProvider().Initialize(host);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<TestApp>()
            // UseHeadlessDrawing = false enables the real (Skia) drawing path so render tests can capture
            // an actual rendered frame (CaptureRenderedFrame), not a no-op headless surface.
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false })
            .UseSkia();
}
