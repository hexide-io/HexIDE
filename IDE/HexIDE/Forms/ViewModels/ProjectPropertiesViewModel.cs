using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ProjectPropertiesViewModel : ObservableObject, IDialog
{
    public string Title { get; }
    public bool CanResize => false;
    public event Action<bool>? CloseRequested;

    public List<VBProjectType> ProjectTypes { get; } =
    [
        VBProjectType.EXE
    ];

    [Notify] private VBProjectType selectedProjectType;

    public List<ProjectStartupObjectViewModel> StartupObjects { get; }

    [Notify] private ProjectStartupObjectViewModel? selectedStartupObject;

    [Notify] private string projectName;

    [Notify] private string projectDescription;

    public DelegateCommand OkCommand { get; }

    public DelegateCommand CancelCommand { get; }

    /// <summary>
    /// Why the name in the box cannot be used, or null when it can. Shown beside the box.
    /// </summary>
    /// <remarks>
    /// A message rather than a disabled button alone. Two projects of a group may not share a name -- VB6
    /// refuses the whole group at load and builds none of it -- and a refusal the developer cannot read is
    /// indistinguishable from a dialog that has stopped working.
    /// </remarks>
    [Notify] private string? nameError;

    private readonly Func<string, string?> validateName;

    /// <param name="validateName">
    /// Answers why a proposed project name cannot be used, or null when it can. Required rather than
    /// optional: a validator that can be left out is one that silently is.
    /// </param>
    public ProjectPropertiesViewModel(ProjectDefinition projectDefinition, Func<string, string?> validateName)
    {
        this.validateName = validateName;
        Title = $"{projectDefinition.Name} - Project Properties";
        selectedProjectType = projectDefinition.ProjectType;
        // Sub Main first, then the forms — VB6's own order, and the order that puts the one entry a
        // code-only project can use where it is reachable without scrolling past forms it has none of.
        StartupObjects = new List<ProjectStartupObjectViewModel> { ProjectStartupObjectViewModel.SubMain };
        StartupObjects.AddRange(projectDefinition.Forms.Select(x => new ProjectStartupObjectViewModel(x)));

        selectedStartupObject = projectDefinition.StartsAtSubMain
            ? ProjectStartupObjectViewModel.SubMain
            : StartupObjects.FirstOrDefault(x => x.Form == projectDefinition.StartupForm);
        projectName = projectDefinition.Name;
        projectDescription = projectDefinition.Description;

        nameError = validateName(projectName);
        OkCommand = new DelegateCommand(() => CloseRequested?.Invoke(true),
            () => !string.IsNullOrEmpty(projectName) && nameError is null);
        CancelCommand = new DelegateCommand(() => CloseRequested?.Invoke(false), () => true);
    }

    public void Apply(ProjectDefinition projectDefinition)
    {
        projectDefinition.Name = projectName;
        projectDefinition.Description = projectDescription;
        projectDefinition.ProjectType = SelectedProjectType;
        // Assign through exactly one of the two: they are mutually exclusive on the model, and setting
        // StartupForm to null while leaving StartsAtSubMain true would silently keep Sub Main selected
        // after the user picked (nothing).
        if (selectedStartupObject?.IsSubMain == true)
            projectDefinition.StartsAtSubMain = true;
        else
            projectDefinition.StartupForm = selectedStartupObject?.Form;
    }

    private void OnProjectNameChanged()
    {
        NameError = string.IsNullOrEmpty(projectName) ? null : validateName(projectName);
        OkCommand.RaiseCanExecutedChanged();
    }

    private void OnNameErrorChanged() => OkCommand.RaiseCanExecutedChanged();
}