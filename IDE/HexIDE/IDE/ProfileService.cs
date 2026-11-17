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
    private readonly HexIDE.Profiles.ProfileSnapshot? profile;
    private readonly HexIDE.Addins.AddinProjectTemplateService templates;

    public ProfileService(HexIDE.Addins.AddinProjectTemplateService templates) : this(Static.Profile, templates) { }

    /// <summary>The current profile over VB6's templates alone, for a caller with no template service.</summary>
    public ProfileService() : this(Static.Profile) { }

    // Without a template service, which only a test leaves out, VB6's templates are the registered ones.
    internal ProfileService(HexIDE.Profiles.ProfileSnapshot? profile, HexIDE.Addins.AddinProjectTemplateService? templates = null)
    {
        this.profile = profile;
        this.templates = templates ?? WithVb6Templates();
        IsProjectReferencesVisible = profile?.MenuVisibility.GetValueOrDefault("project.references", true) ?? true;
        IsToolsReferencesVisible = profile?.MenuVisibility.GetValueOrDefault("tools.references", false) ?? false;
    }

    private static HexIDE.Addins.AddinProjectTemplateService WithVb6Templates()
    {
        var service = new HexIDE.Addins.AddinProjectTemplateService();
        Vb6ProjectType.RegisterTemplates(service);
        return service;
    }

    public bool IsProjectReferencesVisible { get; }
    public bool IsToolsReferencesVisible { get; }

    /// <summary>
    /// The first-party templates registered (#3411 slice 10b), as the profile names and orders them, or all of them where it
    /// names none. A name nothing registered offers is left out.
    /// </summary>
    public IReadOnlyList<IProjectTemplate> AvailableProjectTypes => profile?.ProjectTypes is { } ids
        ? ids.Select(templates.FirstPartyTemplate).OfType<IProjectTemplate>().ToArray()
        : templates.FirstParty.Select(t => t.Template).ToArray();
}
