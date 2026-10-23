using System;
using System.ComponentModel;
using Avalonia;
using HexIDE.Runtime.BuiltinTypes;
using HexIDE.Runtime.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace HexIDE.VisualDesigner;

/// <summary>
/// A control HexIDE does not model, drawn where VB6 draws the box it puts in such a control's place (#157).
/// </summary>
/// <remarks>
/// <para>
/// <b>View-only, and never a <c>ComponentInstance</c>.</b> The control is written back from the block it was preserved
/// as, and a component instance is what the writer writes, so one made up for display would risk the control going out
/// twice. Nothing here moves or changes it: the block is carried, not edited.
/// </para>
/// <para>
/// Its position follows the container it sits in, a Frame or PictureBox the designer does model, or the form. The size
/// is VB6's for the box (<see cref="UnmodelledControl"/>), in the designer's pixels as the deserializer converts twips.
/// </para>
/// </remarks>
public sealed partial class UnmodelledControlViewModel : ObservableObject, IDisposable
{
    private readonly UnmodelledControl control;
    private readonly ComponentInstanceViewModel? container;

    /// <param name="container">The modelled container it sits in, or null for the form itself.</param>
    /// <param name="depth">How many of the container's carried controls are under this one: those written after it.</param>
    public UnmodelledControlViewModel(UnmodelledControl control, ComponentInstanceViewModel? container, int depth = 0)
    {
        this.control = control;
        this.container = container;
        Depth = depth;
        if (container is not null)
            container.PropertyChanged += OnContainerChanged;
    }

    /// <summary>The modelled container it sits in, or null for the form itself.</summary>
    internal ComponentInstanceViewModel? Container => container;

    /// <summary>How many of its container's carried controls are under this one.</summary>
    internal int Depth { get; }

    /// <summary>Where the designer's canvas stacks the box among the controls, the highest on top (#1553).</summary>
    public int DrawIndex
    {
        get => drawIndex;
        internal set => SetProperty(ref drawIndex, value);
    }

    private int drawIndex;

    /// <summary>The control's class as its <c>Begin</c> line gives it, such as <c>MSComctlLib.ImageList</c>.</summary>
    public string TypeName => control.TypeName;

    /// <summary>
    /// The class without its library, such as <c>ImageList</c>, as VB6's <c>TypeName</c> gives it: what fits in the box,
    /// whose default is 80 pixels wide. The tooltip has the whole of <see cref="TypeName"/>.
    /// </summary>
    public string ClassName => control.TypeName[(control.TypeName.LastIndexOf('.') + 1)..];

    public string Name => control.Name;

    public double Left => Origin.X + Pixels(control.Left);

    public double Top => Origin.Y + Pixels(control.Top);

    public double Width => Pixels(control.Width);

    public double Height => Pixels(control.Height);

    private Point Origin => container?.ClientOrigin ?? default;

    private static double Pixels(double twips) => twips / VBScaleModeExtensions.PixelToTwips;

    /// <summary>Lets go of the container, which outlives this box: the designer rebuilds the boxes on every change.</summary>
    public void Dispose()
    {
        if (container is not null)
            container.PropertyChanged -= OnContainerChanged;
    }

    private void OnContainerChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(ComponentInstanceViewModel.Left) or nameof(ComponentInstanceViewModel.Top))
        {
            OnPropertyChanged(nameof(Left));
            OnPropertyChanged(nameof(Top));
        }
    }
}
