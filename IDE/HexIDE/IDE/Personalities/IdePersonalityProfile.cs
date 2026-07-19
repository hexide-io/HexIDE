using System.Collections.Generic;
using HexIDE.Projects;

namespace HexIDE.IDE;

public sealed record IdePersonalityProfile(
    PersonalityName Name,
    IReadOnlySet<PersonalityFeature> VisibleFeatures,
    IReadOnlyList<IProjectTemplate> AvailableProjectTypes);
