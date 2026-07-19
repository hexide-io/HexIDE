using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.ProjectElements;

public partial class ProjectDefinition : INotifyPropertyChanged
{
    private FormDefinition? startupForm;
    private string? absolutePath;
    private string name;
    private string description = "";
    private VBProjectType projectType;

    private List<FormDefinition> forms = new();
    private List<ModuleDefinition> modules = new();
    private List<VbReference> references = new();

    public ProjectDefinition(VBProjectType projectType, string name)
    {
        this.name = name;
        this.projectType = projectType;
    }

    public FormDefinition? StartupForm
    {
        get => startupForm;
        set => SetField(ref startupForm, value);
    }

    public string? AbsolutePath
    {
        get => absolutePath;
        set => SetField(ref absolutePath, value);
    }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string Description
    {
        get => description;
        set => SetField(ref description, value);
    }

    public VBProjectType ProjectType
    {
        get => projectType;
        set => SetField(ref projectType, value);
    }

    public IReadOnlyList<FormDefinition> Forms => forms;
    public IReadOnlyList<ModuleDefinition> Modules => modules;
    public IReadOnlyList<VbReference> References => references;

    // Unknown key=value lines from the .vbp file (before any [Section] header), preserved verbatim.
    // PositionHint = count of recognised keys seen before each unknown line.
    public List<(int PositionHint, string RawLine)> UnknownPreSectionLines { get; } = new();

    // Verbatim .vbp item lines (Form=/UserDocument=/...) for project items that were parsed but not
    // loaded into the live model — e.g. a UserDocument (.dob, unsupported) or a Form whose .frm file is
    // missing on disk. Carried so an open->save round-trip never drops the node, even on a host that
    // lacks the file or a non-Windows machine. (A future spec may surface these in the project explorer
    // with a "missing"/"unsupported" glyph; for now they are preserved opaquely.)
    public List<string> PreservedItemLines { get; } = new();

    // The startup form's NAME as read from the .vbp. Retained even when the form itself couldn't be
    // loaded (missing .frm), so the Startup= line survives round-trip. StartupForm.Name takes precedence.
    public string? StartupFormName { get; set; }

    // Everything from the first [SectionName] line to EOF in the .vbp, preserved verbatim.
    public string? ExtensionTail { get; set; }

    public void AddReference(VbReference r)
    {
        if (!references.Contains(r))
            references.Add(r);
    }

    public void RemoveReference(VbReference r) => references.Remove(r);

    public void SetReferences(IEnumerable<VbReference> newReferences)
    {
        references.Clear();
        references.AddRange(newReferences);
    }

    public event Action<ProjectDefinition, FormDefinition>? FormAdded;
    public event Action<ProjectDefinition, FormDefinition>? FormDeleted;
    public event Action<ProjectDefinition, ModuleDefinition>? ModuleAdded;
    public event Action<ProjectDefinition, ModuleDefinition>? ModuleDeleted;

    public void AddForm(FormDefinition form)
    {
        forms.Add(form);
        FormAdded?.Invoke(this, form);
        StartupForm ??= form;
    }

    public void DeleteForm(FormDefinition form)
    {
        forms.Remove(form);
        FormDeleted?.Invoke(this, form);
        if (startupForm == form)
        {
            startupForm = forms.FirstOrDefault();
        }
    }

    public void AddModule(ModuleDefinition module)
    {
        modules.Add(module);
        ModuleAdded?.Invoke(this, module);
    }

    public void DeleteModule(ModuleDefinition module)
    {
        modules.Remove(module);
        ModuleDeleted?.Invoke(this, module);
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
