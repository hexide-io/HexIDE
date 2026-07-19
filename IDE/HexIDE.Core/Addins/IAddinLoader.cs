using System.Threading.Tasks;

namespace HexIDE.Addins;

public interface IAddinLoader : IDisposable
{
    /// <summary>Verifies and loads add-ins. Pre-consented (first-party / previously-allowed) add-ins
    /// load immediately; others are left awaiting consent. Runs before the main window is shown.</summary>
    void LoadAll(IHexIdeHost host);

    /// <summary>Prompts for any add-ins awaiting first-load consent and late-loads the allowed ones.
    /// Must be called after the main window is visible (so the modal has an owner).</summary>
    Task PromptPendingConsentAsync();
}
