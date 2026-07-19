using HexIDE.Addins;

namespace HexIDE.Addins;

public sealed class AddinProjectTemplateService
{
    private readonly List<IAddinProjectTemplate> _templates = [];
    public IReadOnlyList<IAddinProjectTemplate> Templates => _templates;

    public void Register(IAddinProjectTemplate template) => _templates.Add(template);
}
