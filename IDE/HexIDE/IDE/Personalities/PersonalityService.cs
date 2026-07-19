using System.Collections.Generic;
using HexIDE.Projects;

namespace HexIDE.IDE;

public class PersonalityService : IPersonalityService
{
    public IdePersonalityProfile ActiveProfile { get; }

    public PersonalityService()
    {
        ActiveProfile = Static.Personality switch
        {
            PersonalityName.Vba    => PersonalityProfiles.Vba,
            PersonalityName.VbaOde => PersonalityProfiles.VbaOde,
            _                      => PersonalityProfiles.Vb6,
        };
    }

    public bool Has(PersonalityFeature feature) => ActiveProfile.VisibleFeatures.Contains(feature);
    public bool IsProjectReferencesVisible => Has(PersonalityFeature.ProjectReferences);
    public bool IsToolsReferencesVisible   => Has(PersonalityFeature.ToolsReferences);
    public IReadOnlyList<IProjectTemplate> AvailableProjectTypes => ActiveProfile.AvailableProjectTypes;
}
