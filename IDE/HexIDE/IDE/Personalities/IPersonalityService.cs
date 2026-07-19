using System.Collections.Generic;
using HexIDE.Projects;

namespace HexIDE.IDE;

public interface IPersonalityService
{
    IdePersonalityProfile ActiveProfile { get; }
    bool Has(PersonalityFeature feature);
    bool IsProjectReferencesVisible { get; }
    bool IsToolsReferencesVisible { get; }
    IReadOnlyList<IProjectTemplate> AvailableProjectTypes { get; }
}
