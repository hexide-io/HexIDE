using System;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using HexIDE.IDE;
using HexIDE.Localization;
using HexIDE.Runtime;

namespace HexIDE.Integration.Tests.Views;

public class ProgramBoxCaptionTests
{
    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate IntPtr GetString(uint button);

    // Independent native read: #4250's measurement pins command ID minus one, not command ID.
    private static string Caption(uint index, string english)
    {
        if (!OperatingSystem.IsWindows())
            return english;
        var library = NativeLibrary.Load("user32.dll");
        try
        {
            var get = Marshal.GetDelegateForFunctionPointer<GetString>(NativeLibrary.GetExport(library, "MB_GetString"));
            return (Marshal.PtrToStringUni(get(index)) ?? english).Replace("&&", "\u0001").Replace('&', '_').Replace('\u0001', '&');
        }
        finally { NativeLibrary.Free(library); }
    }

    [AvaloniaTheory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void Program_buttons_use_the_platform_under_a_French_IDE(bool input, bool yesNo)
    {
        var localization = new LocalizationService();
        var previous = localization.ActiveLanguage;
        var window = new Window();
        try
        {
            localization.Apply("fr");
            window.Content = input ? new HexIDE.Runtime.Dialogs.InputBox()
                : new HexIDE.Runtime.Dialogs.MessageBox { Buttons = yesNo ? MessageBoxButtons.YesNo : MessageBoxButtons.OkCancel };
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.GetVisualDescendants().OfType<Button>().Select(b => b.Content).Should().Equal(
                yesNo ? new[] { Caption(5, "Yes"), Caption(6, "No") } : new[] { Caption(0, "OK"), Caption(1, "Cancel") });
            ((Control)window.Content).Classes.Should().NotContain("ideDialog");
        }
        finally
        {
            window.Close();
            localization.Apply(previous);
        }
    }

    [AvaloniaTheory]
    [InlineData(false, null)]
    [InlineData(true, null)]
    [InlineData(false, "")]
    [InlineData(true, "")]
    public async Task Omitted_title_uses_the_hosts_brand_and_explicit_empty_stays_empty(bool input, string? title)
    {
        // #2042 tests construct identities independently rather than changing process-wide Distribution.
        var brand = new DistributionIdentity("acme", "Acme Studio");
        var owner = new Window();
        owner.Show();
        var library = new StandaloneStandardLib(owner, brand.Name);
        Task answer = input ? library.InputBox("probe", title, "")
            : library.MsgBox("probe", title, MessageBoxButtons.Ok, MessageBoxIcon.None);
        var dialog = owner.OwnedWindows.Single();
        try { dialog.Title.Should().Be(title ?? brand.Name); }
        finally
        {
            dialog.Close();
            owner.Close();
        }
        await answer;
    }
}
