using System.Collections.Generic;
using HexIDE.Projects;

namespace HexIDE.IDE;

public static class PersonalityProfiles
{
    public static IdePersonalityProfile Vb6 { get; } = new(
        PersonalityName.Vb6,
        new HashSet<PersonalityFeature>
        {
            PersonalityFeature.ProjectReferences,
        },
        new List<IProjectTemplate>(IProjectTemplate.Templates));

    public static IdePersonalityProfile VbaOde { get; } = new(
        PersonalityName.VbaOde,
        new HashSet<PersonalityFeature>
        {
            PersonalityFeature.ToolsReferences,
        },
        new List<IProjectTemplate>
        {
            IProjectTemplate.StandardEXE,
        });

    public static IdePersonalityProfile Vba { get; } = new(
        PersonalityName.Vba,
        new HashSet<PersonalityFeature>
        {
            PersonalityFeature.ToolsReferences,
        },
        new List<IProjectTemplate>());
}
