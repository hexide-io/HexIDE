using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CommunityToolkit.Mvvm.Input;
using HexIDE.Runtime.Serialization;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Components;
using HexIDE.Utils;
using HexIDE.VisualDesigner.Commands;
using HexIDE.IDE;
using HexIDE.Events;
using HexIDE.Forms.ViewModels;
using HexIDE.Runtime.ProjectElements;
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
    private ModuleDefinition? currentModule;

    /// <summary>
    /// Localized property-grid description (key <c>Str.PropDesc.{name}</c>) with the English text
    /// embedded in <c>HexIDE.Core</c> as the fallback. Property names stay English (VB6 API identifiers).
    /// </summary>
    internal string ResolvePropertyDescription(PropertyClass propertyClass) =>
        localization.GetPropertyDescription(propertyClass.Name) ?? propertyClass.Description;

    /// <summary>Localized property-grid category header (key <c>Str.PropCategory.{category}</c>),
    /// falling back to the English enum name.</summary>
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
        localization.LanguageChanged += () =>
        {
            foreach (var property in Properties)
                property.RefreshDescription();
            foreach (var category in CategorizedProperties.OfType<PropertyCategoryViewModel>())
                category.RefreshHeader();
        };
        CanPin = false;
        CanClose = true;

        documentDockService
            .ObservePropertyChanged(x => x.ActiveDocument)
            .Subscribe(ShowDocument);
        eventBus.Subscribe<ModulePropertiesSelectedEvent>(e =>
        {
            if (e.Module != null)
                ShowModule(e.Module);
            else if (currentModule != null)
                ShowDocument(documentDockService.ActiveDocument);
        });
    }

    private void ShowDocument(BaseEditorWindowViewModel? document)
    {
        if (document is CodeEditorViewModel { ModuleDefinition: { } module })
        {
            ShowModule(module);
            return;
        }
        currentModule = null;
        SelectedProperty = null;
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
                        foreach (var propertyClass in PropertyText.InWindowOrder(component.Instance.BaseClass))
                        {
                            var prop = properties[propertyClass] = new PropertyViewModel(this, propertyClass, Displayed(component.Instance, propertyClass));
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
    }

    private void ShowModule(ModuleDefinition? module)
    {
        currentDocumentSub?.Dispose();
        currentDocumentSub = null;
        currentDocument = null;
        if (currentComponent != null)
            currentComponent.Instance.OnComponentPropertyChanged -= OnComponentValueChanged;
        currentComponent = null;
        currentModule = module;
        Properties.Clear();
        CategorizedProperties.Clear();
        SelectedProperty = null;
        OnPropertyChanged(nameof(ComponentsProxy));
        OnPropertyChanged(nameof(SelectedComponentProxy));
        if (module == null)
            return;
        var row = new PropertyViewModel(this, VBProperties.NameProperty, module.Name);
        Properties.Add(row);
        CategorizedProperties.Add(row);
        SelectedProperty = row;
    }

    private void RenameModule(object? value)
    {
        var module = currentModule!;
        var proposed = value as string;
        var refusal = !ProjectNaming.IsValidName(proposed)
            ? string.Format(localization.GetString("Str.Naming.Msg.NotAVb6Name"), proposed)
            : ProjectNaming.IsNameTaken(module.Owner, proposed!, DocumentIdentity.For(module))
                ? string.Format(localization.GetString("Str.Naming.Msg.DocumentNameTaken"), proposed)
                : null;
        try
        {
            if (refusal != null)
                throw new DataValidationException(refusal);
            if (module.Name == proposed)
                return;
            if (module.FormPart is { } form)
            {
                form.Components[0].SetProperty(VBProperties.NameProperty, proposed!);
                form.NotifyRootPropertiesChanged();
            }
            module.Name = proposed!;
            if (module.FormPart is { } designer)
                EventBus.Publish(new FormLayoutChangedEvent(designer));
            EventBus.Publish(new DocumentNameChangedEvent(DocumentIdentity.For(module)));
        }
        catch (DataValidationException e)
        {
            windowManager.MessageBox(e.Message, icon: MessageBoxIcon.Error).ListenErrors();
            var row = Properties[0];
            Avalonia.Threading.Dispatcher.UIThread.Post(() => row.UpdateValueNoRaise(module.Name));
        }
    }

    private void OnComponentValueChanged(ComponentInstance instance, PropertyClass property)
    {
        // A position is shown with what decides its units and the frame round a form, which another property can change.
        foreach (var prop in Properties)
        {
            if (prop.PropertyClass == property || DesignPositions.IsPosition(prop.PropertyClass))
                prop.UpdateValueNoRaise(Displayed(instance, prop.PropertyClass));
        }
    }

    /// <summary>
    /// The value a row shows: the object's own, except a position, which is shown as VB6's window shows it, in its
    /// container's ScaleMode, and a form's own size with its frame, in twips (#1636). The model holds pixels.
    /// </summary>
    private object? Displayed(ComponentInstance instance, PropertyClass property) =>
        DesignPositions.IsPosition(property) && currentDocument?.FormDefinition is { } form
        && instance.GetBoxedPropertyOrDefault(property) is double pixels
            ? DesignPositions.Shown(form, instance, property, pixels)
            : instance.GetBoxedPropertyOrDefault(property);

    private void OnSelectedPropertyChanged()
    {
        if (selectedProperty != null)
        {
            lastSelectedPropertyClass = selectedProperty.PropertyClass;
        }
    }

    internal static byte[]? ExportablePicture(PropertyClass property, object? value) =>
        property == VBProperties.PictureProperty || property == VBProperties.IconProperty
        || property == VBProperties.MouseIconProperty || property == VBProperties.DragIconProperty
            ? FrxImageHelper.PicturePayload(value as byte[]) : null;

    internal async Task ExportPictureAsync(PropertyClass property, object? value)
    {
        if (ExportablePicture(property, value) is not { } payload) return;
        try
        {
            var extension = FrxImageHelper.DetectKind(payload) switch
            {
                FrxImageHelper.FrxBlobKind.Bitmap => "bmp",
                FrxImageHelper.FrxBlobKind.Icon => "ico",
                FrxImageHelper.FrxBlobKind.Cursor => "cur",
                _ => "bin",
            };
            var path = await windowManager.SaveFilePickerAsync(new FilePickerSaveOptions
            {
                Title = localization.GetString("Str.Tool.Properties.ExportPicture"),
                SuggestedFileName = property.Name + "." + extension,
                DefaultExtension = extension,
            });
            if (path != null) AtomicFileWriter.WriteAllBytes(path, payload);
        }
        catch (Exception e)
        {
            await windowManager.MessageBox(string.Format(localization.GetString("Str.Tool.ProtocolInspector.ExportFailed"),
                e.Message), icon: MessageBoxIcon.Error);
        }
    }

    public void UpdateValue(PropertyClass propertyClass, object? value)
    {
        if (currentModule != null && propertyClass == VBProperties.NameProperty)
        {
            RenameModule(value);
            return;
        }
        if (currentDocument == null || currentDocument.SelectedComponent == null)
            return;

        var instance = currentDocument.SelectedComponent.Instance;
        var before = instance.GetBoxedPropertyOrDefault(propertyClass);
        object? after = before;
        bool committed = false;

        try
        {
            // A position is typed in the units its row shows, and kept in pixels (#1636).
            if (DesignPositions.IsPosition(propertyClass) && currentDocument.FormDefinition is { } form)
                value = ModelPosition(form, instance, propertyClass, value);
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

        // A refused value goes back to what the object holds. The row kept showing it, so after the refusal the
        // grid displayed a value the control did not have, and a caller reading the row back saw the change it
        // had just been refused (#625). POSTED, and measured to need it: this runs inside the row's binding
        // writing to its source, and a change raised during that write is not carried back to the text box.
        if (!committed && Properties.FirstOrDefault(p => p.PropertyClass == propertyClass) is { } row)
            Avalonia.Threading.Dispatcher.UIThread.Post(() => row.UpdateValueNoRaise(before));

        if (committed && !Equals(before, after))
            currentDocument.PushSetPropertyCommand(instance, propertyClass, before, after);
    }

    // The model's pixels for a position typed into its row, or the value as it came when it is not a number, which the
    // rest of UpdateValue refuses.
    private static object? ModelPosition(FormDefinition form, ComponentInstance instance, PropertyClass property, object? value)
    {
        var units = value switch
        {
            double d => (double?)d,
            string text when PropertyText.TryParse(property, text.Trim(), out var parsed) && parsed is double d => d,
            _ => null,
        };
        return units is { } shown ? DesignPositions.ToPixels(form, instance, property, shown) : value;
    }
}

public abstract partial class BasePropertyViewModel : ObservableObject
{
    /// <summary>
    /// What a row is called to anything that cannot see it: a screen reader, or automation addressing the row by
    /// name. Without it each row was named after its view model's type, every row alike. (#526)
    /// </summary>
    public abstract string AccessibleName { get; }

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
    }

    private void OnIsExpandedChanged()
    {
        foreach (var property in parent.Properties)
        {
            if (property.PropertyClass.Category == category)
                property.IsVisible = IsExpanded;
        }
    }

    public string Header => parent.ResolveCategoryHeader(category);

    internal void RefreshHeader()
    {
        OnPropertyChanged(nameof(Header));
        OnPropertyChanged(nameof(AccessibleName));
    }

    internal PropertyCategory Category => category;

    public override string AccessibleName => Header;
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
        ExportPictureCommand = new AsyncRelayCommand(() => parent.ExportPictureAsync(PropertyClass, Value), () => CanExportPicture);
    }

    public void OnValueChanged()
    {
        parent.UpdateValue(PropertyClass, Value);
        OnPropertyChanged(nameof(CanExportPicture));
        ExportPictureCommand.NotifyCanExecuteChanged();
        OnPropertyChanged(nameof(Value));
    }

    public bool CanExportPicture => PropertiesToolViewModel.ExportablePicture(PropertyClass, Value) != null;

    public IAsyncRelayCommand ExportPictureCommand { get; }

    public string Name { get; }

    public override string AccessibleName => Name;
    [Notify] private object? value;
    public string Description => parent.ResolvePropertyDescription(PropertyClass);

    internal void RefreshDescription() => OnPropertyChanged(nameof(Description));
    public PropertyClass PropertyClass { get; }

    public void UpdateValueNoRaise(object? newValue)
    {
        this.value = newValue;
        OnPropertyChanged(nameof(Value));
        OnPropertyChanged(nameof(CanExportPicture));
        ExportPictureCommand.NotifyCanExecuteChanged();
    }
}
