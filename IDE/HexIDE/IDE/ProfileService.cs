using System.Collections.Generic;
using System.Linq;
using HexIDE.Projects;

namespace HexIDE.IDE;

public interface IProfileService
{
    bool IsProjectReferencesVisible { get; }
    bool IsToolsReferencesVisible { get; }
    IReadOnlyList<IProjectTemplate> AvailableProjectTypes { get; }
}

public sealed class ProfileService : IProfileService
{
    private static readonly string[] TemplateIds = ["standard-exe", "activex-exe", "activex-dll", "activex-control",
        "application-wizard", "wizard-manager", "data-project", "enterprise-controls"];

    public ProfileService() : this(Static.Profile) { }

    internal ProfileService(HexIDE.Profiles.ProfileSnapshot? profile)
    {
        IsProjectReferencesVisible = profile?.MenuVisibility.GetValueOrDefault("project.references", true) ?? true;
        IsToolsReferencesVisible = profile?.MenuVisibility.GetValueOrDefault("tools.references", false) ?? false;
        AvailableProjectTypes = profile?.ProjectTypes is { } ids
            ? ids.Select(id => IProjectTemplate.Templates[System.Array.IndexOf(TemplateIds, id)]).ToArray()
            : IProjectTemplate.Templates.ToArray();
    }

    public bool IsProjectReferencesVisible { get; }
    public bool IsToolsReferencesVisible { get; }
    public IReadOnlyList<IProjectTemplate> AvailableProjectTypes { get; }
}
