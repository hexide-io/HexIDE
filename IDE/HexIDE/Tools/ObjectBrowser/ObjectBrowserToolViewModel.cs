using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Lsp;
using HexIDE.Runtime.Components;
using HexIDE.Lsp.Messages;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Runtime.TypeLibrary;
using HexIDE.Utils;
using Dock.Model.Mvvm.Controls;
using PropertyChanged.SourceGenerator;
using R3;
using Serilog;

namespace HexIDE.Tools.ObjectBrowser;

public partial class ObjectBrowserToolViewModel : Document
{
    private readonly IProjectManager projectManager;
    private readonly ILspClient lspClient;
    private readonly IEditorService editorService;
    private readonly ITypeLibraryService typeLibraryService;
    private readonly IFocusedProjectUtil focusedProjectUtil;

    private readonly Dictionary<ProjectDefinition, OBLibraryViewModel> projectToLibrary = new();
    private OBLibraryViewModel? vbaLibrary;

    public ObservableCollection<OBLibraryViewModel> Libraries { get; } = new();

    [Notify] [AlsoNotify(nameof(MembersHeader))]
    private OBClassViewModel? selectedClass;

    [Notify] [AlsoNotify(nameof(DescriptionSignature), nameof(DescriptionMembership), nameof(DescriptionText))]
    private OBMemberViewModel? selectedMember;

    [Notify] private OBLibraryViewModel? selectedLibrary;
    [Notify] private string searchText = string.Empty;
    [Notify] private bool isLoadingMembers;

    public ObservableCollection<OBClassViewModel> FilteredClasses { get; } = new();
    public ObservableCollection<OBMemberViewModel> FilteredMembers { get; } = new();

    public string MembersHeader => selectedClass != null ? $"Members of '{selectedClass.Name}'" : "Members";

    public string DescriptionSignature => selectedMember != null
        ? $"{selectedMember.KindGlyph} {selectedMember.Signature}"
        : string.Empty;

    public string DescriptionMembership => selectedMember != null && selectedClass != null
        ? $"Member of {selectedClass.LibraryName}.{selectedClass.Name}"
        : string.Empty;

    public string DescriptionText => selectedMember?.Description ?? string.Empty;

    public DelegateCommand SearchCommand { get; }
    public DelegateCommand ClearSearchCommand { get; }
    public DelegateCommand GoToDefinitionCommand { get; }
    public DelegateCommand BackCommand { get; }
    public DelegateCommand ForwardCommand { get; }
    public DelegateCommand CloseCommand { get; }

    private record NavEntry(OBLibraryViewModel? Library, OBClassViewModel? Class, OBMemberViewModel? Member);
    private readonly List<NavEntry> _navHistory = [];
    private int _navIndex = -1;
    private bool _isNavigatingHistory;

    public ObjectBrowserToolViewModel(IProjectManager projectManager, ILspClient lspClient,
        IEditorService editorService, IComponentRegistry componentRegistry,
        ITypeLibraryService typeLibraryService, IFocusedProjectUtil focusedProjectUtil,
        ILocalizationService localization)
    {
        localization.BindTitle(this, "Str.Tool.ObjectBrowser.Title");
        CanClose = true;
        CanFloat = false;

        this.projectManager = projectManager;
        this.lspClient = lspClient;
        this.editorService = editorService;
        this.typeLibraryService = typeLibraryService;
        this.focusedProjectUtil = focusedProjectUtil;

        SearchCommand = new DelegateCommand(RebuildFilteredClasses);
        ClearSearchCommand = new DelegateCommand(() => { SearchText = string.Empty; RebuildFilteredClasses(); });
        GoToDefinitionCommand = new DelegateCommand(GoToDefinition, () => SelectedClass?.CanNavigate ?? false);
        BackCommand = new DelegateCommand(GoBack, () => _navIndex > 0);
        ForwardCommand = new DelegateCommand(GoForward, () => _navIndex < _navHistory.Count - 1);
        CloseCommand = new DelegateCommand(Close);

        projectManager.ProjectLoaded += OnProjectLoaded;
        projectManager.ProjectUnloaded += OnProjectUnloaded;
        focusedProjectUtil.ObservePropertyChanged(x => x.FocusedOrStartupProject)
            .Subscribe(_ => ReorderLibraries());

        this.ObservePropertyChanged(x => x.SelectedLibrary).Subscribe(_ =>
        {
            if (SelectedLibrary is { IsLoaded: false })
            {
                if (SelectedLibrary == vbaLibrary)
                    LoadVbaLibraryAsync(SelectedLibrary).ListenErrors();
                else
                    LoadReferenceLibraryAsync(SelectedLibrary).ListenErrors();
            }
            else if (SelectedLibrary == OBLibraryViewModel.AllLibraries
                     && vbaLibrary is { IsLoaded: false })
            {
                // Load VBA eagerly when "All Libraries" is selected so built-ins appear.
                LoadVbaLibraryAsync(vbaLibrary).ListenErrors();
            }
            RebuildFilteredClasses();
        });
        this.ObservePropertyChanged(x => x.SelectedClass).Subscribe(_ => OnClassSelectionChanged());
        this.ObservePropertyChanged(x => x.SelectedMember).Subscribe(_ => UpdateCurrentHistoryMember());

        Libraries.Add(OBLibraryViewModel.AllLibraries);

        foreach (var project in projectManager.LoadedProjects)
            AddProjectLibrary(project);

        AddVbLibrary(componentRegistry);
        AddVbaLibraryStub();

        SelectedLibrary = OBLibraryViewModel.AllLibraries;
    }

    private void AddVbLibrary(IComponentRegistry componentRegistry)
    {
        var lib = new OBLibraryViewModel("VB");
        foreach (var component in componentRegistry.Components)
        {
            var cls = new OBClassViewModel(component.Name, OBClassKind.ClassModule, "VB");
            foreach (var prop in component.Properties)
                cls.Members.Add(new OBMemberViewModel(prop.Name, OBMemberKind.Property, $"Property {prop.Name}"));
            foreach (var evt in component.Events)
                cls.Members.Add(new OBMemberViewModel(evt.Name, OBMemberKind.Event, $"Event {evt.Name}"));
            lib.Classes.Add(cls);
        }
        lib.Classes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        Libraries.Add(lib);
        ReorderLibraries();
    }

    private void AddVbaLibraryStub()
    {
        vbaLibrary = new OBLibraryViewModel("VBA");
        Libraries.Add(vbaLibrary);
        ReorderLibraries();
    }

    private async Task LoadVbaLibraryAsync(OBLibraryViewModel lib)
    {
        if (lib.IsLoaded) return;
        IsLoadingMembers = true;
        try
        {
            var symbols = await lspClient.RequestBuiltinSymbolsAsync();
            lib.IsLoaded = true;

            if (symbols.Length == 0)
            {
                lib.Classes.Add(new OBClassViewModel("(Built-in symbols unavailable)", OBClassKind.Module, "VBA"));
            }
            else
            {
                var globalsClass = new OBClassViewModel("(Globals)", OBClassKind.Module, "VBA");
                foreach (var sym in symbols)
                    globalsClass.Members.Add(new OBMemberViewModel(sym.Name, OBMemberKind.Method, sym.Signature, sym.Documentation));
                lib.Classes.Add(globalsClass);
            }

            if (ReferenceEquals(SelectedLibrary, lib) || SelectedLibrary == OBLibraryViewModel.AllLibraries)
                RebuildFilteredClasses();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ObjectBrowser: failed to load VBA built-in symbols");
            lib.IsLoaded = true;
        }
        finally { IsLoadingMembers = false; }
    }

    private void OnProjectLoaded(ProjectDefinition project) => AddProjectLibrary(project);

    private void OnProjectUnloaded(ProjectDefinition project)
    {
        if (!projectToLibrary.TryGetValue(project, out var lib)) return;
        Libraries.Remove(lib);
        projectToLibrary.Remove(project);
        if (SelectedLibrary == lib) SelectedLibrary = null;
        ReorderLibraries();
    }

    private void AddProjectLibrary(ProjectDefinition project)
    {
        var lib = new OBLibraryViewModel(project.Name);

        foreach (var form in project.Forms)
            lib.Classes.Add(new OBClassViewModel(form.Name, OBClassKind.Form, project.Name, formDefinition: form));

        foreach (var module in project.Modules)
        {
            var kind = module.Kind == ModuleKind.ClassModule ? OBClassKind.ClassModule : OBClassKind.Module;
            lib.Classes.Add(new OBClassViewModel(module.Name, kind, project.Name, moduleDefinition: module));
        }

        lib.Classes.Sort((a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        projectToLibrary[project] = lib;
        Libraries.Add(lib);

        foreach (var reference in project.References)
        {
            if (string.IsNullOrEmpty(reference.LibPath) && string.IsNullOrEmpty(reference.Name)) continue;
            var refName = !string.IsNullOrEmpty(reference.Name)
                ? reference.Name!
                : Path.GetFileNameWithoutExtension(reference.LibPath!);
            Libraries.Add(new OBLibraryViewModel(refName, reference));
        }

        ReorderLibraries();
    }

    private void ReorderLibraries()
    {
        // Order: [<All Libraries>] [focused project] [everything else, alpha]
        var focused = focusedProjectUtil.FocusedOrStartupProject != null
            && projectToLibrary.TryGetValue(focusedProjectUtil.FocusedOrStartupProject, out var fl)
            ? fl : null;

        var desired = Libraries
            .OrderBy(l => l == OBLibraryViewModel.AllLibraries ? 0 : l == focused ? 1 : 2)
            .ThenBy(l => l == OBLibraryViewModel.AllLibraries || l == focused
                ? string.Empty : l.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        for (int i = 0; i < desired.Count; i++)
        {
            int current = Libraries.IndexOf(desired[i]);
            if (current != i) Libraries.Move(current, i);
        }

        RebuildFilteredClasses();
    }

    private void RebuildFilteredClasses()
    {
        FilteredClasses.Clear();

        IEnumerable<OBClassViewModel> source;
        if (SelectedLibrary == null || SelectedLibrary == OBLibraryViewModel.AllLibraries)
        {
            var all = new List<OBClassViewModel>();
            foreach (var lib in Libraries)
            {
                if (lib == OBLibraryViewModel.AllLibraries) continue;
                all.AddRange(lib.Classes);
            }
            source = all;
        }
        else
        {
            source = SelectedLibrary.Classes;
        }

        var filter = searchText.Trim();
        foreach (var cls in source)
        {
            if (filter.Length == 0 || cls.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                FilteredClasses.Add(cls);
        }

        if (SelectedClass != null && !FilteredClasses.Contains(SelectedClass))
            SelectedClass = null;
    }

    private void OnClassSelectionChanged()
    {
        FilteredMembers.Clear();
        SelectedMember = null;
        GoToDefinitionCommand.RaiseCanExecutedChanged();
        PushHistoryForClassChange();

        if (SelectedClass == null) return;

        if (SelectedClass.Members.Count > 0)
        {
            PopulateFilteredMembers(SelectedClass);
            return;
        }

        if (lspClient.IsRunning && SelectedClass.CanNavigate)
            LoadMembersAsync(SelectedClass).ListenErrors();
    }

    private async Task LoadMembersAsync(OBClassViewModel classVm)
    {
        IsLoadingMembers = true;
        try
        {
            var uri = classVm.ModuleDefinition != null
                ? $"vb6://module/{classVm.ModuleDefinition.Name}"
                : $"vb6://form/{classVm.FormDefinition!.Name}";

            var symbols = await lspClient.RequestDocumentSymbolsAsync(uri, CancellationToken.None);

            foreach (var sym in symbols)
            {
                var (kind, signature) = MapSymbol(sym);
                classVm.Members.Add(new OBMemberViewModel(sym.Name, kind, signature));
            }

            if (ReferenceEquals(SelectedClass, classVm))
                PopulateFilteredMembers(classVm);
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ObjectBrowser: failed to load members for {ClassName}", classVm.Name);
        }
        finally
        {
            IsLoadingMembers = false;
        }
    }

    private void PopulateFilteredMembers(OBClassViewModel classVm)
    {
        FilteredMembers.Clear();
        var filter = searchText.Trim();
        foreach (var m in classVm.Members)
        {
            if (filter.Length == 0 || m.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                FilteredMembers.Add(m);
        }
    }

    private static (OBMemberKind kind, string signature) MapSymbol(DocumentSymbol sym) => sym.Kind switch
    {
        SymbolKind.Property => (OBMemberKind.Property, $"Property {sym.Name}"),
        SymbolKind.Enum     => (OBMemberKind.Constant, $"Enum {sym.Name}"),
        SymbolKind.Struct   => (OBMemberKind.Constant, $"Type {sym.Name}"),
        _                   => (OBMemberKind.Method,   sym.Name)
    };

    private async Task LoadReferenceLibraryAsync(OBLibraryViewModel lib)
    {
        if (lib.IsLoaded || lib.Reference == null) return;
        IsLoadingMembers = true;
        try
        {
            var info = await typeLibraryService.GetTypeLibInfoAsync(lib.Reference);
            lib.IsLoaded = true;

            if (info == null)
            {
                lib.Classes.Add(new OBClassViewModel(
                    "(Type metadata unavailable)", OBClassKind.Module, lib.Name));
            }
            else
            {
                foreach (var type in info.Types)
                {
                    var classVm = new OBClassViewModel(type.Name, MapTypeKind(type.Kind), info.Name);
                    foreach (var member in type.Members)
                        classVm.Members.Add(new OBMemberViewModel(
                            member.Name, MapMemberKind(member.Kind),
                            member.Signature, member.Documentation));
                    lib.Classes.Add(classVm);
                }
                lib.Classes.Sort((a, b) =>
                    string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
            }

            if (ReferenceEquals(SelectedLibrary, lib) || SelectedLibrary == OBLibraryViewModel.AllLibraries)
                RebuildFilteredClasses();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ObjectBrowser: failed to load reference library {Name}", lib.Name);
            lib.IsLoaded = true;
        }
        finally { IsLoadingMembers = false; }
    }

    private static OBClassKind MapTypeKind(TypeKind kind) => kind switch
    {
        TypeKind.Enum   => OBClassKind.Enum,
        TypeKind.Module => OBClassKind.Module,
        _               => OBClassKind.ClassModule
    };

    private static OBMemberKind MapMemberKind(MemberKind kind) => kind switch
    {
        MemberKind.PropertyGet or MemberKind.PropertyLet or MemberKind.PropertySet => OBMemberKind.Property,
        MemberKind.Event    => OBMemberKind.Event,
        MemberKind.Constant => OBMemberKind.Constant,
        _                   => OBMemberKind.Method
    };

    private void PushHistoryForClassChange()
    {
        if (_isNavigatingHistory || SelectedClass == null) return;
        if (_navIndex < _navHistory.Count - 1)
            _navHistory.RemoveRange(_navIndex + 1, _navHistory.Count - _navIndex - 1);
        _navHistory.Add(new NavEntry(SelectedLibrary, SelectedClass, null));
        if (_navHistory.Count > 50)
            _navHistory.RemoveAt(0);
        else
            _navIndex++;
        BackCommand.RaiseCanExecutedChanged();
        ForwardCommand.RaiseCanExecutedChanged();
    }

    private void UpdateCurrentHistoryMember()
    {
        if (_isNavigatingHistory || _navIndex < 0) return;
        _navHistory[_navIndex] = _navHistory[_navIndex] with { Member = SelectedMember };
    }

    private void GoBack()
    {
        if (_navIndex <= 0) return;
        _navIndex--;
        _isNavigatingHistory = true;
        try { ApplyNavEntry(_navHistory[_navIndex]); }
        finally { _isNavigatingHistory = false; BackCommand.RaiseCanExecutedChanged(); ForwardCommand.RaiseCanExecutedChanged(); }
    }

    private void GoForward()
    {
        if (_navIndex >= _navHistory.Count - 1) return;
        _navIndex++;
        _isNavigatingHistory = true;
        try { ApplyNavEntry(_navHistory[_navIndex]); }
        finally { _isNavigatingHistory = false; BackCommand.RaiseCanExecutedChanged(); ForwardCommand.RaiseCanExecutedChanged(); }
    }

    private void ApplyNavEntry(NavEntry entry)
    {
        SearchText = string.Empty;
        SelectedLibrary = entry.Library;
        RebuildFilteredClasses();
        SelectedClass = entry.Class;
        SelectedMember = entry.Member;
    }

    private void GoToDefinition()
    {
        if (SelectedClass == null) return;
        if (SelectedClass.ModuleDefinition != null)
            editorService.EditCode(SelectedClass.ModuleDefinition);
        else if (SelectedClass.FormDefinition != null)
            editorService.EditCode(SelectedClass.FormDefinition);
    }

    private void Close()
    {
        if (Factory is { } factory && Owner != null)
            factory.CloseDockable(this);
    }
}
