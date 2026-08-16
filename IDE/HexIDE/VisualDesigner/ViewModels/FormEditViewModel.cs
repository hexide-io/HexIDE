using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.Components;
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
            _ => localization.GetString("Str.Document.FormSuffix")
        };
        return $"{formDefinition?.Owner.Name} - {formDefinition?.Name} ({kind})";
    }

    public override object? Icon { get; } = HexIDE.Utils.IconFactory.Themed("Geo.Form");

    [Notify]
    private ComponentInstanceViewModel? selectedComponent;

    public IReadOnlyList<ComponentInstanceViewModel> SelectedComponents { get; private set; } = [];

    public void SetSelectedComponents(IEnumerable<ComponentInstanceViewModel> components) =>
        SelectedComponents = [.. components];

    public ObservableCollection<ComponentInstanceViewModel> AllComponents { get; } = new();

    public ObservableCollection<ComponentInstanceViewModel> Components { get; } = new();

    public ComponentInstanceViewModel Form { get; private set; }

    public ObservableCollection<ComponentInstanceViewModel> TopLevelMenu { get; } = new();

    public IEventBus EventBus => eventBus;

    public FormDefinition? FormDefinition => formDefinition;

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
        };
        AutoDispose(this.eventBus.Subscribe<ApplyAllUnsavedChangesEvent>(e =>
        {
            var positionInList = Components.Select((comp, index) => (comp, index)).ToDictionary(x => x.comp, x => x.index);
            var orderedComponents = new List<ComponentInstance>();
            foreach (var component in AllComponents.OrderBy(x => positionInList.GetValueOrDefault(x, 0)))
            {
                orderedComponents.Add(component.Instance);
            }
            formDefinition?.UpdateComponents(orderedComponents);
        }));
        AutoDispose(this.eventBus.Subscribe<FormUnloadedEvent>(e =>
        {
            if (e.Form == formDefinition)
                RequestClose();
        }));
        AutoDispose(this.eventBus.Subscribe<ProjectUnloadedEvent>(_ => { DesignerClipboard.Clear(); UndoStack.Clear(); }));
    }

    public FormEditViewModel Initialize(FormDefinition formElement)
    {
        formDefinition = formElement;
        AutoDispose(formElement.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        AutoDispose(formElement.Owner.ObservePropertyChanged(x => x.Name)
            .Subscribe(_ => Title = ComputeTitle()));
        AllComponents.Clear();
        Components.Clear();
        foreach (var comp in formElement.Components)
        {
            var vm = new ComponentInstanceViewModel(this, comp);
            if (comp.BaseClass != FormComponentClass.Instance)
                Components.Add(vm);
            else
                Form = vm;
            AllComponents.Add(vm);
        }

        SelectedComponent = Form;
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
        AllComponents.Clear();
        Components.Clear();
        foreach (var comp in formDefinition.Components)
        {
            var vm = new ComponentInstanceViewModel(this, comp);
            if (comp.BaseClass != FormComponentClass.Instance)
                Components.Add(vm);
            else
                Form = vm;
            AllComponents.Add(vm);
        }

        SelectedComponent = Form;
        UndoStack.Clear();
    }

    /* ctr only for the previewer! */
    public FormEditViewModel()
    {
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

    public void SpawnControlCenter(ComponentBaseClass componentClass)
    {
        SpawnControlAt(componentClass, new Rect(0, 0, Form.Width, Form.Height).CenterRect(new Rect(0, 0, 50, 50)));
    }

    public void SpawnControl(Rect rect)
    {
        if (ToolsBoxToolViewModel.SelectedComponent?.BaseClass is not { } baseClass)
            return;

        SpawnControlAt(baseClass, rect);
    }

    public void SpawnControlAt(ComponentBaseClass baseClass, Rect rect)
    {
        // Name = base + the LOWEST unused index, so a name freed by a delete is reused and we never collide with an
        // existing control. The old `baseClass.Name + Components.Count` collided after a delete: e.g. add Command0 +
        // Command1, delete Command0 → Count is 1 again → a second control also named "Command1".
        var used = new HashSet<string>(AllComponents.Select(c => c.Name), StringComparer.OrdinalIgnoreCase);
        int index = 0;
        while (used.Contains(baseClass.Name + index)) index++;
        var newName = baseClass.Name + index;
        Log.Debug("FormEditViewModel: Spawning {ControlType} as {ControlName} at ({Left},{Top} {Width}x{Height})",
            baseClass.Name, newName, rect.Left, rect.Top, rect.Width, rect.Height);
        var newComponent = new ComponentInstanceViewModel(this, new ComponentInstance(baseClass, newName)
            .SetProperty(VBProperties.CaptionProperty, newName)
            .SetProperty(VBProperties.TabIndexProperty, AllComponents.Select(x => x.Instance.GetPropertyOrDefault(VBProperties.TabIndexProperty)).DefaultIfEmpty().Max() + 1)
        )
        {
            Width = rect.Width,
            Height = rect.Height,
            Left = rect.Left,
            Top = rect.Top
        };
        Components.Add(newComponent);
        AllComponents.Add(newComponent);
        UndoStack.Push(new AddControlsCommand(
            [(newComponent, Components.IndexOf(newComponent), AllComponents.IndexOf(newComponent))],
            $"Add: {newComponent.Name}"));

        ToolsBoxToolViewModel.SelectedComponent = ToolsBoxToolViewModel.Arrow;
        SelectedComponent = newComponent;
    }

    public void BringToFront()
    {
        if (selectedComponent != null)
            BringToFront(selectedComponent);
    }

    public void BringToFront(ComponentInstanceViewModel instance)
    {
        var indexOf = Components.IndexOf(instance);
        if (indexOf != -1)
        {
            var newIndex = Components.Count - 1;
            Components.Move(indexOf, newIndex);
            SelectedComponent = null; // required, otherwise GUI lose selected item
            SelectedComponent = instance;
            if (indexOf != newIndex)
                UndoStack.Push(new ZOrderCommand(instance, indexOf, newIndex, $"Bring to Front: {instance.Name}"));
        }
    }

    public void SendToBack()
    {
        if (selectedComponent != null)
            SendToBack(selectedComponent);
    }

    public void SendToBack(ComponentInstanceViewModel instance)
    {
        var indexOf = Components.IndexOf(instance);
        if (indexOf != -1)
        {
            Components.Move(indexOf, 0);
            SelectedComponent = null; // required, otherwise GUI lose selected item
            SelectedComponent = instance;
            if (indexOf != 0)
                UndoStack.Push(new ZOrderCommand(instance, indexOf, 0, $"Send to Back: {instance.Name}"));
        }
    }

    public void CenterHorizontally()
    {
        if (selectedComponent == null) return;
        var target = selectedComponent;
        PushBatch($"Center H: {target.Name}", [target], [VBProperties.LeftProperty],
            () => target.Left = Form.Width / 2 - target.Width / 2);
    }

    public void CenterVertically()
    {
        if (selectedComponent == null) return;
        var target = selectedComponent;
        PushBatch($"Center V: {target.Name}", [target], [VBProperties.TopProperty],
            () => target.Top = Form.Height / 2 - target.Height / 2);
    }

    public void AlignLefts()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Lefts: {targets.Count} controls", targets, [VBProperties.LeftProperty],
            () => { foreach (var c in targets) c.Left = primary.Left; });
    }

    public void AlignRights()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Rights: {targets.Count} controls", targets, [VBProperties.LeftProperty],
            () => { foreach (var c in targets) c.Left = primary.Left + primary.Width - c.Width; });
    }

    public void AlignTops()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Tops: {targets.Count} controls", targets, [VBProperties.TopProperty],
            () => { foreach (var c in targets) c.Top = primary.Top; });
    }

    public void AlignBottoms()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Bottoms: {targets.Count} controls", targets, [VBProperties.TopProperty],
            () => { foreach (var c in targets) c.Top = primary.Top + primary.Height - c.Height; });
    }

    public void AlignCentersH()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Centers H: {targets.Count} controls", targets, [VBProperties.LeftProperty],
            () => { foreach (var c in targets) c.Left = primary.Left + (primary.Width - c.Width) / 2; });
    }

    public void AlignCentersV()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Align Centers V: {targets.Count} controls", targets, [VBProperties.TopProperty],
            () => { foreach (var c in targets) c.Top = primary.Top + (primary.Height - c.Height) / 2; });
    }

    public void MakeSameWidth()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Same Width: {targets.Count} controls", targets, [VBProperties.WidthProperty],
            () => { foreach (var c in targets) c.Width = primary.Width; });
    }

    public void MakeSameHeight()
    {
        if (selectedComponent == null) return;
        var primary = selectedComponent;
        var targets = SelectedComponents.ToList();
        PushBatch($"Same Height: {targets.Count} controls", targets, [VBProperties.HeightProperty],
            () => { foreach (var c in targets) c.Height = primary.Height; });
    }

    public void MakeSameSize()
    {
        if (selectedComponent == null) return;
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
        PushBatch($"Equal H-Spacing: {sorted.Count} controls", sorted, [VBProperties.LeftProperty], () =>
        {
            double totalSpan = sorted[^1].Left + sorted[^1].Width - sorted[0].Left;
            double totalWidth = sorted.Sum(c => c.Width);
            double gap = (totalSpan - totalWidth) / (sorted.Count - 1);
            double cursor = sorted[0].Left;
            foreach (var c in sorted) { c.Left = cursor; cursor += c.Width + gap; }
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
        PushBatch($"Equal V-Spacing: {sorted.Count} controls", sorted, [VBProperties.TopProperty], () =>
        {
            double totalSpan = sorted[^1].Top + sorted[^1].Height - sorted[0].Top;
            double totalHeight = sorted.Sum(c => c.Height);
            double gap = (totalSpan - totalHeight) / (sorted.Count - 1);
            double cursor = sorted[0].Top;
            foreach (var c in sorted) { c.Top = cursor; cursor += c.Height + gap; }
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
        projectService.SaveForm(formDefinition, false).ListenErrors();
        UndoStack.Push(new LockControlsCommand(before, formDefinition.LockControls));
    }

    public void DeleteSelected()
    {
        // Delete the WHOLE selection (rubber-band / Ctrl-click), not just the primary — matching VB6, which deletes
        // every selected control as one undoable action. Falls back to the primary when nothing multi-selected.
        var targets = SelectedComponents.Count > 0
            ? SelectedComponents
            : (selectedComponent != null ? [selectedComponent] : (IReadOnlyList<ComponentInstanceViewModel>)[]);
        var toDelete = targets.Where(c => c != Form).ToList();
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
        var toDelete = targets.Where(c => c != Form).ToList();
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

        var pastedEntries = new List<(ComponentInstanceViewModel Vm, int ComponentsIdx, int AllComponentsIdx)>();
        ComponentInstanceViewModel? lastAdded = null;
        foreach (var entry in entries)
        {
            var newName = GenerateName(entry.BaseClass);
            var newInstance = new ComponentInstance(entry.BaseClass, newName);
            foreach (var kvp in entry.Properties)
            {
                if (ReferenceEquals(kvp.Key, VBProperties.NameProperty)) continue;
                if (ReferenceEquals(kvp.Key, VBProperties.LeftProperty))
                    newInstance.SetUntypedProperty(kvp.Key, (kvp.Value is double l ? l : 0.0) + offsetX);
                else if (ReferenceEquals(kvp.Key, VBProperties.TopProperty))
                    newInstance.SetUntypedProperty(kvp.Key, (kvp.Value is double t ? t : 0.0) + offsetY);
                else
                    newInstance.SetUntypedProperty(kvp.Key, kvp.Value);
            }
            var newVm = new ComponentInstanceViewModel(this, newInstance);
            Components.Add(newVm);
            AllComponents.Add(newVm);
            pastedEntries.Add((newVm, Components.IndexOf(newVm), AllComponents.IndexOf(newVm)));
            lastAdded = newVm;
        }

        if (lastAdded != null)
            SelectedComponent = lastAdded;

        if (pastedEntries.Count > 0)
        {
            string desc = pastedEntries.Count == 1 ? $"Paste: {pastedEntries[0].Vm.Name}" : $"Paste: {pastedEntries.Count} controls";
            UndoStack.Push(new AddControlsCommand(pastedEntries, desc));
        }
    }

    public void PushSetPropertyCommand(ComponentInstance target, PropertyClass property, object? before, object? after)
    {
        if (!Equals(before, after))
            UndoStack.Push(new Commands.SetPropertyCommand(target, property, before, after));
    }

    private string GenerateName(IComponentClass baseClass)
    {
        var existingNames = new HashSet<string>(AllComponents.Select(c => c.Name));
        int n = Components.Count;
        string candidate;
        do { candidate = baseClass.Name + n++; }
        while (existingNames.Contains(candidate));
        return candidate;
    }

    public void BeginDrag(IReadOnlyList<ComponentInstanceViewModel> targets)
    {
        IsDragging = true;
        _dragStartRects = targets.ToDictionary(
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

    public void SaveFormAs() => projectService.SaveForm(formDefinition!, true).ListenErrors();

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
    }

    public void RequestCode(string? subName)
    {
        Log.Debug("FormEditViewModel: RequestCode({SubName}) for form {FormName}", subName ?? "(null)", formDefinition?.Name);
        editorService.EditCode(formDefinition);
        if (subName == null)
            return;

        // this is a hack, the following line can be executed only after the window is created, it should be solved in a better way.
        DispatcherTimer.RunOnce(() =>
        {
            Log.Debug("FormEditViewModel: Dispatching CreateOrNavigateToSubEvent({SubName})", subName);
            eventBus.Publish(new CreateOrNavigateToSubEvent(formDefinition!, subName));
        }, TimeSpan.FromMilliseconds(16));
    }
}