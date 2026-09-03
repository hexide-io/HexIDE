using HexIDE.Runtime.ProjectElements;

namespace HexIDE.Forms.ViewModels;

/// <summary>
/// One entry in Project Properties → General → Startup Object: either a form, or <c>Sub Main</c>.
///
/// <para>
/// <c>Sub Main</c> is listed UNCONDITIONALLY, exactly as VB6 lists it, and not gated on a
/// <c>Sub Main</c> actually existing. Gating would make the entry appear and vanish as the user edits
/// code, and — worse — would stop them choosing it *before* writing the procedure, which is the order
/// a code-only project is usually built in. A missing or unsuitable <c>Main</c> is reported when the
/// project runs, which is where VB6 reports it too (as a compile error; here as the run begins).
/// </para>
/// </summary>
public class ProjectStartupObjectViewModel
{
    public string Header { get; }

    /// <summary>The form this entry selects, or null when the entry is <c>Sub Main</c>.</summary>
    public FormDefinition? Form { get; }

    /// <summary>True for the <c>Sub Main</c> entry — the one startup object that is not a form.</summary>
    public bool IsSubMain { get; }

    public ProjectStartupObjectViewModel(FormDefinition form)
    {
        Form = form;
        Header = form.Name;
    }

    private ProjectStartupObjectViewModel(string header)
    {
        Header = header;
        IsSubMain = true;
    }

    /// <summary>The <c>Sub Main</c> entry. Its header is VB6's own spelling, which is also the value
    /// written to the .vbp — the dialog and the file agree by construction rather than by coincidence.</summary>
    public static ProjectStartupObjectViewModel SubMain { get; } =
        new(HexIDE.Runtime.Serialization.SerializedProject.SubMainStartup);

    public override string ToString() => Header;
}
