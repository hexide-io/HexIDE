using System;
using Dock.Model.Mvvm.Controls;
using HexIDE.Events;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.ProjectElements;
using HexIDE.VisualDesigner;
using PropertyChanged.SourceGenerator;
using R3;

namespace HexIDE.Tools;

public partial class FormLayoutToolViewModel : Tool
{
    [Notify] private double _screenAspectRatio = 1.333;
    [Notify] private double _thumbLeft = 10;
    [Notify] private double _thumbTop = 10;
    [Notify] private double _thumbWidth = 50;
    [Notify] private double _thumbHeight = 30;

    private double _screenWidthPx;
    private double _screenHeightPx;
    private double _canvasWidth;
    private double _canvasHeight;
    private ComponentInstance? _rootComponent;

    public FormLayoutToolViewModel(IDocumentDockService documentDockService, IEventBus eventBus,
        ILocalizationService localization)
    {
        localization.BindTitle(this, "Str.Tool.FormLayout.Title");
        CanPin = false;
        CanClose = true;

        documentDockService
            .ObservePropertyChanged(x => x.ActiveDocument)
            .Subscribe(doc =>
            {
                if (doc is FormEditViewModel formEdit)
                    UpdateFromForm(formEdit.FormDefinition);
                else if (doc is null)
                    UpdateFromForm(null);
            });

        eventBus.Subscribe<ProjectUnloadedEvent>(_ => UpdateFromForm(null));
    }

    public void SetScreenBounds(double widthPx, double heightPx)
    {
        _screenWidthPx = widthPx;
        _screenHeightPx = heightPx;
        ScreenAspectRatio = heightPx > 0 ? widthPx / heightPx : 1.333;
        RecalculateThumb();
    }

    public void SetCanvasSize(double width, double height)
    {
        _canvasWidth = width;
        _canvasHeight = height;
        RecalculateThumb();
    }

    public void UpdateFromForm(FormDefinition? form)
    {
        if (_rootComponent != null)
            _rootComponent.OnComponentPropertyChanged -= OnRootPropertyChanged;

        _rootComponent = form is not null && form.Components.Count > 0
            ? form.Components[0]
            : null;

        if (_rootComponent != null)
            _rootComponent.OnComponentPropertyChanged += OnRootPropertyChanged;

        RecalculateThumb();
    }

    private void OnRootPropertyChanged(ComponentInstance instance, PropertyClass property)
    {
        if (property == VBProperties.LeftProperty   || property == VBProperties.TopProperty ||
            property == VBProperties.WidthProperty  || property == VBProperties.HeightProperty ||
            property == VBProperties.StartUpPositionProperty)
            RecalculateThumb();
    }

    private void RecalculateThumb()
    {
        if (_rootComponent == null || _canvasWidth <= 0 || _canvasHeight <= 0
            || _screenWidthPx <= 0 || _screenHeightPx <= 0)
            return;

        // Width/Height/Left/Top are stored in logical pixels (Avalonia units), matching screen dimensions
        var fw         = _rootComponent.GetPropertyOrDefault(VBProperties.WidthProperty);
        var fh         = _rootComponent.GetPropertyOrDefault(VBProperties.HeightProperty);
        var startupPos = _rootComponent.GetPropertyOrDefault(VBProperties.StartUpPositionProperty);

        ThumbWidth  = Math.Max(4, fw / _screenWidthPx * _canvasWidth);
        ThumbHeight = Math.Max(2, fh / _screenHeightPx * _canvasHeight);

        double left, top;
        if (startupPos == VBStartupPosition.StartUpManual)
        {
            var fl = _rootComponent.GetPropertyOrDefault(VBProperties.LeftProperty);
            var ft = _rootComponent.GetPropertyOrDefault(VBProperties.TopProperty);
            left = fl / _screenWidthPx * _canvasWidth;
            top  = ft / _screenHeightPx * _canvasHeight;
        }
        else if (startupPos == VBStartupPosition.StartUpWindowsDefault)
        {
            left = _canvasWidth  * 0.1;
            top  = _canvasHeight * 0.1;
        }
        else // CenterScreen or CenterOwner
        {
            left = (_canvasWidth  - ThumbWidth)  / 2;
            top  = (_canvasHeight - ThumbHeight) / 2;
        }

        ThumbLeft = Math.Clamp(left, 0, Math.Max(0, _canvasWidth  - ThumbWidth));
        ThumbTop  = Math.Clamp(top,  0, Math.Max(0, _canvasHeight - ThumbHeight));
    }
}
