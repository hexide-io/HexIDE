using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HexIDE.Runtime.ProjectElements;

public class ProjectGroupDefinition : INotifyPropertyChanged
{
    private string name;
    private string? absolutePath;

    public ProjectGroupDefinition(string name)
    {
        this.name = name;
    }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public string? AbsolutePath
    {
        get => absolutePath;
        set => SetField(ref absolutePath, value);
    }

    // Unknown lines from the .vbg file, preserved verbatim for round-trip fidelity.
    public List<string> UnknownLines { get; } = [];

    public event PropertyChangedEventHandler? PropertyChanged;

    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        return true;
    }
}
