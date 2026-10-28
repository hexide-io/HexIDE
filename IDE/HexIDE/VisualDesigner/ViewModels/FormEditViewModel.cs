using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Labs.Input;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.Serialization;
using HexIDE.Utils;
using HexIDE.VisualDesigner.Commands;
using PropertyChanged.SourceGenerator;
using R3;
using Serilog;

namespace HexIDE.VisualDesigner;

public partial class FormEditViewModel : BaseEditorWindowViewModel
{
    private FormDefinition? formDefinition;
    private readonly IEventBus eventBus;
    private readonly IProjectService projectService;
    private readonly IEditorService editorService;
    private readonly IWindowManager windowManager;
    private readonly ISettingsService settingsService;
    private readonly ILocalizationService localization;
    public ToolBoxToolViewModel ToolsBoxToolViewModel { get; }
    public ISettingsService Settings => settingsService;
    protected override string ComputeTitle()
    {
        var kind = formDefinition?.RootVBTypeName switch
        {
            "VB.UserControl" => localization.GetString("Str.Document.UserControlSuffix"),
            "VB.PropertyPage" => localization.GetString("Str.Document.PropertyPageSuffix"),
            _ => localization.GetString("Str.Document.FormSuffix")
        };
        return $"{formDefinition?.Owner.Name} - {formDefinition?.Name} ({kind})";
    }

    /// <summary>
    /// The class of the designer's root, as the Properties window's object box names it: Form, UserControl or PropertyPage
    /// (#1544). All three are a form's component, so its own class would call each a Form.
    /// </summary>
    public string RootClassName => (formDefinition?.RootVBTypeName ?? "VB.Form") is var type && type.Contains('.')
        ? type[(type.LastIndexOf('.') + 1)..]
        : type;

    /// <summary>
    /// Whether the root is drawn in a window's frame, with a title bar. A UserControl or PropertyPage has no window of its
    /// own at run time, and VB6's designer draws it as a borderless surface (#1544).
    /// </summary>
    public bool HasWindowFrame => formDefinition?.RootVBTypeName is not ("VB.UserControl" or "VB.PropertyPage");

    public override object? Icon { get; } = HexIDE.Utils.IconFactory.Themed("Geo.Form");

    [Notify]
    private ComponentInstanceViewModel? selectedComponent;

    public IReadOnlyList<ComponentInstanceViewModel> SelectedComponents { get; private set; } = [];

    public void SetSelectedComponents(IEnumerable<ComponentInstanceViewModel> components)
    {
        SelectedComponents = [.. components];
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(SelectedComponents)));
        CommandManager.InvalidateRequerySuggested();
    }

    private bool HasMultipleSelectedControls => SelectedComponents.Count >= 2;

    public bool CanAlignLefts(object? parameter) => HasMultipleSelectedControls;
    public bool CanAlignRights(object? parameter) => HasMultipleSelectedControls;
    public bool CanAlignTops(object? parameter) => HasMultipleSelectedControls;
    public bool CanAlignBottoms(object? parameter) => HasMultipleSelectedControls;
    public bool CanAlignCentersH(object? parameter) => HasMultipleSelectedControls;
    public bool CanAlignCentersV(object? parameter) => HasMultipleSelectedControls;
    public bool CanMakeSameWidth(object? parameter) => HasMultipleSelectedControls;
    public bool CanMakeSameHeight(object? parameter) => HasMultipleSelectedControls;
    public bool CanMakeSameSize(object? parameter) => HasMultipleSelectedControls;

    public ObservableCollection<ComponentInstanceViewModel> AllComponents { get; } = new();

    /// <summary>
    /// The view-model wrapping a given model component, for the walks that start from the model tree — the
    /// containment chain and the descendant fan-out.
    ///
    /// A scan rather than a dictionary on purpose: <see cref="AllComponents"/> is mutated by spawn, delete,
    /// paste and reload, and a second index would be one more thing to keep in step for a collection that
    /// holds tens of entries on a busy form.
    /// </summary>
    internal bool TryGetViewModel(ComponentInstance instance, out ComponentInstanceViewModel viewModel)
    {
        foreach (var candidate in AllComponents)
        {
            if (ReferenceEquals(candidate.Instance, instance))
            {
                viewModel = candidate;
                return true;
            }
        }
        viewModel = null!;
        return false;
    }

    /// <summary>
    /// The controls the canvas draws, in the order it draws them, the last on top. That is the reverse of each
    /// container's run of blocks in the file, where VB6 puts the control on top first (measured, #1481), so the list is
    /// built from the file on load (<see cref="DrawOrder"/>) and turned back into it on save
    /// (<see cref="ComponentsAsSaved"/>). A container comes before what it holds, which is drawn over it. Loaded menus are
    /// here too, last; they are never drawn. A Line is stacked by this order like any other control: among the windowless
    /// ones, under every windowed control in its container, as a running form draws it (measured, #1497).
    /// </summary>
    public ObservableCollection<ComponentInstanceViewModel> Components { get; } = new();

    /// <summary>
    /// The controls on this form HexIDE does not model, each drawn where VB6 draws the box it puts in such a control's
    /// place (#157). Built from the blocks they were preserved as, and in neither <see cref="Components"/> nor
    /// <see cref="AllComponents"/>: those are what the designer edits and the writer writes, and these are carried.
    /// </summary>
    public ObservableCollection<UnmodelledControlViewModel> UnmodelledControls { get; } = new();

    // Rebuilt when a component holding such a control comes or goes, so a box goes with the container it sits in when that
    // is deleted, and comes back with it on undo. No other change can alter the boxes.
    private void OnAllComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        static bool HoldsAny(IList? items) =>
            items?.OfType<ComponentInstanceViewModel>().Any(c => c.Instance.PreservedChildSubtrees.Count > 0) == true;

        if (e.Action == NotifyCollectionChangedAction.Reset || HoldsAny(e.NewItems) || HoldsAny(e.OldItems))
            RebuildUnmodelledControls();
    }

    private void RebuildUnmodelledControls()
    {
        foreach (var box in UnmodelledControls)
            box.Dispose();
        UnmodelledControls.Clear();
        foreach (var vm in AllComponents)
        {
            var blocks = vm.Instance.PreservedChildSubtrees;
            for (var i = 0; i < blocks.Count; i++)
            {
                // One on the root, a form's or a UserControl's, is measured from the canvas origin, as a control with no
                // container is (ComponentInstanceViewModel.ContainerBounds).
                if (UnmodelledControl.Read(blocks[i].Text) is { } control)
                    UnmodelledControls.Add(new UnmodelledControlViewModel(control, ReferenceEquals(vm, Form) ? null : vm,
                        depth: blocks.Count - 1 - i));
            }
        }
        RestackCarried();
    }

    // The canvas stacks by ZIndex, the highest on top. Each control's is its place in Components, spaced so that the boxes
    // of carried controls fit between, each one where the controls under it put it (#1553).
    private const int StackSpacing = 4096;

    private void OnComponentsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        for (var i = 0; i < Components.Count; i++)
            Components[i].DrawIndex = (i + 1) * StackSpacing;
        RestackCarried();
    }

    /// <summary>
    /// Puts each carried control's box in its place among the controls the designer draws (#1553): over the last of its
    /// container's controls that is under it, with what that one holds, or over the container itself where none is. A
    /// control is under the box when fewer of the container's carried controls are under the control than under the box.
    /// The boxes in one place go in the order their blocks are written, the first on top.
    /// </summary>
    internal void RestackCarried()
    {
        foreach (var box in UnmodelledControls)
        {
            var container = box.Container?.Instance ?? Form.Instance;
            var count = container.PreservedChildSubtrees.Count;
            var slot = box.Container is null ? -1 : Components.IndexOf(box.Container);
            for (var i = 0; i < Components.Count; i++)
                if (IsContentOf(Components[i], container) && Math.Clamp(Components[i].CarriedBelow, 0, count) <= box.Depth)
                    slot = RegionOf(Components, Components[i].Instance).End - 1;
            box.DrawIndex = (slot + 1) * StackSpacing + 1 + box.Depth;
        }
    }

    public ComponentInstanceViewModel Form { get; private set; }

    /// <summary>
    /// The procedure a double click on the designer's background writes: the root's own event, a form's Form_Load, a
    /// UserControl's UserControl_Initialize and a PropertyPage's PropertyPage_SelectionChanged (measured, #1632).
    /// </summary>
    public string RootDoubleClickProcedure => Form.Instance.BaseClass is FormComponentClass root
        ? $"{root.Name}_{root.DesignerDoubleClickEvent}"
        : "Form_Load";

    /// <summary>
    /// The strings this designer and its component view models show. Internal because a component view
    /// model has no services of its own and needs one to phrase a refusal.
    /// </summary>
    internal ILocalizationService Localization => localization;

    public ObservableCollection<ComponentInstanceViewModel> TopLevelMenu { get; } = new();

    public IEventBus EventBus => eventBus;

    public FormDefinition? FormDefinition => formDefinition;

    /// <inheritdoc/>
    public override DocumentIdentity? OpenDocument =>
        formDefinition is null ? null : DocumentIdentity.For(formDefinition);

    /// <summary>
    /// True when this form cannot be written back faithfully, so editing it would waste the developer's
    /// time — the save is refused (see ProjectService.SaveForm) and every change is lost.
    ///
    /// Enforced by disabling the design surface rather than by guarding each of the twenty-five mutation
    /// methods: input that never arrives cannot mutate anything, and a guard-per-method would rot the
    /// first time someone adds the twenty-sixth. There is no CanExecute routing to hook into either.
    /// </summary>
    public bool IsReadOnly => formDefinition is { CanSaveFaithfully: false };

    public string? ReadOnlyReason => formDefinition?.UnfaithfulSaveReason;

    public DesignerUndoStack UndoStack { get; private set; } = null!;
    public DelegateCommand UndoCommand { get; private set; } = null!;
    public DelegateCommand RedoCommand { get; private set; } = null!;
    public bool CanUndo => UndoStack?.CanUndo ?? false;
    public bool CanRedo => UndoStack?.CanRedo ?? false;
    public bool IsDragging { get; private set; }
    private Dictionary<ComponentInstanceViewModel, Rect>? _dragStartRects;

    public FormEditViewModel(ToolBoxToolViewModel toolsBoxToolViewModel,
        IEventBus eventBus,
        IProjectService projectService,
        IEditorService editorService,
        IWindowManager windowManager,
        ISettingsService settingsService,
        ILocalizationService localization) : this()
    {
        this.eventBus = eventBus;
        this.projectService = projectService;
        this.editorService = editorService;
        this.windowManager = windowManager;
        this.settingsService = settingsService;
        this.localization = localization;
        // Refresh the tab title (its "(Form)"/"(UserControl)" suffix is localized) on language change. Unsubscribe on
        // Dispose (tab close) — a raw `+=` kept the whole designer VM (+ its component/undo graph) reachable from the
        // singleton localization service forever, so closed designers never got collected.
        Action onLanguageChanged = () => Title = ComputeTitle();
        localization.LanguageChanged += onLanguageChanged;
        AutoDispose(new ActionDisposable(() => localization.LanguageChanged -= onLanguageChanged));
        ToolsBoxToolViewModel = toolsBoxToolViewModel;
        UndoStack = new DesignerUndoStack(this);
        UndoCommand = new DelegateCommand(() => UndoStack.Undo(), () => UndoStack.CanUndo);
        RedoCommand = new DelegateCommand(() => UndoStack.Redo(), () => UndoStack.CanRedo);
        UndoStack.Changed += () =>
        {
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(CanUndo)));
            OnPropertyChanged(new System.ComponentModel.PropertyChangedEventArgs(nameof(CanRedo)));
            UndoCommand.RaiseCanExecutedChanged();
            RedoCommand.RaiseCanExecutedChanged();
            RefreshModified();
        };
        AutoDispose(this.eventBus.Subscribe<DocumentSavedEvent>(e =>
        {
            if ((e.Form is not null && ReferenceEquals(e.Form, formDefinition))
                || (e.Module is not null && ReferenceEquals(e.Module, formDefinition?.OwningModule)))
                RefreshModified();
        }));
        AutoDispose(this.eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(_ => FlushComponentsToModel()));
        AutoDispose(this.eventBus.Subscribe<ModuleUnloadedEvent>(e =>
        {
            if (e.Module == formDefinition?.OwningModule)
                RequestClose();
        }));
        AutoDispose(this.eventBus.Subscribe<FormUnloadedEvent>(e =>
        {
            if (e.Form == formDefinition)
                RequestClose();
        }));
        AutoDispose(this.eventBus.Subscribe<ProjectUnloadedEvent>(_ => { DesignerClipboard.Clear(); UndoStack.Clear(); }));
    }

    /// <summary>
    /// The component list a save would hand the model, in the order it would hand it: the form, then each container's
    /// contents with the one drawn on top first, as VB6 writes them, each followed by what it holds; then the menus,
    /// which are not drawn, in the order they have always had. The inverse of <see cref="DrawOrder"/>.
    /// </summary>
    private (List<ComponentInstance> Ordered, Dictionary<ComponentInstanceViewModel, int> PositionInList) ComponentsAsSaved()
    {
        var positionInList = Components.Select((comp, index) => (comp, index)).ToDictionary(x => x.comp, x => x.index);
        var contents = new Dictionary<ComponentInstance, List<ComponentInstanceViewModel>>(ReferenceEqualityComparer.Instance);
        foreach (var vm in AllComponents)
        {
            if (vm == Form || IsMenu(vm))
                continue;
            var parent = ParentOf(vm.Instance);
            if (!contents.TryGetValue(parent, out var list))
                contents[parent] = list = [];
            list.Add(vm);
        }

        var ordered = new List<ComponentInstance> { Form.Instance };
        var placed = new HashSet<ComponentInstance>(ReferenceEqualityComparer.Instance) { Form.Instance };
        void Place(ComponentInstance container)
        {
            if (!contents.TryGetValue(container, out var list))
                return;
            foreach (var vm in list.OrderByDescending(c => positionInList.GetValueOrDefault(c, int.MaxValue)))
            {
                if (!placed.Add(vm.Instance))
                    continue;
                ordered.Add(vm.Instance);
                Place(vm.Instance);
            }
        }
        Place(Form.Instance);

        // A control whose container is not on this form is still handed over rather than lost; the writer refuses a tree
        // it cannot place.
        ordered.AddRange(AllComponents.Where(c => c != Form && !IsMenu(c) && !placed.Contains(c.Instance)).Select(c => c.Instance));
        ordered.AddRange(AllComponents.Where(IsMenu).OrderBy(c => positionInList.GetValueOrDefault(c, 0)).Select(c => c.Instance));
        return (ordered, positionInList);
    }

    /// <summary>
    /// The order the canvas draws a form's components in, the last on top, from the order the file holds them in, where
    /// the first block in each container is the control on top (measured, #1481). Each container's contents go last in
    /// the file first, each followed by what it holds. A VB6 file lists a container's windowed controls before its
    /// windowless ones, so the windowless come out under them, as VB6 draws them. Menus are not drawn and go last, in
    /// the file's order. <see cref="ComponentsAsSaved"/> is the inverse.
    /// </summary>
    private List<ComponentInstance> DrawOrder(IReadOnlyList<ComponentInstance> fileOrder)
    {
        var contents = new Dictionary<ComponentInstance, List<ComponentInstance>>(ReferenceEqualityComparer.Instance);
        foreach (var component in fileOrder)
        {
            if (ReferenceEquals(component, Form.Instance) || component.BaseClass is MenuComponentClass)
                continue;
            var parent = ParentOf(component);
            if (!contents.TryGetValue(parent, out var list))
                contents[parent] = list = [];
            list.Add(component);
        }

        var order = new List<ComponentInstance>();
        var drawn = new HashSet<ComponentInstance>(ReferenceEqualityComparer.Instance);
        void Draw(ComponentInstance container)
        {
            if (!contents.TryGetValue(container, out var list))
                return;
            for (var i = list.Count - 1; i >= 0; i--)
            {
                if (!drawn.Add(list[i]))
                    continue;
                order.Add(list[i]);
                Draw(list[i]);
            }
        }
        Draw(Form.Instance);

        order.AddRange(fileOrder.Where(c =>
            !ReferenceEquals(c, Form.Instance) && c.BaseClass is not MenuComponentClass && !drawn.Contains(c)));
        order.AddRange(fileOrder.Where(c => c.BaseClass is MenuComponentClass));
        return order;
    }

    /// <summary>What a control is drawn in: its container, or the form for one at form level.</summary>
    private ComponentInstance ParentOf(ComponentInstance control) =>
        control.Container is { } container && !ReferenceEquals(container, Form.Instance) ? container : Form.Instance;

    private static bool IsMenu(ComponentInstanceViewModel component) => component.Instance.BaseClass is MenuComponentClass;

    private static bool IsWindowless(IComponentClass type) => type is ComponentBaseClass { IsWindowless: true };

    private bool IsContentOf(ComponentInstanceViewModel component, ComponentInstance container) =>
        !IsMenu(component) && ReferenceEquals(ParentOf(component.Instance), container);

    /// <summary>
    /// Where a container's contents are in a draw-order list: the run after it of what it holds, at any depth, or the
    /// whole list for the form.
    /// </summary>
    private (int Start, int End) RegionOf(IReadOnlyList<ComponentInstanceViewModel> list, ComponentInstance container)
    {
        if (ReferenceEquals(container, Form.Instance))
            return (0, list.Count);
        var at = -1;
        for (var i = 0; i < list.Count && at == -1; i++)
            if (ReferenceEquals(list[i].Instance, container))
                at = i;
        if (at == -1)
            return (list.Count, list.Count);
        var end = at + 1;
        while (end < list.Count && Holds(container, list[end].Instance))
            end++;
        return (at + 1, end);
    }

    private static bool Holds(ComponentInstance container, ComponentInstance control)
    {
        for (var c = control.Container; c is not null; c = c.Container)
            if (ReferenceEquals(c, container))
                return true;
        return false;
    }

    /// <summary>
    /// Where a container's windowless contents end and its windowed ones begin in a draw-order list: at its first windowed
    /// control, or the end of its contents when it has none.
    /// </summary>
    private int PlaneBoundary(IReadOnlyList<ComponentInstanceViewModel> list, ComponentInstance container)
    {
        var (start, end) = RegionOf(list, container);
        for (var i = start; i < end; i++)
            if (IsContentOf(list[i], container) && !IsWindowless(list[i].Instance.BaseClass))
                return i;
        return end;
    }

    /// <summary>
    /// Where a control goes in a draw-order list to be on top of its plane in a container, which is where VB6 puts a
    /// control added there, and where Bring to Front moves one: first in that plane's run of the container's blocks
    /// (measured, #1481). Over everything the container holds for a windowed control; under every windowed control in it
    /// for a windowless one.
    /// </summary>
    private int TopOfPlane(IReadOnlyList<ComponentInstanceViewModel> list, ComponentInstance container, bool windowless) =>
        windowless ? PlaneBoundary(list, container) : RegionOf(list, container).End;

    /// <summary>Where Send to Back moves a control: last in its plane's run of its container's blocks (measured, #1481).</summary>
    private int BottomOfPlane(IReadOnlyList<ComponentInstanceViewModel> list, ComponentInstance container, bool windowless) =>
        windowless ? RegionOf(list, container).Start : PlaneBoundary(list, container);

    /// <summary>
    /// How many of a container's carried controls a control on top of its plane there, or at the bottom of it, has under
    /// it. A carried control stacks as any other (measured, #1498): one added or brought to front goes over all of them,
    /// and one sent to back under all of them in its plane.
    /// </summary>
    /// <remarks>
    /// HexIDE cannot tell a carried control's plane from its class, so it reads it from the file. VB6 writes every windowed
    /// block before every windowless one, so a carried block written after a Label is windowless, and the rest are windowed.
    /// The windowless ones are those under the topmost windowless control, not counting <paramref name="except"/>, the one
    /// being placed.
    /// </remarks>
    private int CarriedBelowInPlane(ComponentInstance container, bool windowless, bool top, ComponentInstanceViewModel? except)
    {
        var carried = container.PreservedChildSubtrees.Count;
        var windowlessCarried = AllComponents
            .Where(c => c != except && c != Form && IsContentOf(c, container) && IsWindowless(c.Instance.BaseClass))
            .Select(c => Math.Clamp(c.CarriedBelow, 0, carried))
            .DefaultIfEmpty(0)
            .Max();
        return (windowless, top) switch
        {
            (false, true) => carried,
            (false, false) or (true, true) => windowlessCarried,
            (true, false) => 0,
        };
    }

    /// <summary>The children a container's block holds, in the order a save writes them, as the writer reads them.</summary>
    private IReadOnlyList<ComponentInstance> WrittenChildren(ComponentInstance container, IReadOnlyList<ComponentInstance> fileOrder) =>
        ReferenceEquals(container, Form.Instance) ? FormSerializer.RootChildren(fileOrder, container) : container.ContainedControls;

    /// <summary>
    /// Where a save puts each container's carried blocks among the children it writes, from how many of them each child
    /// has under it (#1498). Only containers holding any.
    /// </summary>
    private IEnumerable<(ComponentInstance Container, int[] Ordinals)> CarriedPlacement(IReadOnlyList<ComponentInstance> fileOrder)
    {
        foreach (var container in fileOrder)
        {
            var blocks = container.PreservedChildSubtrees;
            if (blocks.Count == 0)
                continue;
            var below = WrittenChildren(container, fileOrder)
                .Select(child => TryGetViewModel(child, out var vm) ? vm.CarriedBelow : 0)
                .ToList();
            yield return (container, CarriedOrder.Ordinals(below, blocks.Count));
        }
    }

    /// <summary>
    /// Puts <see cref="Components"/> in the given order, by moves, so each control keeps its realized view and the
    /// selection with it. What the order does not name keeps its place after those it does.
    /// </summary>
    internal void Arrange(IReadOnlyList<ComponentInstanceViewModel> order)
    {
        var next = 0;
        foreach (var component in order)
        {
            var at = Components.IndexOf(component);
            if (at == -1)
                continue;
            if (at != next)
                Components.Move(at, next);
            next++;
        }
    }

    /// <summary>
    /// True when the designer holds a control the model does not have yet, or lacks one it has, or orders them
    /// differently. Answered without flushing, so asking changes nothing.
    /// </summary>
    /// <remarks>
    /// Only membership and order need this. A control's properties, including its position and size, live on
    /// the same <see cref="ComponentInstance"/> the model holds, so an edit to one is already in the model;
    /// what waits for a save is the list itself. (#481)
    /// </remarks>
    public bool HasComponentsNotInModel
    {
        get
        {
            if (formDefinition is not { } form)
                return false;
            var ordered = ComponentsAsSaved().Ordered;
            return !ordered.SequenceEqual(form.Components)
                || CarriedPlacement(ordered).Any(p => !p.Ordinals.SequenceEqual(p.Container.PreservedChildSubtrees.Select(b => b.Ordinal)));
        }
    }

    /// <summary>
    /// Writes the designer's working collections through to <see cref="FormDefinition.Components"/>, in
    /// document order and with containment rebuilt.
    /// </summary>
    /// <remarks>
    /// <b>Membership and order run ahead of the model; property values do not.</b> The canvas and the model
    /// hold the same <c>ComponentInstance</c> objects, so a drag or a property-grid edit is in the model the
    /// instant it happens. What the model does not have until this runs is a control that was just added or
    /// deleted, and the order they sit in — which is why anything re-rendering the form has to call this
    /// first or it renders the form as it was one control ago.
    /// </remarks>
    public void FlushComponentsToModel()
    {
        var (orderedComponents, positionInList) = ComponentsAsSaved();
        RebuildContainmentOrder(positionInList);
        foreach (var (container, ordinals) in CarriedPlacement(orderedComponents).ToList())
            container.SetPreservedChildOrdinals(ordinals);
        formDefinition?.UpdateComponents(orderedComponents);
    }

    private void RefreshModified()
    {
        IsModified = HasComponentsNotInModel || (formDefinition is { } form &&
            (form.OwningModule is { } module ? projectService?.HasUnsavedChanges(module)
                : projectService?.HasUnsavedChanges(form)) == true);
    }

    /// <summary>
    /// Announces a committed change to this form's designer half, flushing the working collections into the
    /// model first so that whatever re-renders the form sees the change.
    /// </summary>
    /// <remarks>
    /// <b>Called once per commit, never per property write.</b> Every designer gesture funnels through
    /// <see cref="DesignerUndoStack"/>, which already discards pushes while a drag is in flight and takes a
    /// single command when the drag ends — so hanging this off the stack's three mutating operations gives
    /// exactly the granularity <see cref="FormLayoutChangedEvent"/> asks for. The surfaces that mutate the
    /// model without pushing anything (the menu editor, the colour palette) call it themselves.
    ///
    /// <para>
    /// It also announces a <b>rename</b>, which is the half of task 2.4a that was deferred here: a form's
    /// <c>Name</c> is derived from its root component's <c>(Name)</c> property and is only raised as a
    /// property change by <c>UpdateComponents</c>, so renaming a form in the property grid was not
    /// observable to the tab title or to the language layer until something flushed. This is that something.
    /// </para>
    /// </remarks>
    public void CommitLayout()
    {
        FlushComponentsToModel();
        RefreshModified();
        if (formDefinition is not null)
            eventBus.Publish(new FormLayoutChangedEvent(formDefinition));
    }

    public FormEditViewModel Initialize(FormDefinition formElement)
    {
        formDefinition = formElement;
        AutoDispose(formElement.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        AutoDispose(formElement.Owner.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        Populate(formElement.Components);

        SelectedComponent = Form;
        RefreshModified();
        Title = ComputeTitle();
        return this;
    }

    /// <summary>
    /// Rebuilds the designer canvas from the (freshly-reloaded) form model and clears the undo stack.
    /// Called after the file watcher reloads the <c>.frm</c> from disk: the model's components were
    /// replaced in place, so the bound <see cref="AllComponents"/>/<see cref="Components"/> collections
    /// are repopulated and the now-stale undo history is discarded. Must be called on the UI thread.
    /// </summary>
    internal void ReloadFromModel()
    {
        if (formDefinition is null)
            return;
        Populate(formDefinition.Components);

        SelectedComponent = Form;
        UndoStack.Clear();

        // The reload adopts the fresh parse's fidelity verdict, so the read-only state can have changed in
        // either direction — a form whose unhostable nesting was fixed externally becomes editable, one that
        // gained a blob-backed property stops being. Neither of these is a computed property the framework
        // watches, so the banner and the disabled surface would keep the old answer until the tab was reopened.
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(IsReadOnly)));
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(ReadOnlyReason)));
    }

    // AllComponents keeps the file's order; Components takes the order the canvas draws in.
    private void Populate(IReadOnlyList<ComponentInstance> fileOrder)
    {
        AllComponents.Clear();
        Components.Clear();
        var viewModels = new Dictionary<ComponentInstance, ComponentInstanceViewModel>(ReferenceEqualityComparer.Instance);
        foreach (var comp in fileOrder)
        {
            var vm = new ComponentInstanceViewModel(this, comp);
            viewModels[comp] = vm;
            // A UserControl's and a PropertyPage's roots are classes of their own (#1585).
            if (comp.BaseClass is FormComponentClass)
                Form = vm;
            AllComponents.Add(vm);
        }

        foreach (var comp in DrawOrder(fileOrder))
            Components.Add(viewModels[comp]);

        // Where each control stands against the carried ones beside it, as the file has them (#1498).
        foreach (var container in fileOrder.Where(c => c.PreservedChildSubtrees.Count > 0))
        {
            var children = WrittenChildren(container, fileOrder);
            var below = CarriedOrder.CarriedBelow(children.Count, container.PreservedChildSubtrees);
            for (var i = 0; i < children.Count; i++)
                if (viewModels.TryGetValue(children[i], out var child))
                    child.CarriedBelow = below[i];
        }
    }

    /* ctr only for the previewer! */
    public FormEditViewModel()
    {
        CanFloat = false;
        AllComponents.CollectionChanged += OnAllComponentsChanged;
        Components.CollectionChanged += OnComponentsChanged;
        Form = new ComponentInstanceViewModel(this, new ComponentInstance(FormComponentClass.Instance, "Form1")
            .SetProperty(VBProperties.WidthProperty, 400)
            .SetProperty(VBProperties.HeightProperty, 300)
            .SetProperty(VBProperties.CaptionProperty, "Form1"));
        AllComponents.Add(Form);
        eventBus = null!;
        projectService = null!;
        editorService = null!;
        windowManager = null!;
        settingsService = null!;
        localization = null!;
        ToolsBoxToolViewModel = null!;
    }

    /// <summary>
    /// A control added by double-clicking its tool: centred on the form as Center in Form centres one, then put on the
    /// nearest grid line when Align to Grid is on, as VB6 puts it (measured, #929: a 1215 x 495 button on a 6000 x 4500
    /// form went to 2400, 2040, where its centre is 2392.5, 2002.5). A position exactly between two lines goes to the
    /// higher: 12.5 grid units became 13 (measured), where the default to-even rounding gives 12. Its size is its class's
    /// <see cref="ComponentBaseClass.AddedSize"/>, the one VB6 gives it (measured, #1473).
    /// </summary>
    public void SpawnControlCenter(ComponentBaseClass componentClass, RuntimeLibrary? library = null)
    {
        if (componentClass is StatusBarComponentClass)
        {
            // VB6's toolbox double-click: full form width, 25 px high, centred on a whole pixel (#1087).
            SpawnControlAt(componentClass, new Rect(0, Math.Floor((Form.Height - 25) / 2), Form.Width, 25), library: library);
            return;
        }
        var (width, height) = componentClass.AddedSize;
        var left = Twips(Form.Width) / 2 - width / 2;
        var top = Twips(Form.Height) / 2 - height / 2;
        if (settingsService.AlignToGrid && componentClass is not ImageListComponentClass)
        {
            left = OnGrid(left, settingsService.GridWidth);
            top = OnGrid(top, settingsService.GridHeight);
        }
        SpawnControlAt(componentClass, new Rect(Pixels(left), Pixels(top), Pixels(width), Pixels(height)), library: library);

        static long OnGrid(long twips, int gridPixels)
        {
            var grid = Twips(gridPixels);
            return grid <= 0 ? twips : (long)Math.Round(twips / (double)grid, MidpointRounding.AwayFromZero) * grid;
        }
    }

    // The Format commands that halve or divide a distance work as VB6's do, in whole twips, halving each size on its own
    // and truncating (measured, #929). In fractional pixels a control centred on an odd difference landed at x.5 px, which
    // a save wrote as a fractional twip VB6 never holds. A designer pixel is 15 twips, so a whole twip is a whole number
    // of fifteenths of a pixel, and the save writes it back exactly.
    private static long Twips(double pixels) => (long)Math.Round(pixels * VBScaleModeExtensions.PixelToTwips);

    private static double Pixels(long twips) => twips / (double)VBScaleModeExtensions.PixelToTwips;

    /// <summary>
    /// A control drawn with the selected tool by a drag from <paramref name="pressedAt"/> to <paramref name="releasedAt"/>,
    /// both in canvas space and not yet snapped. VB6's designer puts it in the innermost container under the press, snaps
    /// its corners on that container's own grid, measured from its client origin, and holds the drag to it on every side
    /// (measured, #2039).
    /// </summary>
    /// <remarks>
    /// Each corner goes to the nearest grid line, and the control ends one pixel past its far corner, grid or no grid
    /// (measured, #2058): a drag from 5, 14 to 50, 30 with the 120-twip grid is Left 120, Top 240, 615 × 255, and the same
    /// drag with the grid off is Left 75, Top 210, 690 × 270. A corner exactly half way goes up, as a control placed by a
    /// double-click does (#1473); that case is unmeasured for a drag.
    /// </remarks>
    public void SpawnControlDrawn(Point pressedAt, Point releasedAt)
    {
        if (ToolsBoxToolViewModel.SelectedComponent is not { BaseClass: { } baseClass } tool)
            return;

        var (rect, container) = DrawnControlRectangle(pressedAt, releasedAt);
        SpawnControl(baseClass, rect, container, tool.Library);
    }

    /// <summary>The canvas rectangle and container shared by the drag preview and placement.</summary>
    internal (Rect Rectangle, ComponentInstanceViewModel? Container) DrawnControlRectangle(Point pressedAt, Point releasedAt)
    {
        var container = ContainerAt(pressedAt);
        var origin = container?.ClientOrigin ?? default;
        var (x1, x2) = (Snapped(pressedAt.X - origin.X, settingsService.GridWidth), Snapped(releasedAt.X - origin.X, settingsService.GridWidth));
        var (y1, y2) = (Snapped(pressedAt.Y - origin.Y, settingsService.GridHeight), Snapped(releasedAt.Y - origin.Y, settingsService.GridHeight));
        var rect = new Rect(origin.X + Math.Min(x1, x2), origin.Y + Math.Min(y1, y2),
            Math.Max(1, Math.Abs(x2 - x1)), Math.Max(1, Math.Abs(y2 - y1)));
        if (container is not null)
            rect = HeldTo(rect, container);
        return (new Rect(rect.X, rect.Y, rect.Width + 1, rect.Height + 1), container);

        double Snapped(double pixels, int grid) =>
            settingsService.AlignToGrid && grid > 0 ? Math.Round(pixels / grid, MidpointRounding.AwayFromZero) * grid : pixels;
    }

    /// <summary>
    /// The container a control drawn from <paramref name="pressedAt"/> goes into: the innermost Frame or PictureBox under
    /// the point, or null for the form. VB6's designer decides by where the drag began, not where it ended (measured, #2039).
    /// </summary>
    /// <remarks>
    /// A container's contents follow it in <see cref="Components"/>, which is the canvas's drawing order, so the last
    /// container under the point is the innermost, and of two overlapping ones the one on top.
    /// </remarks>
    internal ComponentInstanceViewModel? ContainerAt(Point pressedAt)
    {
        for (var i = Components.Count - 1; i >= 0; i--)
        {
            var candidate = Components[i];
            if (candidate != Form && ContainerClasses.IsContainer(candidate.Instance.BaseClass)
                && new Rect(candidate.Left, candidate.Top, candidate.Width, candidate.Height).Contains(pressedAt))
                return candidate;
        }
        return null;
    }

    /// <summary>
    /// The TabIndex a control added or pasted takes: the next after the highest on the form, counting only the classes
    /// that have one, as VB6's designer gives it (measured, #925). So the first is 0, a Timer neither takes one nor uses
    /// one up, and a Label's or a Frame's counts. A paste takes a new one for each control, its container's first. A
    /// control HexIDE does not model counts too, read from the text it is carried as.
    /// </summary>
    private int NextTabIndex() =>
        AllComponents.Where(c => HasTabIndex(c.Instance.BaseClass))
            .Select(c => c.Instance.GetPropertyOrDefault(VBProperties.TabIndexProperty))
            .Concat(AllComponents.SelectMany(c => c.Instance.PreservedChildSubtrees).SelectMany(b => PreservedControlText.TabIndexes(b.Text)))
            .DefaultIfEmpty(-1).Max() + 1;

    private static bool HasTabIndex(IComponentClass type) => type.Properties.Contains(VBProperties.TabIndexProperty);

    /// <param name="pressedAt">
    /// Where a drawn control's drag began, in canvas space. It goes into the innermost container under that point, and its
    /// rectangle is held to that container, as VB6's designer holds the drag to it (measured, #2039). Null places it on
    /// the form, as a double-click on the toolbox does whatever is selected (measured).
    /// </param>
    /// <param name="library">
    /// For an in-box control, the library it is placed from, when the toolbox offers it from an older library than its
    /// class's newest: a ProgressBar from COMCTL32.OCX is <c>ComctlLib.ProgressBar</c> (#1103).
    /// </param>
    public void SpawnControlAt(ComponentBaseClass baseClass, Rect rect, Point? pressedAt = null,
        RuntimeLibrary? library = null)
    {
        var container = pressedAt is { } press ? ContainerAt(press) : null;
        if (container is not null)
            rect = HeldTo(rect, container);
        SpawnControl(baseClass, rect, container, library);
    }

    private void SpawnControl(ComponentBaseClass baseClass, Rect rect, ComponentInstanceViewModel? container,
        RuntimeLibrary? library)
    {
        var newName = FreeName(baseClass);
        Log.Debug("FormEditViewModel: Spawning {ControlType} as {ControlName} at ({Left},{Top} {Width}x{Height})",
            baseClass.Name, newName, rect.Left, rect.Top, rect.Width, rect.Height);
        var instance = new ComponentInstance(baseClass, newName);
        if (container is not null)
            instance.SetContainer(container.Instance);
        if (baseClass is InBoxComponentClass inBox && library is not null && !ReferenceEquals(library, inBox.Library))
            instance.SetProperty(InBoxComponentClass.LoadedTypeNameProperty, library.TypeName(inBox.ControlClassName));
        DeclareLibraryOf(instance);
        if (HasTabIndex(baseClass))
            instance.SetProperty(VBProperties.TabIndexProperty, NextTabIndex());
        // A drawn control starts with its name as its Caption, or as its Text: VB6's designer writes Text = "Text1" for a
        // TextBox and Text = "Combo1" for a ComboBox (measured, #926). Only where the class has the property.
        if (baseClass.Properties.Contains(VBProperties.CaptionProperty))
            instance.SetProperty(VBProperties.CaptionProperty, newName);
        if (baseClass is RichTextBoxComponentClass)
            RichTextBoxPersistence.Seed(instance, newName);
        if (baseClass.Properties.Contains(VBProperties.TextProperty))
            instance.SetProperty(VBProperties.TextProperty, newName);
        var newComponent = new ComponentInstanceViewModel(this, instance)
        {
            Width = rect.Width,
            Height = rect.Height,
            Left = rect.Left,
            Top = rect.Top
        };
        // On top of its plane, so VB6 finds it first among its container's windowed blocks, or first among its windowless
        // ones after them (measured, #1481), carried ones included (measured, #1498).
        var plane = container?.Instance ?? Form.Instance;
        Components.Insert(TopOfPlane(Components, plane, IsWindowless(baseClass)), newComponent);
        newComponent.CarriedBelow = CarriedBelowInPlane(plane, IsWindowless(baseClass), top: true, except: null);
        AllComponents.Add(newComponent);
        UndoStack.Push(new AddControlsCommand(
            [(newComponent, Components.IndexOf(newComponent), AllComponents.IndexOf(newComponent))],
            $"Add: {newComponent.Name}"));

        ToolsBoxToolViewModel.SelectedComponent = ToolsBoxToolViewModel.Arrow;
        SelectedComponent = newComponent;
    }

    /// <summary>
    /// An in-box control's form names its library in its header, as VB6's designer writes it once one is placed (#1103). A
    /// form holding none of a library's controls has no line for it, though its project references it (measured).
    /// </summary>
    private void DeclareLibraryOf(ComponentInstance instance)
    {
        if (formDefinition is null || instance.BaseClass is not InBoxComponentClass inBox)
            return;
        var library = inBox.LibraryOf(instance);
        if (!formDefinition.ObjectReferences.Any(library.IsNamedBy))
            formDefinition.HeaderLines.Add(library.FormLine);
    }

    /// <summary>
    /// A drawn rectangle held to the container it was begun in, as VB6's designer holds the drag (measured, #2039). Measured
    /// from the container's client origin, it starts no further up or left than that origin, and stops at the container's
    /// outer Width and Height, snapped down to the grid: for a bordered PictureBox a little past its client area, which is
    /// drawn clipped.
    /// </summary>
    private Rect HeldTo(Rect rect, ComponentInstanceViewModel container)
    {
        var origin = container.ClientOrigin;
        var right = origin.X + SnappedDown(container.Width, settingsService.GridWidth);
        var bottom = origin.Y + SnappedDown(container.Height, settingsService.GridHeight);
        var left = Math.Min(Math.Max(rect.Left, origin.X), right - 1);
        var top = Math.Min(Math.Max(rect.Top, origin.Y), bottom - 1);
        return new Rect(left, top, Math.Max(1, Math.Min(rect.Right, right) - left), Math.Max(1, Math.Min(rect.Bottom, bottom) - top));

        double SnappedDown(double pixels, int grid) =>
            settingsService.AlignToGrid && grid > 0 ? Math.Floor(pixels / grid) * grid : pixels;
    }

    public void BringToFront()
    {
        if (selectedComponent != null)
            BringToFront(selectedComponent);
    }

    /// <summary>
    /// Re-orders each container's contents to match the canvas order before the model is written: the one drawn on top
    /// first, as VB6 writes them (#1481).
    ///
    /// This handler is the only place the designer authors the model's component list, so whatever the canvas
    /// holds has to become the tree here or a save flattens the form again. Menus are skipped: they share the
    /// canvas collection but their order is their own tree, and reordering a control must not reorder a menu.
    /// </summary>
    private void RebuildContainmentOrder(Dictionary<ComponentInstanceViewModel, int> positionInList)
    {
        var canvasOrder = new Dictionary<ComponentInstance, int>();
        foreach (var (vm, index) in positionInList)
            canvasOrder[vm.Instance] = index;

        foreach (var owner in AllComponents.Select(x => x.Instance))
        {
            if (owner.ContainedControls.Count < 2)
                continue;

            var ordered = owner.ContainedControls
                .OrderByDescending(c => canvasOrder.TryGetValue(c, out var i) ? i : int.MinValue)
                .ToList();

            // Re-inserting in ascending order rebuilds the list: each SetContainer detaches the child and
            // puts it back at the index given, so by the time index i is written every earlier slot is final.
            for (var i = 0; i < ordered.Count; i++)
                ordered[i].SetContainer(owner, i);
        }
    }

    public void BringToFront(ComponentInstanceViewModel instance) => MoveInPlane(instance, toFront: true, "Bring to Front");

    public void SendToBack()
    {
        if (selectedComponent != null)
            SendToBack(selectedComponent);
    }

    public void SendToBack(ComponentInstanceViewModel instance) => MoveInPlane(instance, toFront: false, "Send to Back");

    /// <summary>
    /// Moves a control to the top or the bottom of its plane in its container, with what it holds, as VB6's Bring to Front
    /// and Send to Back do (measured, #1481). Its block goes first or last in its plane's run of the container's blocks: a
    /// windowed control never goes under a Label, a Label never over a windowed control, and nothing leaves its
    /// container. Its TabIndex does not change.
    /// </summary>
    private void MoveInPlane(ComponentInstanceViewModel instance, bool toFront, string verb)
    {
        // A menu has no z-order. It is only in this collection because a loaded menu item becomes a zero-sized item
        // parked at the origin, which is a separate defect.
        if (IsMenu(instance))
            return;
        var from = Components.IndexOf(instance);
        if (from == -1)
            return;

        // What it holds moves with it, or the container would be drawn over its own contents.
        var count = RegionOf(Components, instance.Instance).End - from;
        var before = Components.ToList();
        var rest = before.Take(from).Concat(before.Skip(from + count)).ToList();
        var container = ParentOf(instance.Instance);
        var windowless = IsWindowless(instance.Instance.BaseClass);
        var at = toFront ? TopOfPlane(rest, container, windowless) : BottomOfPlane(rest, container, windowless);
        var carriedBefore = instance.CarriedBelow;
        instance.CarriedBelow = CarriedBelowInPlane(container, windowless, toFront, except: instance);

        if (at > from)
            for (var k = 0; k < count; k++)
                Components.Move(from, at + count - 1);
        else if (at < from)
            for (var k = 0; k < count; k++)
                Components.Move(from + k, at + k);

        SelectedComponent = null; // required, otherwise GUI lose selected item
        SelectedComponent = instance;
        var after = Components.ToList();
        if (!before.SequenceEqual(after) || carriedBefore != instance.CarriedBelow)
            UndoStack.Push(new ZOrderCommand(instance, before, after, carriedBefore, instance.CarriedBelow, $"{verb}: {instance.Name}"));
    }

    public void CenterHorizontally()
    {
        if (selectedComponent == null) return;
        var target = selectedComponent;
        // ScaleWidth\2 - Width\2: 4680 and 1215 give 2340 - 607 = 1733, where the exact answer is 1732.5 (measured).
        PushBatch($"Center H: {target.Name}", [target], [VBProperties.LeftProperty],
            () => target.Left = Pixels(Twips(Form.Width) / 2 - Twips(target.Width) / 2));
    }

    public void CenterVertically()
    {
        if (selectedComponent == null) return;
        var target = selectedComponent;
        // 3195 and 510 give 1597 - 255 = 1342: the exact 1342.5 goes down, where 1732.5 went up across (measured).
        PushBatch($"Center V: {target.Name}", [target], [VBProperties.TopProperty],
            () => target.Top = Pixels(Twips(Form.Height) / 2 - Twips(target.Height) / 2));
    }

    public void AlignLefts()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Lefts: {targets.Count} controls", targets, [VBProperties.LeftProperty],
            () => { foreach (var c in targets) c.Left = primary.Left; });
    }

    public void AlignRights()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Rights: {targets.Count} controls", targets, [VBProperties.LeftProperty],
            () => { foreach (var c in targets) c.Left = primary.Left + primary.Width - c.Width; });
    }

    public void AlignTops()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Tops: {targets.Count} controls", targets, [VBProperties.TopProperty],
            () => { foreach (var c in targets) c.Top = primary.Top; });
    }

    public void AlignBottoms()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Bottoms: {targets.Count} controls", targets, [VBProperties.TopProperty],
            () => { foreach (var c in targets) c.Top = primary.Top + primary.Height - c.Height; });
    }

    public void AlignCentersH()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Centers H: {targets.Count} controls", targets, [VBProperties.LeftProperty],
            () => { foreach (var c in targets) c.Left = primary.Left + Pixels(Twips(primary.Width) / 2 - Twips(c.Width) / 2); });
    }

    public void AlignCentersV()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Centers V: {targets.Count} controls", targets, [VBProperties.TopProperty],
            () => { foreach (var c in targets) c.Top = primary.Top + Pixels(Twips(primary.Height) / 2 - Twips(c.Height) / 2); });
    }

    public void MakeSameWidth()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Same Width: {targets.Count} controls", targets, [VBProperties.WidthProperty],
            () => { foreach (var c in targets) c.Width = primary.Width; });
    }

    public void MakeSameHeight()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Same Height: {targets.Count} controls", targets, [VBProperties.HeightProperty],
            () => { foreach (var c in targets) c.Height = primary.Height; });
    }

    public void MakeSameSize()
    {
        if (!HasMultipleSelectedControls || selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Same Size: {targets.Count} controls", targets,
            [VBProperties.WidthProperty, VBProperties.HeightProperty], () =>
            {
                foreach (var c in targets) c.Width = primary.Width;
                foreach (var c in targets) c.Height = primary.Height;
            });
    }

    public void MakeHorizontalSpacingEqual()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Left).ToList();
        if (sorted.Count < 2) return;
        // The gap is truncated, and the last control stays where it was, its gap taking what is left over: Lefts 300, 2000
        // and 4005, 1200 wide, become 300, 2152 and 4005 (measured).
        PushBatch($"Equal H-Spacing: {sorted.Count} controls", sorted, [VBProperties.LeftProperty], () =>
        {
            long start = Twips(sorted[0].Left);
            long span = Twips(sorted[^1].Left) + Twips(sorted[^1].Width) - start;
            long gap = (span - sorted.Sum(c => Twips(c.Width))) / (sorted.Count - 1);
            long cursor = start;
            foreach (var c in sorted.Take(sorted.Count - 1)) { c.Left = Pixels(cursor); cursor += Twips(c.Width) + gap; }
        });
    }

    public void IncreaseHorizontalSpacing()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Left).ToList();
        if (sorted.Count < 2) return;
        PushBatch($"Increase H-Spacing: {sorted.Count} controls", sorted, [VBProperties.LeftProperty], () =>
        {
            double unit = settingsService.GridWidth;
            for (int i = 1; i < sorted.Count; i++)
                sorted[i].Left += i * unit;
        });
    }

    public void DecreaseHorizontalSpacing()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Left).ToList();
        if (sorted.Count < 2) return;
        PushBatch($"Decrease H-Spacing: {sorted.Count} controls", sorted, [VBProperties.LeftProperty], () =>
        {
            double unit = settingsService.GridWidth;
            // Compute original gaps, decrease each by unit (min 0), then reposition.
            double[] gaps = new double[sorted.Count - 1];
            for (int i = 0; i < gaps.Length; i++)
                gaps[i] = Math.Max(0, sorted[i + 1].Left - (sorted[i].Left + sorted[i].Width) - unit);
            double cursor = sorted[0].Left;
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].Left = cursor;
                if (i < gaps.Length) cursor += sorted[i].Width + gaps[i];
            }
        });
    }

    public void RemoveHorizontalSpacing()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Left).ToList();
        if (sorted.Count < 2) return;
        PushBatch($"Remove H-Spacing: {sorted.Count} controls", sorted, [VBProperties.LeftProperty], () =>
        {
            double cursor = sorted[0].Left;
            foreach (var c in sorted) { c.Left = cursor; cursor += c.Width; }
        });
    }

    public void MakeVerticalSpacingEqual()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Top).ToList();
        if (sorted.Count < 2) return;
        // As across: the gap truncated, the last control left where it was.
        PushBatch($"Equal V-Spacing: {sorted.Count} controls", sorted, [VBProperties.TopProperty], () =>
        {
            long start = Twips(sorted[0].Top);
            long span = Twips(sorted[^1].Top) + Twips(sorted[^1].Height) - start;
            long gap = (span - sorted.Sum(c => Twips(c.Height))) / (sorted.Count - 1);
            long cursor = start;
            foreach (var c in sorted.Take(sorted.Count - 1)) { c.Top = Pixels(cursor); cursor += Twips(c.Height) + gap; }
        });
    }

    public void IncreaseVerticalSpacing()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Top).ToList();
        if (sorted.Count < 2) return;
        PushBatch($"Increase V-Spacing: {sorted.Count} controls", sorted, [VBProperties.TopProperty], () =>
        {
            double unit = settingsService.GridHeight;
            for (int i = 1; i < sorted.Count; i++)
                sorted[i].Top += i * unit;
        });
    }

    public void DecreaseVerticalSpacing()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Top).ToList();
        if (sorted.Count < 2) return;
        PushBatch($"Decrease V-Spacing: {sorted.Count} controls", sorted, [VBProperties.TopProperty], () =>
        {
            double unit = settingsService.GridHeight;
            double[] gaps = new double[sorted.Count - 1];
            for (int i = 0; i < gaps.Length; i++)
                gaps[i] = Math.Max(0, sorted[i + 1].Top - (sorted[i].Top + sorted[i].Height) - unit);
            double cursor = sorted[0].Top;
            for (int i = 0; i < sorted.Count; i++)
            {
                sorted[i].Top = cursor;
                if (i < gaps.Length) cursor += sorted[i].Height + gaps[i];
            }
        });
    }

    public void RemoveVerticalSpacing()
    {
        var sorted = SelectedComponents.OrderBy(c => c.Top).ToList();
        if (sorted.Count < 2) return;
        PushBatch($"Remove V-Spacing: {sorted.Count} controls", sorted, [VBProperties.TopProperty], () =>
        {
            double cursor = sorted[0].Top;
            foreach (var c in sorted) { c.Top = cursor; cursor += c.Height; }
        });
    }

    public void SizeToGrid()
    {
        int gridW = settingsService.GridWidth;
        int gridH = settingsService.GridHeight;
        if (gridW == 0 || gridH == 0) return;
        var targets = SelectedComponents.ToList();
        PushBatch($"Size to Grid: {targets.Count} controls", targets,
            [VBProperties.LeftProperty, VBProperties.TopProperty, VBProperties.WidthProperty, VBProperties.HeightProperty],
            () =>
            {
                foreach (var c in targets)
                {
                    c.Left   = (int)c.Left   / gridW * gridW;
                    c.Top    = (int)c.Top    / gridH * gridH;
                    c.Width  = Math.Max(gridW, (int)c.Width  / gridW * gridW);
                    c.Height = Math.Max(gridH, (int)c.Height / gridH * gridH);
                }
            });
    }

    private void PushBatch(string description, IReadOnlyList<ComponentInstanceViewModel> targets,
        IReadOnlyList<PropertyClass> relevantProperties, Action operation)
    {
        var before = targets.ToDictionary(
            t => t,
            t => relevantProperties.ToDictionary(p => p, p => t.Instance.GetBoxedPropertyOrDefault(p)));
        operation();
        var inner = new List<IDesignerCommand>();
        foreach (var t in targets)
            foreach (var p in relevantProperties)
            {
                var bv = before[t][p];
                var av = t.Instance.GetBoxedPropertyOrDefault(p);
                if (!Equals(bv, av))
                    inner.Add(new Commands.SetPropertyCommand(t.Instance, p, bv, av));
            }
        if (inner.Count > 0)
            UndoStack.Push(new Commands.BatchCommand(description, inner));
    }

    public void ToggleLockControls()
    {
        if (formDefinition == null) return;
        bool before = formDefinition.LockControls;
        formDefinition.LockControls = !formDefinition.LockControls;
        SaveLockControls();
        UndoStack.Push(new LockControlsCommand(before, formDefinition.LockControls));
    }

    public void DeleteSelected()
    {
        // Delete the WHOLE selection (rubber-band / Ctrl-click), not just the primary — matching VB6, which deletes
        // every selected control as one undoable action. Falls back to the primary when nothing multi-selected.
        var targets = SelectedComponents.Count > 0
            ? SelectedComponents
            : (selectedComponent != null ? [selectedComponent] : (IReadOnlyList<ComponentInstanceViewModel>)[]);
        var toDelete = WithContents(targets.Where(c => c != Form));
        if (toDelete.Count == 0) return;

        var entries = toDelete.Select(c => (c, Components.IndexOf(c), AllComponents.IndexOf(c))).ToList();
        foreach (var c in toDelete)
        {
            Components.Remove(c);
            AllComponents.Remove(c);
        }
        SelectedComponent = Form;
        string desc = entries.Count == 1 ? $"Delete: {entries[0].Item1.Name}" : $"Delete: {entries.Count} controls";
        UndoStack.Push(new RemoveControlsCommand(entries, desc));
    }

    /// <summary>
    /// A selection plus everything inside any container in it, in <see cref="AllComponents"/> order.
    ///
    /// Deleting a container has to take its contents with it: leaving them behind would leave controls whose
    /// container is no longer in the form, and the write-back would then try to nest them inside a component
    /// it is not writing. Restoring is symmetric, which is what makes it one undo rather than a dozen.
    /// </summary>
    private List<ComponentInstanceViewModel> WithContents(IEnumerable<ComponentInstanceViewModel> selection)
    {
        var included = new HashSet<ComponentInstanceViewModel>();

        void Add(ComponentInstanceViewModel vm)
        {
            if (!included.Add(vm))
                return;
            foreach (var child in vm.Instance.ContainedControls)
                if (TryGetViewModel(child, out var childVm))
                    Add(childVm);
        }

        foreach (var vm in selection)
            Add(vm);

        // AllComponents order, so the captured indices ascend the way RemoveControlsCommand restores them.
        return AllComponents.Where(included.Contains).ToList();
    }

    /// <summary>
    /// The controls a drag should actually displace: a selected control whose container is also selected is
    /// dropped, because its container is already moving it.
    ///
    /// A marquee across a Frame on this flat canvas selects the Frame AND its children — a selection VB6
    /// never produces, because VB6 scopes the marquee to the container the drag began in. Container-scoped
    /// marquee needs the same container hit-test that interactive re-parenting needs and is deferred with it,
    /// so this rule is the consequence of a deliberate divergence rather than an implementation detail.
    /// Without it a child inside a selected Frame moves twice as far as the Frame.
    /// </summary>
    internal List<ComponentInstanceViewModel> WithoutRedundantDragTargets(IReadOnlyList<ComponentInstanceViewModel> selection)
    {
        var selected = new HashSet<ComponentInstance>(selection.Select(v => v.Instance));
        return selection.Where(vm =>
        {
            for (var container = vm.Instance.Container; container is not null; container = container.Container)
                if (selected.Contains(container))
                    return false;
            return true;
        }).ToList();
    }

    private int _pasteOffset;

    public void CopySelectedControls()
    {
        var targets = SelectedComponents.Count > 0 ? SelectedComponents : (selectedComponent != null ? [selectedComponent] : (IReadOnlyList<ComponentInstanceViewModel>)[]);
        var toCopy = targets.Where(c => c != Form).ToList();
        if (toCopy.Count == 0) return;
        DesignerClipboard.Set(toCopy);
        _pasteOffset = 0;
    }

    public void CutSelectedControls()
    {
        var targets = SelectedComponents.Count > 0 ? SelectedComponents : (selectedComponent != null ? [selectedComponent] : (IReadOnlyList<ComponentInstanceViewModel>)[]);
        var toDelete = WithContents(targets.Where(c => c != Form));
        if (toDelete.Count == 0) return;
        var entries = toDelete.Select(c => (c, Components.IndexOf(c), AllComponents.IndexOf(c))).ToList();
        DesignerClipboard.Set(toDelete);
        _pasteOffset = 0;
        foreach (var c in toDelete)
        {
            Components.Remove(c);
            AllComponents.Remove(c);
        }
        SelectedComponent = Form;
        string desc = entries.Count == 1 ? $"Cut: {entries[0].Item1.Name}" : $"Cut: {entries.Count} controls";
        UndoStack.Push(new RemoveControlsCommand(entries, desc));
    }

    public void PasteControls()
    {
        if (DesignerClipboard.Contents is not { Count: > 0 } entries) return;
        _pasteOffset++;
        double offsetX = _pasteOffset * settingsService.GridWidth;
        double offsetY = _pasteOffset * settingsService.GridHeight;

        var pasted = new List<ComponentInstanceViewModel>();
        var contentsOf = new Dictionary<ComponentInstanceViewModel, List<ComponentInstanceViewModel>>();
        ComponentInstanceViewModel? lastAdded = null;

        // A control HexIDE does not model is carried by its class name alone, so it goes only into a form declaring
        // every library the form it came from did; anywhere else VB6 could not load it. A pasted one is numbered after
        // every block already on the form.
        var declared = new HashSet<string>(
            DesignerClipboard.Declarations(formDefinition?.HeaderLines ?? []), StringComparer.OrdinalIgnoreCase);
        var carriesControls = DesignerClipboard.SourceDeclarations.All(declared.Contains);
        var documentOrder = AllComponents.SelectMany(c => c.Instance.PreservedChildSubtrees)
            .Select(block => block.DocumentOrder).DefaultIfEmpty(-1).Max() + 1;

        ComponentInstanceViewModel Build(DesignerClipboard.ClipboardEntry entry, ComponentInstance? container, double shiftX,
            double shiftY)
        {
            var newName = FreeName(entry.BaseClass);
            var newInstance = new ComponentInstance(entry.BaseClass, newName);
            foreach (var kvp in entry.Properties)
            {
                if (ReferenceEquals(kvp.Key, VBProperties.NameProperty)) continue;
                if (ReferenceEquals(kvp.Key, VBProperties.LeftProperty))
                    newInstance.SetUntypedProperty(kvp.Key, (kvp.Value is double l ? l : 0.0) + shiftX);
                else if (ReferenceEquals(kvp.Key, VBProperties.TopProperty))
                    newInstance.SetUntypedProperty(kvp.Key, (kvp.Value is double t ? t : 0.0) + shiftY);
                else
                    newInstance.SetUntypedProperty(kvp.Key, kvp.Value);
            }
            // Its own place in the tab order, not its source's, which would put two controls at one TabIndex (#925).
            if (HasTabIndex(entry.BaseClass))
                newInstance.SetProperty(VBProperties.TabIndexProperty, NextTabIndex());
            if (container is not null)
                newInstance.SetContainer(container);

            // What HexIDE carries without modelling goes with the control, or a cut and paste loses it (#1453). Its
            // properties have no name to repeat, so a copy takes them too, as VB6 copies every property. A control
            // inside it goes only where its names are free, which a cut leaves them: a copy's would be the original's
            // again, and vb6.exe builds a form holding a name twice without either control (#854).
            newInstance.UnknownRawProperties.AddRange(entry.UnknownProperties);
            if (carriesControls)
            {
                var inUse = ComponentNaming.NamesInUse(AllComponents.Select(c => c.Instance));
                inUse.Add(newName);
                foreach (var block in entry.PreservedChildren)
                {
                    var names = ComponentNaming.NamesDeclaredBy(block.Text).ToList();
                    if (names.Any(inUse.Contains))
                        continue;
                    newInstance.AddPreservedChildSubtree(block.Ordinal, documentOrder++, block.Text, block.Records);
                    inUse.UnionWith(names);
                }
            }

            var newVm = new ComponentInstanceViewModel(this, newInstance);
            Components.Add(newVm);
            AllComponents.Add(newVm);
            pasted.Add(newVm);
            lastAdded = newVm;

            // Contents keep their own container-relative coordinates and are re-linked into the copy, so a
            // pasted container arrives with its own children rather than sharing the original's.
            contentsOf[newVm] = entry.Children.Select(child => Build(child, newInstance, 0, 0)).ToList();

            // Its contents stand against the carried controls it took as its source's did.
            var below = CarriedOrder.CarriedBelow(contentsOf[newVm].Count, newInstance.PreservedChildSubtrees);
            for (var i = 0; i < below.Length; i++)
                contentsOf[newVm][i].CarriedBelow = below[i];
            return newVm;
        }

        // The copy is drawn as its source was, its contents stacked as theirs were, where VB6 pastes them one at a time,
        // each on top, and so stacks a Frame's copy the other way up (measured, #1481). Names and TabIndexes still follow
        // the source's order, as VB6's do. The copy goes on top of its plane in the container it lands in, as a control
        // added there.
        void Restack(ComponentInstanceViewModel top, ComponentInstance container)
        {
            var stack = new List<ComponentInstanceViewModel>();
            void Add(ComponentInstanceViewModel vm)
            {
                stack.Add(vm);
                var contents = contentsOf[vm];
                for (var i = contents.Count - 1; i >= 0; i--)
                    Add(contents[i]);
            }
            Add(top);
            foreach (var vm in stack)
                Components.Remove(vm);
            var at = TopOfPlane(Components, container, IsWindowless(top.Instance.BaseClass));
            for (var i = 0; i < stack.Count; i++)
                Components.Insert(at + i, stack[i]);
            top.CarriedBelow = CarriedBelowInPlane(container, IsWindowless(top.Instance.BaseClass), top: true, except: top);
        }

        foreach (var entry in entries)
        {
            // Paste back into the container it came from where that container is still on this form. Where it
            // is not — a different form, or the container has since been deleted — the copy lands at form
            // level, and the recorded Left/Top were relative to a container that is not here, so they are
            // rebased to form coordinates. Without that the copy appears wherever the old container's offset
            // happened to put it.
            var target = entry.SourceContainer is { } source && AllComponents.Any(c => ReferenceEquals(c.Instance, source))
                ? source
                : null;
            var rebase = target is null ? entry.SourceContainerOrigin : default;
            Restack(Build(entry, target, offsetX + rebase.X, offsetY + rebase.Y), target ?? Form.Instance);
        }

        if (lastAdded != null)
            SelectedComponent = lastAdded;

        var pastedEntries = pasted.Select(vm => (Vm: vm, ComponentsIdx: Components.IndexOf(vm), AllComponentsIdx: AllComponents.IndexOf(vm))).ToList();
        if (pastedEntries.Count > 0)
        {
            string desc = pastedEntries.Count == 1 ? $"Paste: {pastedEntries[0].Vm.Name}" : $"Paste: {pastedEntries.Count} controls";
            UndoStack.Push(new AddControlsCommand(pastedEntries, desc));
        }
    }

    /// <summary>
    /// Record a property just set on <paramref name="target"/> as one undoable change, with what VB6's designer changes
    /// along with it, which this applies (<see cref="ComponentBaseClass.ApplyDesignerConsequences"/>). Returns those.
    /// </summary>
    public IReadOnlyList<PropertyChange> PushSetPropertyCommand(ComponentInstance target, PropertyClass property, object? before, object? after)
    {
        if (Equals(before, after))
            return [];
        var command = new Commands.SetPropertyCommand(target, property, before, after);
        var consequences = target.BaseClass.ApplyDesignerConsequences(target, property);
        if (consequences.Count == 0)
        {
            UndoStack.Push(command);
            return consequences;
        }
        var inner = new List<IDesignerCommand> { command };
        foreach (var change in consequences)
            inner.Add(new Commands.SetPropertyCommand(target, change.Property, change.Before, change.After));
        UndoStack.Push(new Commands.BatchCommand(command.Description, inner));
        return consequences;
    }

    /// <summary>
    /// The name a control added or pasted takes: its class's prefix and the lowest number from 1 that no name on the form
    /// has, a preserved control's included, in any case (#854). VB6's designer names a new control and a pasted one so,
    /// filling a gap a file left or a delete made (measured, #540): beside Command1 and Command3 the next is Command2, and
    /// beside Text5 it is Text1.
    /// </summary>
    private string FreeName(IComponentClass baseClass)
    {
        var used = ComponentNaming.NamesInUse(AllComponents.Select(c => c.Instance));
        var index = 1;
        while (used.Contains(baseClass.Name + index)) index++;
        return baseClass.Name + index;
    }

    public void BeginDrag(IReadOnlyList<ComponentInstanceViewModel> targets)
    {
        IsDragging = true;
        // A control whose container is also being dragged is moved by that container, not in its own right —
        // recording it here would push a MoveResizeCommand whose before/after are both correct and whose undo
        // would then fight the container's.
        _dragStartRects = WithoutRedundantDragTargets(targets).ToDictionary(
            t => t,
            t => new Rect(t.Left, t.Top, t.Width, t.Height));
    }

    public void EndDrag()
    {
        IsDragging = false;
        if (_dragStartRects is null) return;
        var entries = new List<(ComponentInstanceViewModel, Rect, Rect)>();
        foreach (var (vm, before) in _dragStartRects)
        {
            var after = new Rect(vm.Left, vm.Top, vm.Width, vm.Height);
            if (after != before)
                entries.Add((vm, before, after));
        }
        _dragStartRects = null;
        if (entries.Count > 0)
            UndoStack.Push(new Commands.MoveResizeCommand(entries));
    }

    public void CancelDrag()
    {
        IsDragging = false;
        _dragStartRects = null;
    }

    public void SaveForm() => projectService.SaveForm(formDefinition!, false).ListenErrors();

    /// <summary>
    /// Writes a Lock Controls change, but only when the designer's own form definition has a file.
    /// </summary>
    /// <remarks>
    /// A form with no file saves through the Save As picker, so toggling Lock Controls, or undoing or redoing
    /// the toggle, put a native Save As dialog in front of someone who had asked for nothing of the kind. An
    /// automation client cannot see or close that dialog at all (#539 follow-up). Otherwise the change waits
    /// for the next save, and a form with no file is unsaved whatever it holds.
    ///
    /// <para>
    /// A UserControl's or PropertyPage's designer half has no path even when its .ctl/.pag exists (#474), so
    /// for those this never auto-saves either. That is deliberate until #474 is fixed: the path it used to take
    /// wrote a stray .frm.
    /// </para>
    /// </remarks>
    public void SaveLockControls()
    {
        if (formDefinition?.AbsolutePath is not null)
            SaveForm();
    }

    public void SaveFormAs() => projectService.SaveForm(formDefinition!, true).ListenErrors();

    public async Task PreviewForm()
    {
        if (formDefinition is null || !HasWindowFrame)
            return;

        VBFormRuntime? preview = null;
        try
        {
            preview = FormComponentClass.Instance.InstantiateWindow(Form.Instance, VBLoader.HasMenuBar(formDefinition));
            // No SetCode, PrepareForm or Start: events have no interpreter to dispatch to (#2003).
            preview.Content = VBLoader.SpawnComponents(formDefinition, preview.Context.ExecutionContext,
                preview.Context.RootEnv, new StandaloneStandardLib(preview));
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime { MainWindow: { } owner })
                preview.Show(owner);
            else
                preview.Show();
        }
        catch (NotImplementedException e)
        {
            preview?.Close();
            await windowManager.MessageBox(e.Message, localization.GetString("Str.Designer.PreviewForm"),
                MessageBoxButtons.Ok, MessageBoxIcon.Error);
        }
    }

    public void ViewCode() => editorService.EditCode(formDefinition);

    public void ViewObject() => editorService.EditForm(formDefinition);

    public async Task EditMenu()
    {
        var vm = new MenuEditorViewModel(this);
        if (!await windowManager.ShowDialog(vm))
            return;

        foreach (var deleted in vm.Deleted)
        {
            if (deleted.Menu != null)
                AllComponents.Remove(AllComponents.First(c => c.Instance == deleted.Menu));
        }

        List<ComponentInstanceViewModel> topLevel = new();
        Stack<(int level, ComponentInstance component)> stack = new();
        foreach (var menuViewModel in vm.FlatMenu)
        {
            if (menuViewModel.Menu != null)
            {
                if (menuViewModel.Menu.TryGetProperty(MenuComponentClass.SubItemsProperty, out var subItems) &&
                    subItems != null)
                    subItems.Clear();
            }
        }
        foreach (var menuViewModel in vm.FlatMenu)
        {
            var menu = menuViewModel.Menu;
            if (menu == null)
                menu = new ComponentInstance(MenuComponentClass.Instance, menuViewModel.Name);
            menuViewModel.Apply(menu);
            if (menuViewModel.Menu == null)
                AllComponents.Add(new ComponentInstanceViewModel(this, menu));

            while (stack.Count > 0 && stack.Peek().level >= menuViewModel.Indent)
                stack.Pop();

            var parent = stack.Count > 0 ? stack.Peek().component : null;
            if (parent == null)
                topLevel.Add(AllComponents.First(c => c.Instance == menu));
            else
            {
                var elements = parent.GetPropertyOrDefault(MenuComponentClass.SubItemsProperty) ?? new();
                elements.Add(menu);
                parent.SetProperty(MenuComponentClass.SubItemsProperty, elements);
            }

            stack.Push((menuViewModel.Indent, menu));
        }

        while (TopLevelMenu.Count > 0)
            TopLevelMenu.RemoveAt(TopLevelMenu.Count - 1);
        foreach (var x in topLevel)
            TopLevelMenu.Add(x);

        // The first of the three paths that bypass the undo stack: the menu editor adds, removes and
        // re-parents components and pushes nothing, so nothing else would announce it. In practice the
        // refresh usually stops at the fidelity gate — HexIDE flattens nested Begin blocks, so a form with
        // a real menu is one it refuses to save — and that is the correct outcome rather than a missing
        // one: the buffer keeps showing the file, which is what the save refusal protects.
        CommitLayout();
    }

    public void RequestCode(string? subName)
    {
        Log.Debug("FormEditViewModel: RequestCode({SubName}) for form {FormName}", subName ?? "(null)", formDefinition?.Name);
        editorService.EditCode(formDefinition);
        if (subName == null)
            return;

        // this is a hack, the following line can be executed only after the window is created, it should be solved in a better way.
        DispatcherTimer.RunOnce(() => LoggedTimerCallback.Run("designer code navigation", () =>
        {
            Log.Debug("FormEditViewModel: Dispatching CreateOrNavigateToSubEvent({SubName})", subName);
            eventBus.Publish(new CreateOrNavigateToSubEvent(formDefinition!, subName));
        }), TimeSpan.FromMilliseconds(16));
    }
}