using System;
using Avalonia.Media;
using HexIDE.Addins;
using HexIDE.Projects;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.Forms.ViewModels;

public class ProjectTemplateViewModel : ObservableObject
{
    private static readonly Lazy<IImage> FallbackIcon = new(() => IconFactory.Themed("Geo.Object"));

    private readonly IAddinProjectTemplate _template;
    private readonly IImage _icon;

    public string Name => _template.Name;
    public IImage Icon => _icon;
    public bool Supported => _template.Supported;
    public IAddinProjectTemplate Template => _template;

    public ProjectTemplateViewModel(IProjectTemplate template)
    {
        _template = template;
        _icon = template.Icon;
    }

    public ProjectTemplateViewModel(IAddinProjectTemplate template)
    {
        _template = template;
        _icon = FallbackIcon.Value;
    }
}