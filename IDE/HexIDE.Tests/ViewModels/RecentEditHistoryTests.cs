using AvaloniaEdit.Document;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Tools;

namespace HexIDE.Tests.ViewModels;

public class RecentEditHistoryTests
{
    private readonly IEventBus eventBus = Substitute.For<IEventBus>();
    private RecentEditHistory CreateHistory() => new(eventBus);
    private static TextDocument Text() => new(string.Join(Environment.NewLine, Enumerable.Range(1, 900).Select(i => $"line {i}")));
    private static DocumentIdentity Identity()
    {
        var module = TestHelpers.CreateModule();
        module.Owner.AddModule(module);
        return DocumentIdentity.For(module);
    }
    private static void Edit(TextDocument text, int line) => text.Insert(text.GetLineByNumber(line).EndOffset, " edited");

    [Theory]
    [InlineData(9, 1)]
    [InlineData(10, 1)]
    [InlineData(11, 2)]
    public void CoalescesOnlyWithinTenLinesOfNewest(int distance, int count)
    {
        var history = CreateHistory();
        var identity = Identity();
        var text = Text();
        using var tracking = history.Track(identity, text);
        history.Entries.Should().BeEmpty();
        Edit(text, 20);
        Edit(text, 20 + distance);
        history.Entries.Should().HaveCount(count);
        history.Entries[0].Line.Should().Be(20 + distance);
        history.Entries[0].Preview.Should().Be($"line {20 + distance} edited");
    }

    [Fact]
    public void InsertingManyLinesAtNewestLocationStillReplacesIt()
    {
        var history = CreateHistory();
        var text = Text();
        using var tracking = history.Track(Identity(), text);
        Edit(text, 40);
        text.Insert(text.GetLineByNumber(40).Offset, string.Concat(Enumerable.Repeat("new line\n", 20)));
        history.Entries.Should().ContainSingle();
        history.Entries[0].Line.Should().Be(40);
        history.Entries[0].Preview.Should().Be("new line");
    }

    [Fact]
    public void AnotherDocumentAddsAnEntryEvenAtSameLine()
    {
        var history = CreateHistory();
        var first = Identity();
        var second = Identity();
        var a = Text();
        var b = Text();
        using var trackingA = history.Track(first, a);
        using var trackingB = history.Track(second, b);
        Edit(a, 20);
        Edit(b, 20);
        history.Entries.Select(e => e.Document).Should().Equal(second, first);
    }

    [Fact]
    public void CapsAtFiftyNewestEntries()
    {
        var history = CreateHistory();
        var text = Text();
        using var tracking = history.Track(Identity(), text);
        for (var line = 1; line <= 561; line += 11) Edit(text, line);
        history.Entries.Should().HaveCount(50);
        history.Entries.Select(e => e.Line).Should().Equal(Enumerable.Range(1, 50).Reverse().Select(i => 1 + i * 11));
    }

    [Fact]
    public void LocationsFollowInsertionsAndDeletionsAboveThem()
    {
        var history = CreateHistory();
        var text = Text();
        using var tracking = history.Track(Identity(), text);
        Edit(text, 40);
        var location = history.Entries.Single();
        text.Insert(0, "first\nsecond\n");
        location.Line.Should().Be(42);
        location.Preview.Should().Be("line 40 edited");
        text.Remove(0, "first\nsecond\n".Length);
        location.Line.Should().Be(40);
    }

    [Fact]
    public void DeletingAnEntireLineDropsItsEntry()
    {
        var history = CreateHistory();
        var text = Text();
        using var tracking = history.Track(Identity(), text);
        Edit(text, 40);
        var deleted = history.Entries.Single();
        var line = text.GetLineByNumber(40);
        text.Remove(line.Offset, line.TotalLength);
        history.Entries.Should().NotContain(deleted);
    }

    [Fact]
    public void ReplacingTextOnALineKeepsItsOlderLocation()
    {
        var history = CreateHistory();
        var text = Text();
        using var tracking = history.Track(Identity(), text);
        Edit(text, 40);
        var location = history.Entries.Single();
        Edit(text, 80);
        var line = text.GetLineByNumber(40);
        text.Replace(line.Offset, line.Length, "replacement");
        history.Entries.Should().Contain(location);
        location.Preview.Should().Be("replacement");
    }

    [Fact]
    public void ClosingAndReopeningKeepsEntriesAndResumesTracking()
    {
        var history = CreateHistory();
        var identity = Identity();
        var text = Text();
        var tracking = history.Track(identity, text);
        Edit(text, 40);
        tracking.Dispose();
        var reopened = new TextDocument(text.Text);
        using var reopenedTracking = history.Track(identity, reopened);
        history.Entries.Should().ContainSingle();
        reopened.Insert(0, "above\n");
        history.Entries.Last().Line.Should().Be(41);
        history.Entries.Last().Preview.Should().Be("line 40 edited");
    }

    [Fact]
    public void RemovingDocumentDropsItsEntriesIncludingWhileClosed()
    {
        var history = CreateHistory();
        var identity = Identity();
        var text = Text();
        using (history.Track(identity, text)) Edit(text, 40);
        history.Entries.Should().ContainSingle();
        identity.Project.DeleteModule(identity.Module!);
        history.Entries.Should().BeEmpty();
    }

    [Fact]
    public void UnloadingProjectDropsItsEntries()
    {
        Action<ProjectUnloadedEvent> unload = null!;
        eventBus.Subscribe(Arg.Do<Action<ProjectUnloadedEvent>>(handler => unload = handler));
        var history = CreateHistory();
        var identity = Identity();
        var text = Text();
        using var tracking = history.Track(identity, text);
        Edit(text, 40);
        history.Entries.Should().ContainSingle();
        unload(new ProjectUnloadedEvent(identity.Project));
        history.Entries.Should().BeEmpty();
    }

    [Fact]
    public void SelectingRowsNavigatesWithoutRecordingEdits()
    {
        var history = CreateHistory();
        var editor = Substitute.For<IEditorService>();
        var tool = new RecentEditsToolViewModel(history, () => editor, Substitute.For<ILocalizationService>());
        var text = Text();
        var identity = Identity();
        using var tracking = history.Track(identity, text);
        Edit(text, 20);
        Edit(text, 40);
        tool.Entries.Should().HaveCount(2);
        tool.SelectedEntry = tool.Entries[1];
        tool.SelectedEntry = tool.Entries[0];
        editor.Received().NavigateTo(DocumentWireName.For(identity), 20, 1);
        editor.Received().NavigateTo(DocumentWireName.For(identity), 40, 1);
        history.Entries.Select(e => e.Line).Should().Equal(40, 20);
    }
}
