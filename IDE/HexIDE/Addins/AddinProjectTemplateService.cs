using HexIDE.Addins;

namespace HexIDE.Addins;

public sealed class AddinProjectTemplateService
{
    private readonly List<IAddinProjectTemplate> _templates = [];
    public IReadOnlyList<IAddinProjectTemplate> Templates => _templates;

    public IDisposable Register(IAddinProjectTemplate template)
    {
        _templates.Add(template);
        var handle = new ActionDisposable(() => _templates.RemoveAt(_templates.LastIndexOf(template)));
        AddinInitializationScope.Record(handle.Dispose);
        return handle;
    }
}
