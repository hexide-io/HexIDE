using System.ComponentModel;
using AvaloniaEdit.Document;
using HexIDE.Bookmarks;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;

namespace HexIDE.Tests.ViewModels;

/// <summary>
/// The code window's read-only section provider: what typing may change (hexide-io/HexIDE#273 task 3.7).
/// </summary>
/// <remarks>
/// The provider carries two gates, and they are deliberately not the same thing. The header and each member's
/// attribute run are read-only <em>regions</em>; a form the IDE cannot reproduce is read-only <em>as a
/// whole</em>, which refuses typing but must leave breakpoints and Find working, so it never makes a line
/// part of a region.
/// </remarks>
public class ReadOnlySectionProviderTests : IDisposable
{
    private readonly List<CodeEditorViewModel> made = [];

    public void Dispose()
    {
        foreach (var vm in made) vm.Dispose();
        GC.SuppressFinalize(this);
    }

    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private const string Frm =
        "VERSION 5.00\r\n" +
        "Begin VB.Form frmOrders \r\n" +
        "   Caption         =   \"Orders\"\r\n" +
        "End\r\n" +
        "Attribute VB_Name = \"frmOrders\"\r\n" +
        "Attribute VB_PredeclaredId = True\r\n" +
        "Option Explicit\r\n" +
        "\r\n" +
        "Private Sub Form_Load()\r\n" +
        "    Caption = \"Ready\"\r\n" +
        "End Sub\r\n";

    private const string ClassCode =
        "Option Explicit\r\n" +
        "\r\n" +
        "Public Function Total() As Currency\r\n" +
        "Attribute Total.VB_Description = \"The order total\"\r\n" +
        "Attribute Total.VB_UserMemId = 0\r\n" +
        "    Total = 0\r\n" +
        "End Function\r\n" +
        "\r\n" +
        "Public Sub Clear()\r\n" +
        "End Sub\r\n";

    private CodeEditorViewModel NewEditor()
    {
        var eventBus = Substitute.For<IEventBus>();
        eventBus.Subscribe<CreateOrNavigateToSubEvent>(Arg.Any<Action<CreateOrNavigateToSubEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(Arg.Any<Action<ApplyAllUnsavedChangesEvent>>()).Returns(Substitute.For<IDisposable>());
        eventBus.Subscribe<FormUnloadedEvent>(Arg.Any<Action<FormUnloadedEvent>>()).Returns(Substitute.For<IDisposable>());
        var vm = new CodeEditorViewModel(
            Substitute.For<IWindowManager>(), Substitute.For<IEditorService>(),
            Substitute.For<IProjectService>(), eventBus, Substitute.For<ILspClient>(),
            Substitute.For<ISettingsService>(), Substitute.For<IStatusBarService>(),
            Substitute.For<IBookmarkService>(), Substitute.For<HexIDE.Debugging.IBreakpointService>(),
            Substitute.For<HexIDE.Runtime.Debugging.IDebugController>(),
            Substitute.For<HexIDE.Debugging.IRunScope>(), Substitute.For<ILocalizationService>());
        made.Add(vm);
        return vm;
    }

    private static FormDefinition AForm() => new FormDeserializer().Deserialize(Project, Frm, NullSink.Instance)!;

    private CodeEditorViewModel OpenClass()
    {
        var module = TestHelpers.CreateModule(name: "Order", kind: ModuleKind.ClassModule);
        module.UpdateCode(ClassCode);
        return NewEditor().Initialize(module);
    }

    private static int At(CodeEditorViewModel vm, string text) =>
        vm.Document.Text.IndexOf(text, StringComparison.Ordinal) is var at and >= 0
            ? at
            : throw new InvalidOperationException($"'{text}' is not in the buffer");

    private static int LineEnd(CodeEditorViewModel vm, string text) =>
        vm.Document.GetLineByOffset(At(vm, text)).EndOffset;

    private static int LineEndWithTerminator(CodeEditorViewModel vm, string text)
    {
        var line = vm.Document.GetLineByOffset(At(vm, text));
        return line.Offset + line.TotalLength;
    }

    private static List<(int Offset, int End)> Deletable(CodeEditorViewModel vm, int from, int to) =>
        vm.ReadOnlySections.GetDeletableSegments(new SimpleSegment(from, to - from))
            .Select(s => (s.Offset, s.EndOffset)).ToList();

    // -- Insertion ----------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Insertion_AtTheTopOfTheFile_IsRefused()
    {
        // The stock provider allows insertion at a segment's edges, so the very first offset of the file
        // would take text above the header.
        OpenClass().ReadOnlySections.CanInsert(0).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Insertion_WhereTheHeaderEnds_IsAllowed()
    {
        var vm = OpenClass();
        vm.ReadOnlySections.CanInsert(At(vm, "Option Explicit")).Should().BeTrue(
            "the end of a region is the start of the developer's first line");
    }

    [AvaloniaFact]
    public void AFormsHeader_StraddlesThePrefixAndIsProtectedToItsLastAttribute()
    {
        // A form's prefix ends at the designer block's End; its header runs on through the attribute lines,
        // which are the first lines of the code.
        var vm = NewEditor().Initialize(AForm());
        vm.ReadOnlySections.CanInsert(At(vm, "Attribute VB_Name")).Should().BeFalse();
        vm.ReadOnlySections.CanInsert(At(vm, "Attribute VB_PredeclaredId")).Should().BeFalse();
        vm.ReadOnlySections.CanInsert(At(vm, "Option Explicit")).Should().BeTrue();
    }

    [AvaloniaFact]
    public void Insertion_AtTheStartOfAnAttributeRun_IsRefused()
    {
        var vm = OpenClass();
        vm.ReadOnlySections.CanInsert(At(vm, "Attribute Total.VB_Description")).Should().BeFalse(
            "text there would separate the run from the declaration it describes");
        vm.ReadOnlySections.CanInsert(At(vm, "Attribute Total.VB_UserMemId") + 3).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Insertion_WhereAnAttributeRunEnds_IsAllowed()
    {
        var vm = OpenClass();
        vm.ReadOnlySections.CanInsert(At(vm, "    Total = 0")).Should().BeTrue();
    }

    // -- Deletion -----------------------------------------------------------------------------------

    [AvaloniaFact]
    public void Deletion_OfOrdinaryCode_IsUntouched()
    {
        var vm = OpenClass();
        var from = At(vm, "Public Sub Clear");
        var to = LineEndWithTerminator(vm, "End Sub");
        Deletable(vm, from, to).Should().Equal((from, to));
    }

    [AvaloniaFact]
    public void Deletion_OfAWholeProcedure_TakesItsAttributeRunWithIt()
    {
        var vm = OpenClass();
        var from = At(vm, "Public Function Total");
        var to = LineEndWithTerminator(vm, "End Function");

        Deletable(vm, from, to).Should().Equal([(from, to)],
            "the run describes a member that is going; leaving it behind would attach it to whatever is above");
    }

    [AvaloniaFact]
    public void Deletion_ThatStartsPartWayIntoTheDeclaration_CarvesTheRunOut()
    {
        var vm = OpenClass();
        var from = At(vm, "Function Total");
        var to = LineEndWithTerminator(vm, "    Total = 0");

        // The declaration line survives in part, so the run still describes it — and the declaration's own
        // terminator stays too, or the first attribute line would join onto what is left of it.
        Deletable(vm, from, to).Should().Equal(
            (from, LineEnd(vm, "Public Function Total")),
            (At(vm, "    Total = 0"), to));
    }

    [AvaloniaFact]
    public void Deletion_OfTheTerminatorAboveAnAttributeRun_IsRefused()
    {
        var vm = OpenClass();
        var terminator = LineEnd(vm, "Public Function Total");

        Deletable(vm, terminator, At(vm, "Attribute Total.VB_Description")).Should().BeEmpty(
            "Delete at the end of the declaration would join the first attribute line onto it");
    }

    [AvaloniaFact]
    public void TypingAndEveryOtherWriterAgreeAboutTheLineBreakARunHangsFrom()
    {
        // The provider guards typing and IsReadOnlyRegion guards every other writer (#273 tasks 3.9 and
        // 3.11). They disagreed once: typing could not delete this line break and Replace could.
        var vm = OpenClass();
        var terminator = LineEnd(vm, "Public Function Total");

        Deletable(vm, terminator, terminator + 2).Should().BeEmpty();
        vm.IsReadOnlyRegion(terminator, 2).Should().BeTrue();

        vm.ReadOnlySections.CanInsert(terminator).Should().BeTrue();
        vm.IsReadOnlyRegion(terminator, 0).Should().BeFalse();
    }

    [AvaloniaFact]
    public void Deletion_OfTheWholeBuffer_KeepsTheHeader()
    {
        var vm = OpenClass();
        var codeStart = At(vm, "Option Explicit");

        Deletable(vm, 0, vm.Document.TextLength).Should().Equal((codeStart, vm.Document.TextLength));
    }

    // -- The whole-document verdict ----------------------------------------------------------------

    [AvaloniaFact]
    public void AnUnfaithfulForm_RefusesTypingAnywhere()
    {
        var form = AForm();
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        var vm = NewEditor().Initialize(form);
        var code = At(vm, "    Caption = ");

        vm.ReadOnlySections.CanInsert(code).Should().BeFalse();
        Deletable(vm, code, code + 4).Should().BeEmpty();
    }

    [AvaloniaFact]
    public void AnUnfaithfulForm_StillHasNoRegionOverItsCode()
    {
        // Breakpoints and Find ask about regions. A form held read-only as a whole must still take a
        // breakpoint on any line of its code and still answer Find.
        var form = AForm();
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        var vm = NewEditor().Initialize(form);

        vm.IsReadOnlyRegion(At(vm, "    Caption = "), 0).Should().BeFalse();
        vm.IsReadOnlyRegion(At(vm, "Private Sub Form_Load"), 1).Should().BeFalse();
    }

    [AvaloniaFact]
    public void AReloadThatClearsTheVerdict_MakesTheFormTypableAndTellsTheBanner()
    {
        var form = AForm();
        form.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        var vm = NewEditor().Initialize(form);
        var raised = new List<string?>();
        ((INotifyPropertyChanged)vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        // What FileReloader does after the file was fixed externally: the model adopts the fresh verdict,
        // then the window reloads. The code is byte-identical, which is the case that used to leave the
        // banner describing the old file.
        form.AdoptFidelityState(AForm());
        var text = vm.Document.Text;
        var prefixEnd = At(vm, "Attribute VB_Name");
        vm.ReloadFrom(text[..prefixEnd], text[prefixEnd..]);

        vm.ReadOnlySections.CanInsert(At(vm, "    Caption = ")).Should().BeTrue();
        raised.Should().Contain([nameof(CodeEditorViewModel.IsReadOnly), nameof(CodeEditorViewModel.ReadOnlyReason)]);
    }

    [AvaloniaFact]
    public void AReloadThatSetsTheVerdict_StopsTypingWithoutReinstallingAnything()
    {
        var form = AForm();
        var vm = NewEditor().Initialize(form);
        var provider = vm.ReadOnlySections;
        provider.CanInsert(At(vm, "    Caption = ")).Should().BeTrue();

        var fresh = AForm();
        fresh.MarkUnfaithfulToSave(UnfaithfulSaveCause.NestedContainers, "a menu was flattened");
        form.AdoptFidelityState(fresh);
        var text = vm.Document.Text;
        var prefixEnd = At(vm, "Attribute VB_Name");
        vm.ReloadFrom(text[..prefixEnd], text[prefixEnd..]);

        vm.ReadOnlySections.Should().BeSameAs(provider, "the view installs it once");
        provider.CanInsert(At(vm, "    Caption = ")).Should().BeFalse();
    }

    // -- The region cache ---------------------------------------------------------------------------

    [AvaloniaFact]
    public void TheRegions_FollowAnEdit()
    {
        var vm = OpenClass();
        var before = vm.ReadOnlyRegionsNow;
        vm.ReadOnlyRegionsNow.Should().BeSameAs(before, "nothing changed, so the scan is not repeated");

        vm.Document.Insert(At(vm, "Public Function Total"), "' a comment\r\n");

        vm.ReadOnlyRegionsNow.Should().NotBeSameAs(before);
        vm.ReadOnlySections.CanInsert(At(vm, "Attribute Total.VB_Description")).Should().BeFalse(
            "the run moved down a line, and the provider must see where it is now");
    }
}
