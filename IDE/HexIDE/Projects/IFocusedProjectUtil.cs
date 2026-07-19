using System.ComponentModel;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Projects;

public interface IFocusedProjectUtil : INotifyPropertyChanged
{
    public ProjectDefinition? FocusedProject { get; }
    public ProjectDefinition? FocusedOrStartupProject { get; }
    public FormDefinition? FocusedForm { get; }
    public string FocusedComponentPosition { get; }
    public string FocusedComponentSize { get; }
}