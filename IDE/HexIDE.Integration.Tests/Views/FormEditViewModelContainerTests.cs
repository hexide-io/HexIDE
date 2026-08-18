using System.Linq;
using Avalonia;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.VisualDesigner;
using NSubstitute;

namespace HexIDE.Integration.Tests.Views;

/// <summary>
/// Issue #84 phase 6 — the designer learns that model coordinates are relative.
///
/// The designer keeps ONE flat canvas and does the arithmetic, because it cannot have a real visual tree while
/// re-parenting is out of scope. So the boundary between the VB6 container-relative number the model holds and
/// the canvas coordinate everything in the designer reads is exactly one place: the view-model's Left/Top.
///
/// This file is also the first test coverage the designer view-model has had at all. Before it, only the
/// file-watcher reload and the read-only banner referenced FormEditViewModel across all four suites, and
/// neither exercised selection, drag, align, z-order, clipboard or undo.
/// </summary>
public class FormEditViewModelContainerTests
{
    private static readonly ProjectDefinition Project = new(VBProjectType.EXE, "P");

    private sealed class NullSink : IDeserializeErrorSink
    {
        public static readonly NullSink Instance = new();
        public void LogError(string _) { }
    }

    private static FormDefinition Deserialize(string source) =>
        new FormDeserializer().Deserialize(Project, source, NullSink.Instance)!;

    // Only the dependencies these paths actually touch need to be real: the event bus, the localization
    // service ComputeTitle reads, and the settings service PasteControls takes its grid offset from.
    private static FormEditViewModel NewDesignerVm() => new(
        null!, Substitute.For<IEventBus>(), null!, null!, null!, Substitute.For<ISettingsService>(),
        Substitute.For<ILocalizationService>());

    private static FormEditViewModel Open(string frm) => NewDesignerVm().Initialize(Deserialize(frm));

    private static ComponentInstanceViewModel Find(FormEditViewModel vm, string name) =>
        vm.AllComponents.First(c => c.Name == name);

    // picOuter at 300 twips = 20 px with a default (bordered) 2 px inset, so its client origin is 22 px.
    // fraInner records 150 twips = 10 px inside it, i.e. 32 px on the canvas; cmdDeep records 10 px inside the
    // Frame, whose own inset is zero, i.e. 42 px.
    private const string NestedForm = """
        VERSION 5.00
        Begin VB.Form Form1
           ClientWidth     =   6000
           ClientHeight    =   4000
           Begin VB.PictureBox picOuter
              Left            =   300
              Top             =   300
              Width           =   4500
              Height          =   3000
              Begin VB.Frame fraInner
                 Caption         =   "Inner"
                 Left            =   150
                 Top             =   150
                 Width           =   3000
                 Height          =   1500
                 Begin VB.CommandButton cmdDeep
                    Caption         =   "Deep"
                    Left            =   150
                    Top             =   150
                    Width           =   1200
                    Height          =   375
                 End
              End
           End
           Begin VB.CommandButton cmdOnForm
              Caption         =   "Form"
              Left            =   300
              Top             =   3600
              Width           =   1200
              Height          =   375
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // Microsoft's own shape: four sibling controls sharing one name.
    private const string ControlArrayForm = """
        VERSION 5.00
        Begin VB.Form Form1
           Begin VB.PictureBox picOptions
              Index           =   0
              Left            =   0
              Top             =   0
              Width           =   1500
              Height          =   1500
           End
           Begin VB.PictureBox picOptions
              Index           =   1
              Left            =   1800
              Top             =   0
              Width           =   1500
              Height          =   1500
           End
        End
        Attribute VB_Name = "Form1"
        """;

    // ── the coordinate boundary ───────────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void AContainedControl_ReportsItsCanvasPositionAndItsRelativeOne()
    {
        var vm = Open(NestedForm);
        var deep = Find(vm, "cmdDeep");

        // 20 (picOuter) + 2 (its border) + 10 (fraInner) + 0 (a Frame insets nothing) + 10 (cmdDeep).
        deep.Left.Should().BeApproximately(42, 0.01);
        deep.Top.Should().BeApproximately(42, 0.01);

        // The model keeps the VB6 number, which is what the property grid and the status bar show.
        deep.RelativeLeft.Should().BeApproximately(10, 0.01);
        deep.RelativeTop.Should().BeApproximately(10, 0.01);
    }

    [AvaloniaFact]
    public void AControlOnTheForm_IsUnaffected()
    {
        var vm = Open(NestedForm);
        var onForm = Find(vm, "cmdOnForm");

        // The form's client area IS the canvas, so absolute and relative coincide — which is why this whole
        // change moves nothing on a form with no containers in it.
        onForm.Left.Should().BeApproximately(20, 0.01);
        onForm.RelativeLeft.Should().BeApproximately(20, 0.01);
    }

    [AvaloniaFact]
    public void WritingTheCanvasPosition_StoresTheRelativeOne()
    {
        var vm = Open(NestedForm);
        var deep = Find(vm, "cmdDeep");

        deep.Left = 62;

        deep.RelativeLeft.Should().BeApproximately(30, 0.01);
        deep.Left.Should().BeApproximately(62, 0.01);
    }

    [AvaloniaFact]
    public void AContainersClientRect_IsReportedInCanvasSpace()
    {
        var vm = Open(NestedForm);

        // fraInner's container is picOuter: origin 20 + 2 inset, size 300x200 less 2 px per side.
        var bounds = Find(vm, "fraInner").ContainerBounds;
        bounds.X.Should().BeApproximately(22, 0.01);
        bounds.Y.Should().BeApproximately(22, 0.01);
        bounds.Width.Should().BeApproximately(296, 0.01);

        // A control on the form gets the form's own client rect, which is what the resize clamp needs.
        var formBounds = Find(vm, "cmdOnForm").ContainerBounds;
        formBounds.X.Should().Be(0);
        formBounds.Y.Should().Be(0);
        formBounds.Width.Should().BeApproximately(400, 0.01);
    }

    [AvaloniaFact]
    public void MovingAContainer_NotifiesEveryDescendant()
    {
        var vm = Open(NestedForm);
        var outer = Find(vm, "picOuter");
        var inner = Find(vm, "fraInner");
        var deep = Find(vm, "cmdDeep");

        var raised = new System.Collections.Generic.List<string>();
        inner.PropertyChanged += (_, e) => raised.Add($"inner.{e.PropertyName}");
        deep.PropertyChanged += (_, e) => raised.Add($"deep.{e.PropertyName}");

        outer.Left = 100;

        // Without the fan-out the model raises nothing for the descendants, so the TwoWay (Canvas.Left)
        // setters never fire: the children stay drawn where they were while the marquee and the align commands
        // read the new value. They would only jump into place on the next reload.
        raised.Should().Contain("inner.Left").And.Contain("inner.ContainerBounds");
        raised.Should().Contain("deep.Left").And.Contain("deep.ContainerBounds");

        // And the values themselves follow, with the relative ones untouched.
        deep.Left.Should().BeApproximately(122, 0.01);
        deep.RelativeLeft.Should().BeApproximately(10, 0.01);
    }

    // ── drag, z-order, delete, paste ──────────────────────────────────────────────────────────────

    [AvaloniaFact]
    public void ADragSkipsAControlWhoseContainerIsAlsoSelected()
    {
        var vm = Open(NestedForm);
        var outer = Find(vm, "picOuter");
        var deep = Find(vm, "cmdDeep");
        var onForm = Find(vm, "cmdOnForm");

        // The selection a marquee across picOuter produces on a flat canvas — a selection VB6 never makes,
        // because it scopes the marquee to the container the drag began in.
        var effective = vm.WithoutRedundantDragTargets([outer, Find(vm, "fraInner"), deep, onForm]);

        effective.Should().Equal(outer, onForm);
    }

    [AvaloniaFact]
    public void ZOrderIsScopedToSiblings()
    {
        var vm = Open(NestedForm);
        var inner = Find(vm, "fraInner");

        // fraInner's only sibling inside picOuter is itself, so neither command may move it — in particular
        // Bring to Front must not lift it out of its container and onto the end of the whole canvas list.
        var before = vm.Components.IndexOf(inner);
        vm.BringToFront(inner);
        vm.Components.IndexOf(inner).Should().Be(before);

        // The two form-level controls are each other's siblings, so they do reorder.
        var onForm = Find(vm, "cmdOnForm");
        var outer = Find(vm, "picOuter");
        vm.SendToBack(onForm);
        vm.Components.IndexOf(onForm).Should().BeLessThan(vm.Components.IndexOf(outer));
    }

    [AvaloniaFact]
    public void DeletingAContainer_TakesItsContentsAndRestoresThemInOneUndo()
    {
        var vm = Open(NestedForm);
        var outer = Find(vm, "picOuter");
        var before = vm.AllComponents.Count;

        vm.SetSelectedComponents([outer]);
        vm.DeleteSelected();

        // picOuter, fraInner and cmdDeep — leaving the contents behind would leave controls whose container is
        // no longer in the form, and the write-back would try to nest them inside a component it is not writing.
        vm.AllComponents.Count.Should().Be(before - 3);

        vm.UndoStack.Undo();

        vm.AllComponents.Count.Should().Be(before);
        Find(vm, "cmdDeep").Left.Should().BeApproximately(42, 0.01);
        vm.UndoStack.CanUndo.Should().BeFalse("restoring a container and its contents is one undo, not three");
    }

    [AvaloniaFact]
    public void CopyingAContainer_CopiesItsContentsIntoTheCopy()
    {
        var vm = Open(NestedForm);
        var outer = Find(vm, "picOuter");

        vm.SetSelectedComponents([outer]);
        vm.CopySelectedControls();
        vm.PasteControls();

        var pictures = vm.AllComponents.Where(c => c.Instance.BaseClass is PictureBoxComponentClass).ToList();
        pictures.Should().HaveCount(2);

        var copy = pictures.Last();
        copy.Instance.ContainedControls.Should().HaveCount(1, "the copy gets its own children, not the original's");
        copy.Instance.ContainedControls[0].Should().NotBeSameAs(outer.Instance.ContainedControls[0]);
        outer.Instance.ContainedControls.Should().HaveCount(1, "and the original keeps exactly its own");
    }

    [AvaloniaFact]
    public void CopyingOutOfAContainerAndPastingBack_KeepsTheControlInIt()
    {
        var vm = Open(NestedForm);
        var deep = Find(vm, "cmdDeep");
        var inner = Find(vm, "fraInner");

        vm.SetSelectedComponents([deep]);
        vm.CopySelectedControls();
        vm.PasteControls();

        // The source container is still on this form, so the copy belongs in it and its recorded
        // container-relative Left is still the right number.
        var copy = vm.AllComponents.Last();
        copy.Instance.Container.Should().BeSameAs(inner.Instance);
        copy.RelativeLeft.Should().BeGreaterThan(0);
        copy.Left.Should().BeGreaterThan(inner.Left, "the copy is drawn inside the frame it was copied from");
    }

    // ── the guard that rejected Microsoft's own data ──────────────────────────────────────────────

    [AvaloniaFact]
    public void RenamingAControlToItsOwnName_IsAccepted()
    {
        var vm = Open(NestedForm);
        var deep = Find(vm, "cmdDeep");

        // The property grid commits Name on every focus change, so this fired constantly.
        var rename = () => deep.Instance.SetProperty(VBProperties.NameProperty, "cmdDeep");

        rename.Should().NotThrow();
    }

    [AvaloniaFact]
    public void ANameAlreadyHeldByOneOtherControl_IsStillRejected()
    {
        var vm = Open(NestedForm);
        var deep = Find(vm, "cmdDeep");

        var collide = () => deep.Instance.SetProperty(VBProperties.NameProperty, "cmdOnForm");

        collide.Should().Throw<DataValidationException>();
    }

    [AvaloniaFact]
    public void AControlArraysSharedName_IsNotTreatedAsACollision()
    {
        var vm = Open(ControlArrayForm);

        // Two sibling picOptions load without complaint, and a rename to that name is not a collision with a
        // standalone control — VB6's rule is uniqueness per name AND Index, and Options Dialog.frm ships four.
        vm.AllComponents.Count(c => c.Name == "picOptions").Should().Be(2);

        var rename = () => vm.AllComponents.First(c => c.Name == "picOptions")
                             .Instance.SetProperty(VBProperties.NameProperty, "picOptions");
        rename.Should().NotThrow();
    }
}
