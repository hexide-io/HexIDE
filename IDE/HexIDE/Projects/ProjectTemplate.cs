using System;
using Avalonia.Media;
using HexIDE.Runtime.ProjectElements;
using HexIDE.Utils;

namespace HexIDE.Projects;

public class ProjectTemplate : IProjectTemplate
{
    private readonly Func<string, ProjectDefinition> creator;

    public ProjectTemplate(string name,
        string iconName,
        bool supported,
        Func<string, ProjectDefinition> creator)
    {
        this.creator = creator;
        Supported = supported;
        Name = name;
        Icon = IconFactory.Themed(iconName switch
        {
            "standard_exe" => "Geo.Standardexe16",
            "activex_exe" => "Geo.Activexexe16",
            "activex_dll" => "Geo.Activexdll16",
            "activex_control" => "Geo.Activexcontrol16",
            "wizard" => "Geo.Wizard",
            _ => "Geo.Generic",
        });
    }

    public string Name { get; set; }

    public IImage Icon { get; set; }

    public bool Supported { get; set; }

    public ProjectDefinition Create(string name)
    {
        return creator(name);
    }
}