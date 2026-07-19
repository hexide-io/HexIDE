using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using HexIDE.Runtime.Components;

namespace HexIDE.Runtime.ProjectElements;

public partial class FormDefinition : INotifyPropertyChanged
{
    public ProjectDefinition Owner { get; }

    /// <summary>
    /// Creates a FormDefinition with a pre-built component list (used by deserializers
    /// and factory methods that know the concrete IComponentClass for "Form").
    /// </summary>
    public FormDefinition(ProjectDefinition owner, IReadOnlyList<ComponentInstance> initialComponents, string code)
    {
        Owner = owner;
        Code = code;
        components = new List<ComponentInstance>(initialComponents);
    }

    /// <summary>
    /// Convenience constructor: creates a default form with initial Form component.
    /// The caller provides the concrete IComponentClass for "Form" (e.g. FormComponentClass.Instance).
    /// </summary>
    public FormDefinition(ProjectDefinition owner, IComponentClass formComponentClass, string name)
    {
        Owner = owner;
        Code = "Private Sub Form_Load()\n\nEnd Sub";
        components = new List<ComponentInstance>
        {
            new ComponentInstance(formComponentClass, name)
                .SetProperty(VBProperties.WidthProperty, 400d)
                .SetProperty(VBProperties.HeightProperty, 300d)
                .SetProperty(VBProperties.CaptionProperty, name)
        };
    }

    private string? absolutePath;
    private List<ComponentInstance> components;

    public string? AbsolutePath
    {
        get => absolutePath;
        set => SetField(ref absolutePath, value);
    }

    public IReadOnlyList<ComponentInstance> Components => components;

    public string Code { get; private set; }

    private bool lockControls;
    public bool LockControls
    {
        get => lockControls;
        set => SetField(ref lockControls, value);
    }

    public string Name
    {
        get
        {
            foreach (var c in components)
            {
                if (c.BaseClass.VBTypeName == "VB.Form")
                    return c.GetPropertyOrDefault(VBProperties.NameProperty) ?? throw new Exception("Form without a name!");
            }
            throw new Exception("FormDefinition has no form component!");
        }
    }

    public void UpdateCode(string newCode) => Code = newCode;

    public void UpdateComponents(IReadOnlyList<ComponentInstance> components)
    {
        this.components.Clear();
        this.components.AddRange(components);
        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Name)));
    }

    public string RootVBTypeName { get; private set; } = "VB.Form";

    // Raw text blocks for child component types that HexIDE does not support.
    // Each entry is the full Begin...End block, preserved verbatim for round-trip fidelity.
    public List<string> UnknownChildSubtreeTexts { get; } = [];

    public void UpdateRootTypeName(string typeName) => RootVBTypeName = typeName;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }

    // Overload accepting PropertyChangedEventArgs (used by UpdateComponents)
    protected virtual void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        PropertyChanged?.Invoke(this, e);
    }

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
