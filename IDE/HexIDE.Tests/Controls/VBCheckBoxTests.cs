using HexIDE.Runtime.BuiltinControls;
using HexIDE.Runtime.BuiltinTypes;

namespace HexIDE.Tests.Controls;

/// <summary>
/// Regression for the adversarial bug-hunt MED: a user click updated the checkbox's IsChecked but left the VB6
/// <c>Value</c> property stale, and the Click event fired only when the box became checked (suppressed on uncheck).
/// VB6 keeps Value in sync and fires Click on every click. (The Click-event firing itself no-ops here — it needs a
/// running form's execution root — but is now unconditional; the Value sync is asserted below.)
/// </summary>
public class VBCheckBoxTests
{
    // OnClick is protected (invoked by input); expose it so the toggle + Value-sync can be driven headlessly.
    private sealed class PokableCheckBox : VBCheckBox
    {
        public void Poke() => OnClick();
    }

    public VBCheckBoxTests() => AvaloniaTestSetup.EnsureInitialized();

    [Fact]
    public void UserClick_TogglesIsChecked_AndSyncsValue_OnCheckAndUncheck()
    {
        var cb = new PokableCheckBox();

        cb.Poke();   // user click → checked
        cb.IsChecked.Should().Be(true);
        cb.Value.Should().Be(VBCheckValue.Checked);   // was stale before the fix

        cb.Poke();   // user click → unchecked (was entirely suppressed before the fix)
        cb.IsChecked.Should().Be(false);
        cb.Value.Should().Be(VBCheckValue.Unchecked);
    }
}
