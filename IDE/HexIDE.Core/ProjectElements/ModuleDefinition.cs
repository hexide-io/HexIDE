using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HexIDE.Runtime.ProjectElements;

public partial class ModuleDefinition : INotifyPropertyChanged
{
    public ProjectDefinition Owner { get; }
    public ModuleKind Kind { get; }

    private string? absolutePath;
    private string name;

    /// <summary>The full file content — shown as-is in the code editor.</summary>
    public string Code { get; private set; }

    public FormDefinition? FormPart { get; private set; }

    public void UpdateFormPart(FormDefinition? formPart) => FormPart = formPart;

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

    public ModuleDefinition(ProjectDefinition owner, string name, ModuleKind kind)
    {
        Owner = owner;
        this.name = name;
        Kind = kind;
        // .bas/.cls keep their VB6 file header OUT of the editable Code (ModuleFileFormat adds it on save,
        // mirroring how FormSerializer manages a form's structural header) — so the editor shows only the
        // code body, as the VB6 IDE does, and clearing the editor can't corrupt the file. .ctl/.pag carry a
        // FormPart through FormSerializer, which expects the Attribute line to live in Code.
        Code = kind is ModuleKind.StandardModule or ModuleKind.ClassModule
            ? ""
            : $"Attribute VB_Name = \"{name}\"\r\n";
    }

    public void UpdateCode(string newCode) => Code = newCode;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
