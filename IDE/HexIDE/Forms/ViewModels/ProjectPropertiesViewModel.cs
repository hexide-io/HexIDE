using System;
using System.Collections.Generic;
using System.Linq;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;
using PropertyChanged.SourceGenerator;

namespace HexIDE.Forms.ViewModels;

public partial class ProjectPropertiesViewModel : ObservableObject, IDialog
{
    public string Title { get; }
    public bool CanResize => false;

    public bool CanSetVb6Properties { get; }
    public event Action<bool>? CloseRequested;

    /// <summary>The Project Type list: what each row reads, and the value the project file stores.</summary>
    public List<ProjectTypeChoice> ProjectTypes { get; }

    [Notify] private ProjectTypeChoice? selectedProjectTypeChoice;

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
    /// <param name="generalPropertiesOnly">An add-in writer carries name and description, not VB6 type or startup settings.</param>
    public ProjectPropertiesViewModel(ProjectDefinition projectDefinition, Func<string, string?> validateName,
        ILocalizationService? localization = null, bool generalPropertiesOnly = false)
    {
        this.validateName = validateName;
        CanSetVb6Properties = !generalPropertiesOnly;
        Title = string.Format(localization?.GetString("Str.Dialog.ProjectProperties.TitleFormat") ?? "{0} - Project Properties",
            projectDefinition.Name);
        ProjectTypes =
        [
            new ProjectTypeChoice(VBProjectType.EXE,
                localization?.GetString(ProjectTypeNames.KeyOf(VBProjectType.EXE)!) ?? "Standard EXE"),
        ];
        selectedProjectType = projectDefinition.ProjectType;
        selectedProjectTypeChoice = ProjectTypes.Find(choice => choice.Value == selectedProjectType);
        // Sub Main first, then the forms — VB6's own order, and the order that puts the one entry a
        // code-only project can use where it is reachable without scrolling past forms it has none of.
        StartupObjects = new List<ProjectStartupObjectViewModel> { ProjectStartupObjectViewModel.SubMain };
        StartupObjects.AddRange(projectDefinition.Forms.Select(x => new ProjectStartupObjectViewModel(x)));

        selectedStartupObject = projectDefinition.StartsAtSubMain
            ? ProjectStartupObjectViewModel.SubMain
            : StartupObjects.FirstOrDefault(x => !x.IsSubMain && x.Form == projectDefinition.StartupForm);
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
        if (!CanSetVb6Properties)
            return;
        projectDefinition.ProjectType = SelectedProjectType;
        // Assign through exactly one of the two: they are mutually exclusive on the model, and setting
        // StartupForm to null while leaving StartsAtSubMain true would silently keep Sub Main selected.
        // No selected row leaves the project's startup unchanged (#3730).
        if (selectedStartupObject?.IsSubMain == true)
            projectDefinition.StartsAtSubMain = true;
        else if (selectedStartupObject is { } startupObject)
            projectDefinition.StartupForm = startupObject.Form;
    }

    private void OnProjectNameChanged()
    {
        NameError = string.IsNullOrEmpty(projectName) ? null : validateName(projectName);
        OkCommand.RaiseCanExecutedChanged();
    }

    private void OnNameErrorChanged() => OkCommand.RaiseCanExecutedChanged();

    private void OnSelectedProjectTypeChoiceChanged()
    {
        if (selectedProjectTypeChoice is { } choice)
            SelectedProjectType = choice.Value;
    }
}

/// <summary>One row of Project Properties' Project Type list.</summary>
/// <param name="Value">What the project file stores: the enum, spelled as the <c>.vbp</c>'s <c>Type=</c> line has it.</param>
/// <param name="DisplayName">What the developer reads, in the IDE's language.</param>
public sealed record ProjectTypeChoice(VBProjectType Value, string DisplayName)
{
    public override string ToString() => DisplayName;
}