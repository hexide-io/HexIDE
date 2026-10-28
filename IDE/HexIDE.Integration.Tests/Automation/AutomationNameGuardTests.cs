using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Dock.Avalonia.Controls;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using HexIDE.Addins;
using HexIDE.Automation;
using HexIDE.Forms.ViewModels;
using HexIDE.Forms.ViewModels.Options;
using HexIDE.Forms.Views;
using HexIDE.IDE;
using HexIDE.Keymaps;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Themes;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;
using HexIDE.VisualDesigner;
using HexIDE.VisualDesigner.Views;

namespace HexIDE.Integration.Tests.Automation;

public class AutomationNameGuardTests
{
    private static IEnumerable<UiNode> Flatten(UiNode node) => node.Children.SelectMany(Flatten).Prepend(node);

    private static void Check(Window window, string surface, List<string> failures)
    {
        Dispatcher.UIThread.RunJobs();
        using var frame = window.CaptureRenderedFrame();
        frame.Should().NotBeNull(surface);
        var nodes = Flatten(UiAutomationDriver.Dump(window, "Window", 40, false)).ToList();
        nodes.Should().HaveCountGreaterThan(5, surface);
        failures.AddRange(nodes.Where(n => n.TypeNameAsName != null)
            .Select(n => $"{surface}: {n.Path}: {n.TypeNameAsName}"));
        var rows = nodes.Where(n => n.ControlType is "TreeItem" or "ListItem").ToList();
        rows.Should().NotBeEmpty(surface + " must realize its rows for the guard to cover them");
        failures.AddRange(nodes.Where(n => n.ControlType is "TreeItem" or "ListItem" or "ComboBox")
            .Where(n => string.IsNullOrWhiteSpace(n.Name))
            .Select(n => $"{surface}: {n.Path}: unnamed {n.ControlType}"));
    }

    private static void RequireItem<T>(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.GetVisualDescendants().OfType<Control>()
            .Where(c => c.IsEffectivelyVisible && (c is ListBoxItem or TreeViewItem) && c.DataContext is T)
            .Should().NotBeEmpty(typeof(T).Name + " must have a realized row for the guard to cover it");
    }

    [AvaloniaFact]
    public void Rendered_IDE_surfaces_have_no_type_names_or_unnamed_rows_or_combo_boxes()
    {
        var failures = new List<string>();
        var window = ToolbarAutomationNameTests.ShowMainView();
        try
        {
            // Load the application's compiled markup without its startup, services or user profile. Several chrome
            // dictionaries are inlined by the XAML compiler and cannot be loaded separately by URI.
            var app = new App();
            app.LoadMarkup();
            var resources = app.Resources;
            app.Resources = new ResourceDictionary();
            window.Resources.MergedDictionaries.Add(resources);
            while (app.Styles.Count > 0)
            {
                var style = app.Styles[0];
                app.Styles.RemoveAt(0);
                window.Styles.Add(style);
            }
            window.DataTemplates.Add(new ViewLocator());
            var loc = Substitute.For<ILocalizationService>();
            loc.GetString(Arg.Any<string>()).Returns(c => window.FindResource(c.Arg<string>()) as string ?? c.Arg<string>());
            var project = new ProjectDefinition(VBProjectType.EXE, "Project1");
            var form = new FormDefinition(project, FormComponentClass.Instance, "Form1");
            form.UpdateComponents([.. form.Components, new ComponentInstance(CommandButtonComponentClass.Instance, "Command1")]);
            project.AddForm(form);
            var manager = Substitute.For<IProjectManager>();
            manager.LoadedProjects.Returns(new List<ProjectDefinition> { project });
            var bus = Substitute.For<IEventBus>();
            var editor = Substitute.For<IEditorService>();
            var designer = new FormEditViewModel(null!, bus, Substitute.For<IProjectService>(), null!, null!,
                Substitute.For<ISettingsService>(), loc).Initialize(form);
            designer.SelectedComponent = designer.Form;
            var documents = Substitute.For<IDocumentDockService>();
            documents.ActiveDocument.Returns(designer);
            var properties = new PropertiesToolViewModel(documents, bus, Substitute.For<IWindowManager>(), loc);
            var explorer = new ProjectToolViewModel(manager, bus, Substitute.For<IProjectService>(), editor, loc);
            var browser = new ObjectBrowserToolViewModel(manager, Substitute.For<ILspClient>(), editor,
                new ComponentRegistry(), Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(),
                loc, Substitute.For<ILanguageConnectionRegistry>());
            var tools = new ToolDock { ActiveDockable = explorer, VisibleDockables = [explorer, properties] };
            var docs = new DocumentDock { ActiveDockable = designer, VisibleDockables = [designer, browser] };
            var root = new RootDock
            {
                VisibleDockables = [new ProportionalDock { VisibleDockables = [tools, docs] }],
            };
            root.ActiveDockable = root.VisibleDockables[0];
            root.DefaultDockable = root.ActiveDockable;
            var factory = new Factory();
            factory.InitLayout(root);
            var main = (MainView)window.Content!;
            main.FindControl<DockControl>("DockControl")!.Layout = root;
            RequireItem<ProjectViewModel>(window);
            RequireItem<FormViewModel>(window);
            window.GetVisualDescendants().OfType<FormEditView>().Should().Contain(v => v.IsEffectivelyVisible);
            Check(window, "main window and Project Explorer", failures);
            tools.ActiveDockable = properties;
            RequireItem<PropertyViewModel>(window);
            Check(window, "designer and Properties", failures);
            docs.ActiveDockable = browser;
            RequireItem<OBClassViewModel>(window);
            Check(window, "Object Browser classes", failures);
            browser.SelectedClass = browser.FilteredClasses.First(c => c.Members.Count > 0);
            RequireItem<OBMemberViewModel>(window);
            Check(window, "Object Browser members", failures);
            browser.SearchResults.Add(new OBSearchResultViewModel(new SymbolInformation("Example", SymbolKind.Method,
                new HexIDE.Lsp.Messages.Location("file:///project/Form1.frm", new HexIDE.Lsp.Messages.Range(new Position(0, 0), new Position(0, 7)))),
                OBMemberKind.Method));
            browser.IsShowingSearchResults = true;
            RequireItem<OBSearchResultViewModel>(window);
            Check(window, "Object Browser search results", failures);

            window.Content = new NewProjectView
            {
                DataContext = new NewProjectViewModel(Substitute.For<IWindowManager>(),
                    Substitute.For<IRecentProjectsService>(), Substitute.For<ISettingsService>(), new PersonalityService(), loc),
            };
            RequireItem<ProjectTemplateViewModel>(window);
            Check(window, "New Project dialog", failures);
            var settings = Substitute.For<ISettingsService>();
            settings.ActiveLanguage.Returns("system");
            var languageSwitch = Substitute.For<ILanguageSwitchService>();
            languageSwitch.AvailableLanguages.Returns([new LanguageInfo("system", "System default", "ltr")]);
            var devMode = Substitute.For<IDeveloperModeService>();
            devMode.IsEnabled.Returns(true);
            var options = new OptionsViewModel(settings, Substitute.For<IThemeService>(),
                Substitute.For<IKeymapService>(), languageSwitch, Substitute.For<IAddinRegistry>(),
                new AddinOptionsService(), devMode, loc, bus);
            window.Content = new OptionsView { DataContext = options };
            foreach (var node in options.RootNodes.Concat(options.RootNodes.SelectMany(n => n.Children)).Where(n => n.Page != null))
            {
                options.SelectedNode = node;
                Check(window, "Options / " + node.Title, failures);
            }
            string.Join(Environment.NewLine, failures).Should().BeEmpty(
                "rendered IDE controls must not be named after loaded types, and list/tree rows and combo boxes must have names");
        }
        finally { window.Close(); }
    }
}
