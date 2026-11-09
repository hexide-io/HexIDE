using System;
using Avalonia.Media;
using HexIDE.Addins;
using HexIDE.Localization;
using HexIDE.Projects;
using HexIDE.Utils;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.Forms.ViewModels;

public class ProjectTemplateViewModel : ObservableObject
{
    private static readonly Lazy<IImage> FallbackIcon = new(() => IconFactory.Themed("Geo.Object"));

    private readonly IAddinProjectTemplate _template;
    private readonly IImage _icon;

    /// <summary>The template's English name, which is its identity to automation and the profile catalogue.</summary>
    public string Name => _template.Name;

    /// <summary>What the dialog shows: the template's name in the IDE's language, or an add-in's own name.</summary>
    public string DisplayName { get; }

    public IImage Icon => _icon;
    public bool Supported => _template.Supported;
    public IAddinProjectTemplate Template => _template;

    public ProjectTemplateViewModel(IProjectTemplate template, ILocalizationService localization)
    {
        _template = template;
        _icon = template.Icon;
        DisplayName = template.NameKey is { } key ? localization.GetString(key) : template.Name;
    }

    public ProjectTemplateViewModel(IAddinProjectTemplate template)
    {
        _template = template;
        _icon = FallbackIcon.Value;
        DisplayName = template.Name;
    }
}