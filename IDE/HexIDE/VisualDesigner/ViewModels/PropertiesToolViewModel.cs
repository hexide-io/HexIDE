using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using HexIDE.Runtime.Components;
using HexIDE.Utils;
using HexIDE.VisualDesigner.Commands;
using HexIDE.IDE;
using HexIDE.Localization;
using CommunityToolkit.Mvvm.ComponentModel;
using Dock.Model.Mvvm.Controls;
using PropertyChanged.SourceGenerator;
using R3;
using Serilog;

namespace HexIDE.VisualDesigner;

public partial class PropertiesToolViewModel : Tool
{
    public IEventBus EventBus { get; }
    private readonly IWindowManager windowManager;
    private readonly ILocalizationService localization;

    /// <summary>
    /// Localized property-grid description (key <c>Str.PropDesc.{name}</c>) with the English text
    /// embedded in <c>HexIDE.Core</c> as the fallback. Property names stay English (VB6 API identifiers).
    /// </summary>
    internal string ResolvePropertyDescription(PropertyClass propertyClass) =>
        localization.GetPropertyDescription(propertyClass.Name) ?? propertyClass.Description;

    /// <summary>Localized property-grid category header (key <c>Str.PropCategory.{category}</c>),
    /// falling back to the English enum name. Refreshes on next selection (like descriptions).</summary>
    internal string ResolveCategoryHeader(PropertyCategory category)
    {
        var key = $"Str.PropCategory.{category}";
        var value = localization.GetString(key);
        return string.IsNullOrEmpty(value) || value == key ? category.ToString() : value;
    }
    public ObservableCollection<ComponentInstanceViewModel>? ComponentsProxy => currentDocument?.AllComponents;
    public ObservableCollection<PropertyViewModel> Properties { get; } = new();
    public ObservableCollection<BasePropertyViewModel> CategorizedProperties { get; } = new();
    [Notify] private PropertyViewModel? selectedProperty;

    private PropertyClass? lastSelectedPropertyClass;

    public ComponentInstanceViewModel? SelectedComponentProxy
    {
        get => currentDocument?.SelectedComponent;
        set
        {
            if (currentDocument != null)
                currentDocument.SelectedComponent = value;
        }
    }

    private System.IDisposable? currentDocumentSub;
    private FormEditViewModel? currentDocument;
    private ComponentInstanceViewModel? currentComponent;

    public PropertiesToolViewModel(IDocumentDockService documentDockService,
        IEventBus eventBus,
        IWindowManager windowManager,
        ILocalizationService localization)
    {
        EventBus = eventBus;
        this.windowManager = windowManager;
        this.localization = localization;
        localization.BindTitle(this, "Str.Tool.Properties.Title");
        CanPin = false;
        CanClose = true;

        documentDockService
            .ObservePropertyChanged(x => x.ActiveDocument)
            .Subscribe(document =>
            {
                Properties.Clear();
                CategorizedProperties.Clear();
                currentDocumentSub?.Dispose();
                currentDocument = null;
                OnPropertyChanged(nameof(ComponentsProxy));
                if (currentComponent != null)
                {
                    currentComponent.Instance.OnComponentPropertyChanged -= OnComponentValueChanged;
                    currentComponent = null;
                }
                if (document is FormEditViewModel formEditViewModel)
                {
                    currentDocument = formEditViewModel;
                    OnPropertyChanged(nameof(ComponentsProxy));
                    currentDocumentSub = formEditViewModel.ObservePropertyChanged(y => y.SelectedComponent)
                        .Subscribe(component =>
                        {
                            Properties.Clear();
                            CategorizedProperties.Clear();

                            if (currentComponent != null)
                            {
                                currentComponent.Instance.OnComponentPropertyChanged -= OnComponentValueChanged;
                                currentComponent = null;
                            }

                            if (component != null)
                            {
                                currentComponent = component;
                                Dictionary<PropertyClass, PropertyViewModel> properties = new();
                                foreach (var propertyClass in component.Instance.BaseClass.Properties.OrderBy(prop => prop.Name))
                                {
                                    var prop = properties[propertyClass] = new PropertyViewModel(this, propertyClass, component.Instance.GetBoxedPropertyOrDefault(propertyClass));
                                    Properties.Add(prop);
                                }
                                foreach (var categoryGroup in component.Instance.BaseClass.Properties.GroupBy(p => p.Category))
                                {
                                    CategorizedProperties.Add(new PropertyCategoryViewModel(this, categoryGroup.Key));
                                    foreach (var propertyClass in categoryGroup.OrderBy(prop => prop.Name))
                                    {
                                        var prop = properties[propertyClass];
                                        CategorizedProperties.Add(prop);
                                    }
                                }

                                SelectedProperty = Properties.FirstOrDefault(x => x.PropertyClass == lastSelectedPropertyClass) ??
                                                   /* hardcoded default properties */
                                                   Properties.FirstOrDefault(x => x.PropertyClass == VBProperties.CaptionProperty) ??
                                                   Properties.FirstOrDefault(x => x.PropertyClass == VBProperties.IntervalProperty) ??
                                                   Properties.FirstOrDefault(x => x.PropertyClass == VBProperties.ShapeProperty) ??
                                                   Properties.FirstOrDefault(x => x.PropertyClass == VBProperties.NameProperty);

                                currentComponent.Instance.OnComponentPropertyChanged += OnComponentValueChanged;
                            }

                            OnPropertyChanged(nameof(SelectedComponentProxy));
                        });
                }
            });
    }

    private void OnComponentValueChanged(ComponentInstance instance, PropertyClass property)
    {
        foreach (var prop in Properties)
        {
            if (prop.PropertyClass == property)
            {
                prop.UpdateValueNoRaise(instance.GetBoxedPropertyOrDefault(property));
                break;
            }
        }
    }

    private void OnSelectedPropertyChanged()
    {
        if (selectedProperty != null)
        {
            lastSelectedPropertyClass = selectedProperty.PropertyClass;
        }
    }

    public void UpdateValue(PropertyClass propertyClass, object? value)
    {
        if (currentDocument == null || currentDocument.SelectedComponent == null)
            return;

        var instance = currentDocument.SelectedComponent.Instance;
        var before = instance.GetBoxedPropertyOrDefault(propertyClass);
        object? after = before;
        bool committed = false;

        try
        {
            if (value is string stringValue)
            {
                if (propertyClass.TryParseString(stringValue, out var typedValue))
                {
                    instance.SetUntypedProperty(propertyClass, typedValue);
                    after = typedValue;
                    committed = true;
                }
                else throw new Exception();
            }
            else if (value == null)
            {
                instance.SetUntypedProperty(propertyClass, null);
                after = null;
                committed = true;
            }
            else if (value.GetType() == propertyClass.PropertyType)
            {
                instance.SetUntypedProperty(propertyClass, value);
                after = value;
                committed = true;
            }
            else throw new Exception();
        }
        catch (DataValidationException e)
        {
            windowManager.MessageBox(e.Message, icon: MessageBoxIcon.Error).ListenErrors();
        }
        catch (Exception e)
        {
            Log.Error(e, "Invalid property value");
            windowManager.MessageBox("Invalid property value", icon: MessageBoxIcon.Error).ListenErrors();
        }

        if (committed && !Equals(before, after))
            currentDocument.UndoStack.Push(new SetPropertyCommand(instance, propertyClass, before, after));
    }
}

public abstract partial class BasePropertyViewModel : ObservableObject
{
    [Notify] private bool isVisible = true;
}

public partial class PropertyCategoryViewModel : BasePropertyViewModel
{
    private readonly PropertiesToolViewModel parent;
    private readonly PropertyCategory category;
    [Notify] private bool isExpanded = true;

    public PropertyCategoryViewModel(PropertiesToolViewModel parent, PropertyCategory category)
    {
        this.parent = parent;
        this.category = category;
        Header = parent.ResolveCategoryHeader(category);
    }

    private void OnIsExpandedChanged()
    {
        foreach (var property in parent.Properties)
        {
            if (property.PropertyClass.Category == category)
                property.IsVisible = IsExpanded;
        }
    }

    public string Header { get; }
}

public partial class PropertyViewModel : BasePropertyViewModel
{
    private readonly PropertiesToolViewModel parent;

    public PropertyViewModel(PropertiesToolViewModel parent,
        PropertyClass propertyClass,
        object? value)
    {
        this.parent = parent;
        PropertyClass = propertyClass;
        Name = propertyClass.Name;
        this.value = value;
        Description = parent.ResolvePropertyDescription(propertyClass);
    }

    public void OnValueChanged()
    {
        parent.UpdateValue(PropertyClass, Value);
        OnPropertyChanged(nameof(Value));
    }

    public string Name { get; }
    [Notify] private object? value;
    public string Description { get; }
    public PropertyClass PropertyClass { get; }

    public void UpdateValueNoRaise(object? newValue)
    {
        this.value = newValue;
        OnPropertyChanged(nameof(Value));
    }
}
