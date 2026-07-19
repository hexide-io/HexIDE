using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using HexIDE.Runtime.ProjectElements;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Tools;

public partial class ProjectViewModel : ObservableObject, IDisposable, IProjectTreeElement
{
    private readonly ProjectToolViewModel parentVm;
    private readonly ProjectDefinition projectDefinition;

    public string Name => projectDefinition.Name;

    public ObservableCollection<IProjectTreeElement> Elements { get; } = new();

    [Notify] private bool isExpanded = true;

    [Notify] private string file;

    [Notify] private bool isStartupProject;

    private readonly Dictionary<FormDefinition, FormViewModel> formViewModels = new();
    private readonly Dictionary<ModuleDefinition, ModuleViewModel> moduleViewModels = new();

    // Session-only memory of directory-node expansion, keyed by project-relative directory path.
    private readonly Dictionary<string, bool> directoryExpansion = new(StringComparer.OrdinalIgnoreCase);

    public ProjectDefinition Definition => projectDefinition;

    public ProjectViewModel(ProjectToolViewModel parentVm,
        ProjectDefinition projectDefinition)
    {
        this.parentVm = parentVm;
        this.projectDefinition = projectDefinition;
        file = projectDefinition.AbsolutePath == null ? projectDefinition.Name : Path.GetFileName(projectDefinition.AbsolutePath);
        projectDefinition.FormAdded += OnFormAdded;
        projectDefinition.FormDeleted += OnFormDeleted;
        projectDefinition.ModuleAdded += OnModuleAdded;
        projectDefinition.ModuleDeleted += OnModuleDeleted;
        projectDefinition.PropertyChanged += OnChanged;

        foreach (var form in this.projectDefinition.Forms)
            AddFormViewModel(form);
        foreach (var module in this.projectDefinition.Modules)
            AddModuleViewModel(module);
        Rebuild();
    }

    private void OnChanged(object? sender, PropertyChangedEventArgs e)
    {
        File = projectDefinition.AbsolutePath == null ? projectDefinition.Name : Path.GetFileName(projectDefinition.AbsolutePath);
        OnPropertyChanged(nameof(Name));
        // Save Project As re-anchors every member's tree placement.
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(ProjectDefinition.AbsolutePath))
            Rebuild();
    }

    private void AddFormViewModel(FormDefinition form)
    {
        formViewModels[form] = new FormViewModel(this, form);
        form.PropertyChanged += OnMemberDefinitionChanged;
    }

    private void AddModuleViewModel(ModuleDefinition module)
    {
        moduleViewModels[module] = new ModuleViewModel(this, module);
        module.PropertyChanged += OnMemberDefinitionChanged;
    }

    private void OnFormAdded(ProjectDefinition _, FormDefinition form)
    {
        AddFormViewModel(form);
        Rebuild();
    }

    private void OnFormDeleted(ProjectDefinition _, FormDefinition form)
    {
        if (formViewModels.Remove(form, out var vm))
        {
            form.PropertyChanged -= OnMemberDefinitionChanged;
            vm.Dispose();
        }
        Rebuild();
    }

    private void OnModuleAdded(ProjectDefinition _, ModuleDefinition module)
    {
        AddModuleViewModel(module);
        Rebuild();
    }

    private void OnModuleDeleted(ProjectDefinition _, ModuleDefinition module)
    {
        if (moduleViewModels.Remove(module, out var vm))
        {
            module.PropertyChanged -= OnMemberDefinitionChanged;
            vm.Dispose();
        }
        Rebuild();
    }

    private void OnMemberDefinitionChanged(object? sender, PropertyChangedEventArgs e)
    {
        // Save As re-homes a member; its tree placement follows the disk location.
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName == nameof(FormDefinition.AbsolutePath))
            Rebuild();
    }

    private void Rebuild()
    {
        HarvestExpansion(Elements);
        var selected = parentVm.SelectedItem;

        var children = ProjectTreeBuilder.BuildChildren(
            formViewModels.Values.Cast<IProjectFileNode>().Concat(moduleViewModels.Values),
            projectDefinition.AbsolutePath is { } vbpPath ? Path.GetDirectoryName(vbpPath) : null,
            directoryExpansion,
            this);

        Elements.Clear();
        foreach (var child in children)
            Elements.Add(child);

        RestoreSelection(selected);
    }

    private void HarvestExpansion(IEnumerable<IProjectTreeElement> nodes)
    {
        foreach (var node in nodes)
        {
            if (node is DirectoryViewModel dir)
            {
                directoryExpansion[dir.Key] = dir.IsExpanded;
                HarvestExpansion(dir.Children);
            }
        }
    }

    private void RestoreSelection(object? selected)
    {
        // Leaf instances are reused across rebuilds, so re-selecting them by instance suffices;
        // directory nodes are recreated and re-matched by Key. Selections owned by other
        // projects are left alone.
        switch (selected)
        {
            case FormViewModel form when form.Project == this:
                parentVm.SelectedItem = formViewModels.ContainsValue(form) ? form : null;
                break;
            case ModuleViewModel module when module.Project == this:
                parentVm.SelectedItem = moduleViewModels.ContainsValue(module) ? module : null;
                break;
            case DirectoryViewModel dir when dir.Project == this:
                parentVm.SelectedItem = FindDirectoryByKey(Elements, dir.Key);
                break;
        }
    }

    private static DirectoryViewModel? FindDirectoryByKey(IEnumerable<IProjectTreeElement> nodes, string key)
    {
        foreach (var node in nodes)
        {
            if (node is DirectoryViewModel dir)
            {
                if (string.Equals(dir.Key, key, StringComparison.OrdinalIgnoreCase))
                    return dir;
                if (FindDirectoryByKey(dir.Children, key) is { } match)
                    return match;
            }
        }
        return null;
    }

    public void Dispose()
    {
        foreach (var (form, vm) in formViewModels)
        {
            form.PropertyChanged -= OnMemberDefinitionChanged;
            vm.Dispose();
        }
        foreach (var (module, vm) in moduleViewModels)
        {
            module.PropertyChanged -= OnMemberDefinitionChanged;
            vm.Dispose();
        }
        projectDefinition.PropertyChanged -= OnChanged;
        projectDefinition.FormAdded -= OnFormAdded;
        projectDefinition.FormDeleted -= OnFormDeleted;
        projectDefinition.ModuleAdded -= OnModuleAdded;
        projectDefinition.ModuleDeleted -= OnModuleDeleted;
    }
}
