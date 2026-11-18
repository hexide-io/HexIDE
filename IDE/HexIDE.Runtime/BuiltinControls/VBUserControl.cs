using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Threading;
using Avalonia.Controls;
using Avalonia.Media;
using HexIDE.Runtime.AvaloniaInterop;
using HexIDE.Runtime.Interpreter;
using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Runtime.BuiltinControls;

// A placed control's code has its own instance and uses its program's interpreter and event queue (#1631).
internal sealed class VBUserControl : Decorator, IModuleExecutionRoot
{
    internal VBWindowContext? Context { get; private set; }
    internal Canvas ContainerHost { get; } = new();
    private BackStyles backStyle;
    private readonly VBColor backColor;
    internal string AccessKeys { get; private set; } = "";
    internal bool DisplayAsDefault { get; private set; }
    private readonly Ambient ambient;
    private readonly FormDefinition definition;
    private VbObject? ambientForm;
    private string ambientName = "";
    private bool ambientRightToLeft;

    internal VBUserControl(Control contents, string accessKeys, FormDefinition definition)
    {
        this.definition = definition;
        Child = new Grid { Children = { contents, ContainerHost } };
        var root = definition.Components.First(c => c.BaseClass is Components.FormComponentClass);
        backColor = root.GetPropertyOrDefault(VBProperties.BackColorProperty);
        backStyle = VBLoader.RawNumber(root, "BackStyle") == 0 ? BackStyles.Transparent : BackStyles.Opaque;
        AccessKeys = accessKeys;
        ambient = new Ambient(this);
    }

    /// <summary>
    /// Attaches an array element made by <c>Load</c> (#4337) as its template was attached: the same form, extender name and
    /// right-to-left, since it sits in the template's container. Ambient.DisplayName reads the element's own index.
    /// </summary>
    internal void AttachLike(VBUserControl template, VBProgram program, string moduleName) =>
        Attach(program, moduleName, template.ambientForm, template.ambientName, template.ambientRightToLeft);

    internal void Attach(VBProgram program, string moduleName, VbObject? form, string name, bool rightToLeft)
    {
        if (!program.Interpreter.Modules.TryGet(moduleName, out var module))
            return;
        ambientForm = form;
        ambientName = name;
        ambientRightToLeft = rightToLeft;
        Context = new VBWindowContext(program.StandardLibrary);
        Context.AttachControl(program, module);
        if (Child is Panel old)
            old.Children.Remove(ContainerHost);
        Child = new Grid { Children =
        {
            VBLoader.SpawnComponents(definition, Context.ExecutionContext, Context.InstanceEnv, program.StandardLibrary,
                Context.FormInstance, program), ContainerHost
        } };
        ApplyBackground();
        var value = new Vb6Value(new RootObject(this));
        Context.ExecutionContext.AllocVariable(Context.InstanceEnv, "UserControl", value);
        Context.ExecutionContext.AllocVariable(Context.InstanceEnv, "Ambient", new Vb6Value(ambient));
    }

    internal async Task StartLoaded()
    {
        if (Context is not { } context)
            return;
        await context.ControlEvent("UserControl_Initialize");
        await context.ControlEvent("UserControl_Resize");
        await context.ControlEvent("UserControl_ReadProperties", [new Vb6Value(new VBWindowContext.UnimplementedPropertyBag())]);
    }

    private bool watchLoadedVisibility;
    internal bool LoadedAtRunTime => watchLoadedVisibility;
    internal void WatchLoadedVisibility() => watchLoadedVisibility = true;

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (watchLoadedVisibility && change.Property == IsVisibleProperty && IsVisible
            && TopLevel.GetTopLevel(this) is Window { IsVisible: true })
        {
            // #4337: Show is posted, after the Visible statement returns.
            Dispatcher.UIThread.Post(() => Context?.ExecuteSubIf("UserControl_Show", null,
                () => IsVisible && TopLevel.GetTopLevel(this) is Window { IsVisible: true }));
        }
    }

    private void ApplyBackground()
    {
        if (Child is Panel panel)
            panel.Background = backStyle == BackStyles.Transparent ? Brushes.Transparent : backColor.ToBrush();
    }

    internal void AccessKey(int ascii) => ExecuteSub("UserControl_AccessKeyPress", [new Vb6Value(ascii)]);

    internal void SetDisplayAsDefault(bool display)
    {
        if (DisplayAsDefault == display)
            return;
        DisplayAsDefault = display;
        ExecuteSub("UserControl_AmbientChanged", [new Vb6Value("DisplayAsDefault")]);
    }

    public void ExecuteSub(string name, IReadOnlyList<Vb6Value>? args = null) => Context?.ExecuteSub(name, args);
    public EventPump? Events => Context?.Events;
    public bool Handles(string name) => Context?.Handles(name) == true;
    // The measured root surface is implemented here; other members remain an explicit engine refusal.
    private sealed class RootObject(VBUserControl owner) : ICSharpProxy, ICSharpPropertyBag
    {
        public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException($"UserControl.{method} is not implemented (#1631)");
        public bool TryGetProperty(string name, out Vb6Value value)
        {
            value = name.ToLowerInvariant() switch
            {
                "containedcontrols" => new Vb6Value(new ContainedControls(owner)),
                "accesskeys" => new Vb6Value(owner.AccessKeys),
                "ambient" => new Vb6Value(owner.ambient),
                _ => throw new NotImplementedException($"UserControl.{name} is not implemented (#1631)"),
            };
            return true;
        }
        public bool TrySetProperty(string name, Vb6Value value)
        {
            if (name.Equals("BackStyle", StringComparison.OrdinalIgnoreCase))
            {
                var style = Convert.ToInt16(VB6BuiltIns.NarrowNum(value, Vb6Value.ValueType.Integer).Value);
                if (style is not (0 or 1))
                    throw new NotImplementedException("UserControl.BackStyle outside 0 and 1 is unmeasured (#1631)");
                owner.backStyle = (BackStyles)style;
                owner.ApplyBackground();
                return true;
            }
            if (!name.Equals("AccessKeys", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException($"UserControl.{name} is not implemented (#1631)");
            owner.AccessKeys = value.Type == Vb6Value.ValueType.String ? (string)value.Value! : throw new NotImplementedException("UserControl.AccessKeys conversion is not measured (#1631)");
            return true;
        }
    }

    private sealed class ContainedControls(VBUserControl owner) : ICSharpProxy, ICSharpPropertyBag
    {
        public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException($"ContainedControls.{method}");
        public bool TryGetProperty(string name, out Vb6Value value)
        {
            if (!name.Equals("Count", StringComparison.OrdinalIgnoreCase))
                throw new NotImplementedException($"ContainedControls.{name}");
            value = new Vb6Value(owner.ContainerHost.Children.Count);
            return true;
        }
        public bool TrySetProperty(string name, Vb6Value value) => throw new NotImplementedException($"ContainedControls.{name}");
    }

    private sealed class Ambient(VBUserControl owner) : ICSharpProxy, ICSharpPropertyBag
    {
        public void Call(string method, List<Vb6Value> args) => throw new NotImplementedException($"Ambient.{method}");
        public bool TryGetProperty(string name, out Vb6Value value)
        {
            if (name.Equals("DisplayAsDefault", StringComparison.OrdinalIgnoreCase))
            {
                value = new Vb6Value(owner.DisplayAsDefault);
                return true;
            }
            // #4448: a PictureBox or Frame child reads the form too; other containers remain unmeasured.
            if (UserControlContainers.TryGet(owner, "Container", out _)
                || !ContainerMembers.TryGet(owner, "Container", out var container)
                || !FormAmbients.TryForm(container.Value, owner.ambientForm, out var window))
                throw new NotImplementedException($"Ambient.{name} on a non-form container is not implemented (#4149)");
            value = name.ToLowerInvariant() switch
            {
                "backcolor" => new Vb6Value((long)(AttachedProperties.GetBackColor(window)
                    ?? throw new NotImplementedException("Ambient.BackColor is not implemented (#4149)")).ToOle()),
                "forecolor" => new Vb6Value((long)(AttachedProperties.GetForeColor(window)
                    ?? throw new NotImplementedException("Ambient.ForeColor is not implemented (#4149)")).ToOle()),
                "font" => new Vb6Value(ControlFont.Get(window)),
                // A control-array element's name carries its index, as `AmbCtl1(0)` (measured, oracle PR #4271).
                "displayname" => new Vb6Value(VBProps.GetIndex(owner) is { } index ? $"{owner.ambientName}({index})" : owner.ambientName),
                // 1033 whatever the user locale, on a Windows whose system locale and interface languages are US
                // English (measured, oracle PR #4271). Which of those it follows, if any, is unmeasured (#4266).
                "localeid" when WindowsLanguages.Current is { AllUsEnglish: true } => new Vb6Value(1033L),
                "localeid" => throw new NotImplementedException(
                    "Ambient.LocaleID has not been measured where the system locale or interface language is not US English (#4266)"),
                "textalign" => new Vb6Value(0),
                "scaleunits" => new Vb6Value("Twip"),
                "showgrabhandles" or "showhatching" or "uidead" => new Vb6Value(false),
                "righttoleft" when !owner.ambientRightToLeft && window.FlowDirection == FlowDirection.LeftToRight => new Vb6Value(false),
                "usermode" => new Vb6Value(true),
                "supportsmnemonics" or "messagereflect" => new Vb6Value(true),
                _ => throw new NotImplementedException($"Ambient.{name} is not implemented (#4149)"),
            };
            return true;
        }
        public bool TrySetProperty(string name, Vb6Value value) => throw new NotImplementedException($"Ambient.{name}");
    }
}
