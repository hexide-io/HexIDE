using HexIDE.IDE;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace HexIDE.Runtime.ProjectElements;

public partial class ModuleDefinition : INotifyPropertyChanged
{
    /// <summary>Source encoding retained through saves and path changes.</summary>
    public Vb6TextEncoding TextEncoding { get; set; } = Vb6TextEncoding.Ansi;

    public ProjectDefinition Owner { get; }
    public ModuleKind Kind { get; }

    private string? absolutePath;
    private string name;

    /// <summary>
    /// The module's code section: the file minus what its kind splits off at load.
    /// </summary>
    /// <remarks>
    /// <para>
    /// For a <c>.bas</c> or <c>.cls</c> that is the body below the header, which is held apart in
    /// <see cref="OriginalHeader"/>. For a <c>.ctl</c> or <c>.pag</c> it is everything after the designer block's
    /// root <c>End</c>, the leading <c>Attribute</c> run included, with the designer block on the
    /// <see cref="FormPart"/>. Where the load recognised no header, or could not parse the designer block, nothing
    /// was split off and this is the whole file.
    /// </para>
    /// <para>
    /// <b>This used to say it was the full file, shown as-is in the code editor.</b> It was neither, for any kind
    /// that split something off. Since hexide-io/HexIDE#273 the code window shows the whole file, but as a prefix
    /// composed in front of this (<c>FormCodeText.Prefix</c>), split off again on every flush, so this still never
    /// holds the header the window shows.
    /// </para>
    /// </remarks>
    public string Code { get; private set; }

    public FormDefinition? FormPart { get; private set; }

    /// <summary>
    /// Attaches (or detaches) the designer half of a UserControl or PropertyPage.
    /// </summary>
    /// <remarks>
    /// The link is recorded on the form as well, because the answer is needed in both directions and only
    /// one of them can be reached by looking. Going the other way means scanning the project's modules for
    /// one holding this form, which is wrong during construction: a UserControl's module and its form part
    /// are joined here several statements before the module is added to the project, and anything asking
    /// in that window would conclude the form stands alone.
    /// </remarks>
    public void UpdateFormPart(FormDefinition? formPart)
    {
        FormPart?.SetOwningModule(null);
        FormPart = formPart;
        formPart?.SetOwningModule(this);
    }

    public string? AbsolutePath
    {
        get => absolutePath;
        set => SetField(ref absolutePath, value);
    }

    public string Name
    {
        get => name;
        set => SetField(ref name, value);
    }

    public ModuleDefinition(ProjectDefinition owner, string name, ModuleKind kind)
    {
        Owner = owner;
        this.name = name;
        Kind = kind;
        // .bas/.cls keep their VB6 file header OUT of Code: ModuleFileFormat writes it in front on save, as
        // FormSerializer writes a form's designer block. The code window shows it all the same, as a greyed,
        // read-only prefix that the flush splits off again (#273), so typing in the window cannot reach the
        // header and Code stays the body. .ctl/.pag carry a FormPart through FormSerializer, which expects
        // the Attribute line to live in Code, so their new Code opens with it.
        Code = kind is ModuleKind.StandardModule or ModuleKind.ClassModule
            ? ""
            : $"Attribute VB_Name = \"{name}\"\r\n";
    }

    public void UpdateCode(string newCode) => Code = newCode;

    /// <summary>
    /// A <c>.bas</c> or <c>.cls</c> module's VB6 header exactly as it was read from disk, re-emitted verbatim on
    /// save with only its <c>Attribute VB_Name</c> retargeted, and shown as the code window's read-only header.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Regenerating a class header from the literal resets VB_Exposed, VB_Creatable, MultiUse and the
    /// data-binding keys — how VB6 encodes Instancing — on every save, including for files the user never
    /// opened. Preserving the original text is both safer and simpler than modelling those settings.
    /// </para>
    /// <para>
    /// <b>Null and empty mean different things, and this used to say a created module's was empty.</b>
    /// </para>
    /// <list type="bullet">
    /// <item><description><c>null</c> — never read from disk: a module HexIDE created. Its file will say the
    /// canonical literal, and the code window shows that.</description></item>
    /// <item><description><c>""</c> — read, and no header recognised, so <see cref="Code"/> holds the whole
    /// file. The code window adds nothing in front of it, and a save adds nothing either, unless the file has no
    /// <c>VB_Name</c> at all (hexide-io/HexIDE#472).</description></item>
    /// <item><description>anything else — the header as read.</description></item>
    /// </list>
    /// <para>
    /// <c>ModuleFileFormat.BufferHeader</c> tells the first two apart by this; <c>ModuleFileFormat.ToFileContent</c>
    /// by whether the body carries a <c>VB_Name</c>.
    /// </para>
    /// </remarks>
    public string? OriginalHeader { get; private set; }

    public void RecordOriginalHeader(string? header) => OriginalHeader = header;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
        => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    protected bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }
}
