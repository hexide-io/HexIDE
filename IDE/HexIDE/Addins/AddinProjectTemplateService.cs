using HexIDE.Addins;

namespace HexIDE.Addins;

public sealed class AddinProjectTemplateService
{
    private readonly List<IAddinProjectTemplate> _templates = [];
    private readonly List<(string Id, HexIDE.Projects.IProjectTemplate Template)> _firstParty = [];

    /// <summary>The templates add-ins contributed, which the New Project dialog lists after the profile's.</summary>
    public IReadOnlyList<IAddinProjectTemplate> Templates => _templates;

    public bool HasTemplates => _firstParty.Count > 0 || _templates.Count > 0;

    /// <summary>
    /// The templates HexIDE's own project types offer, each with the id a profile names it by, in the order they were
    /// registered (#3411 slice 10b). A profile chooses from these.
    /// </summary>
    public IReadOnlyList<(string Id, HexIDE.Projects.IProjectTemplate Template)> FirstParty => _firstParty;

    /// <summary>The first first-party template with this id, or null when none is registered.</summary>
    public HexIDE.Projects.IProjectTemplate? FirstPartyTemplate(string id) =>
        _firstParty.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal)).Template;

    /// <summary>Adds a template a first-party project type offers.</summary>
    public void RegisterFirstParty(string id, HexIDE.Projects.IProjectTemplate template) => _firstParty.Add((id, template));

    public IDisposable Register(IAddinProjectTemplate template)
    {
        _templates.Add(template);
        var handle = new ActionDisposable(() => _templates.RemoveAt(_templates.LastIndexOf(template)));
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }
}
