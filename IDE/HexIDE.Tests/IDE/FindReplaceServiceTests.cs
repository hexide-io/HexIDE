using System.Collections.Generic;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Localization;
using HexIDE.Tools;

namespace HexIDE.Tests.IDE;

public class FindReplaceServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Reopen_PreservesSearchState(bool replace)
    {
        var windows = Substitute.For<IWindowManager>();
        var projects = Substitute.For<IProjectManager>();
        var editors = Substitute.For<IEditorService>();
        var localization = Substitute.For<ILocalizationService>();
        var explorer = new ProjectToolViewModel(projects, Substitute.For<IEventBus>(),
            Substitute.For<IProjectService>(), editors, localization);
        var shown = new List<IDialog>();
        windows.When(manager => manager.ShowWindow(Arg.Any<IDialog>()))
            .Do(call => shown.Add(call.Arg<IDialog>()));
        var sut = new FindReplaceService(windows, Substitute.For<IDocumentDockService>(),
            localization, projects, editors, () => explorer, new HexIDE.Runtime.Serialization.Vb6ReadOnlyRegionReader());

        sut.ShowFind();
        var first = (FindReplaceViewModel)shown[0];
        first.SearchText = "needle";
        first.ReplaceText = "replacement";
        first.MatchCase = true;
        first.WholeWordOnly = true;
        first.UsePatternMatching = true;
        first.Direction = FindDirection.Up;
        first.Scope = FindScope.AllOpenDocuments;

        if (replace) sut.ShowReplace();
        else sut.ShowFind();

        windows.Received(2).ShowWindow(Arg.Any<IDialog>());
        shown[1].Should().BeSameAs(first);
        var reopened = (FindReplaceViewModel)shown[1];
        reopened.SearchText.Should().Be("needle");
        reopened.ReplaceText.Should().Be("replacement");
        reopened.MatchCase.Should().BeTrue();
        reopened.WholeWordOnly.Should().BeTrue();
        reopened.UsePatternMatching.Should().BeTrue();
        reopened.Direction.Should().Be(FindDirection.Up);
        reopened.Scope.Should().Be(FindScope.AllOpenDocuments);
        reopened.ShowReplace.Should().Be(replace);
    }
}
