using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using HexIDE.Debugging;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Debugging;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;
using HexIDE.Tools.ObjectBrowser;

namespace HexIDE.Tests.ViewModels;

public class ToolLanguageSwitchTests
{
    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void ObjectBrowser_LabelsFollowLanguage(bool selectClass)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var sut = Browser(localization);
        if (selectClass) sut.SelectedClass = new OBClassViewModel("Example", OBClassKind.Module, "Project1");
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localization.Apply("de");

        sut.MembersHeader.Should().Be(selectClass ? "Mitglieder von 'Example'" : "Member");
        changed.Should().Contain(nameof(sut.MembersHeader));
    }

    private static ObjectBrowserToolViewModel Browser(ILocalizationService localization)
    {
        var projects = Substitute.For<IProjectManager>();
        projects.LoadedProjects.Returns(new List<ProjectDefinition>());
        return new ObjectBrowserToolViewModel(projects, Substitute.For<ILspClient>(),
            Substitute.For<IEditorService>(), Substitute.For<IComponentRegistry>(),
            Substitute.For<ITypeLibraryService>(), Substitute.For<IFocusedProjectUtil>(),
            localization, Substitute.For<ILanguageConnectionRegistry>());
    }

    [AvaloniaFact]
    public void ObjectBrowser_AllLibrariesLabelFollowsLanguage()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var sut = Browser(localization);
        var view = new ObjectBrowserToolView { DataContext = sut };
        var combo = view.GetLogicalDescendants().OfType<ComboBox>().Single();
        var libraryChanged = new List<string?>();
        if (sut.Libraries[0] is System.ComponentModel.INotifyPropertyChanged observable)
            observable.PropertyChanged += (_, e) => libraryChanged.Add(e.PropertyName);

        localization.Apply("de");

        sut.Libraries[0].Name.Should().Be("<Alle Bibliotheken>");
        sut.Libraries[0].ToString().Should().Be("<Alle Bibliotheken>");
        libraryChanged.Should().Contain("Name");
        combo.ItemTemplate.Should().NotBeNull();
        var label = (TextBlock)combo.ItemTemplate!.Build(sut.Libraries[0])!;
        label.DataContext = sut.Libraries[0];
        label.Text.Should().Be("<Alle Bibliotheken>");
        localization.Apply("en");
        label.Text.Should().Be("<All Libraries>");
    }

    [AvaloniaFact]
    public void Locals_ReadyHeaderFollowsLanguage()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var sut = new LocalsToolViewModel(localization, Substitute.For<IDebugController>());
        var changed = new List<string?>();
        sut.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localization.Apply("de");

        sut.ContextLabel.Should().Be("<Bereit>");
        changed.Should().Contain(nameof(sut.ContextLabel));
    }

    [AvaloniaFact]
    public void Locals_LanguageChangePreservesPausedContext()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var debug = Substitute.For<IDebugController>();
        debug.GetLocals().Returns(new DebugScope("Module1.Go", new[] { new DebugNode("x", "42", "Integer") }));
        var sut = new LocalsToolViewModel(localization, debug);
        debug.Stopped += Raise.Event<Action<StoppedInfo>>(new StoppedInfo(StopReason.Step, "Module1", 1));

        localization.Apply("de");

        sut.ContextLabel.Should().Be("Module1.Go");
        sut.Roots.Should().ContainSingle();
    }

    [AvaloniaTheory]
    [InlineData(false)]
    [InlineData(true)]
    public void Watches_LanguageChangePreservesEvaluationOrError(bool error)
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var watches = new WatchService();
        watches.Add(new WatchExpression("x", WatchType.Expression, "Module1.Go"));
        var sut = new WatchesToolViewModel(localization, watches,
            Substitute.For<IDebugController>(), Substitute.For<IWindowManager>());
        var row = sut.Rows[0];
        if (error) row.UpdateError("failure");
        else row.Update(new DebugEvalResult(true, "42", "Integer", false, new DebugNode("x", "42", "Integer")));

        localization.Apply("de");

        row.Value.Should().Be(error ? "failure" : "42");
    }

    [AvaloniaFact]
    public void Watches_OutOfContextValueFollowsLanguage()
    {
        var localization = new LocalizationService();
        localization.Apply("en");
        var watches = new WatchService();
        watches.Add(new WatchExpression("count", WatchType.Expression, "Module1.Go"));
        var sut = new WatchesToolViewModel(localization, watches,
            Substitute.For<IDebugController>(), Substitute.For<IWindowManager>());
        var changed = new List<string?>();
        sut.Rows[0].PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        localization.Apply("de");

        sut.Rows[0].Value.Should().Be("<Außerhalb des Kontexts>");
        changed.Should().Contain(nameof(WatchRowViewModel.Value));
    }
}
