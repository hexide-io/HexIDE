using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Integration.Tests;

public class WindowLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(typeof(AddinToolWindowReHostTests), nameof(AddinToolWindowReHostTests.AddinToolWindowContent_HostedByTwoViews_DoesNotCrash))]
    [InlineData(typeof(FormQualifiedMemberTests), nameof(FormQualifiedMemberTests.The_Immediate_window_reads_a_control_array_element_through_its_form_as_the_VB6_IDE_s_does))]
    [InlineData(typeof(FrameContainerRuntimeTests), nameof(FrameContainerRuntimeTests.AClickOnAFramesOwnSurface_BelongsToTheFrame))]
    [InlineData(typeof(LabelBorderTests), nameof(LabelBorderTests.A_3D_Fixed_Single_label_draws_a_sunken_edge_two_pixels_deep))]
    [InlineData(typeof(LabelColourTests), nameof(LabelColourTests.A_BackColor_set_at_run_time_paints_and_reads_back))]
    [InlineData(typeof(LineControlTests), nameof(LineControlTests.A_line_takes_no_clicks))]
    [InlineData(typeof(ListBoxClickTests), nameof(ListBoxClickTests.A_ListIndex_out_of_range_raises_380_and_no_Click))]
    [InlineData(typeof(ListBoxSelectedTests), nameof(ListBoxSelectedTests.MultiSelect_reads_as_an_Integer_and_cannot_be_assigned))]
    [InlineData(typeof(MenuMembersTests), nameof(MenuMembersTests.Choosing_an_element_of_a_menu_array_passes_its_Index))]
    [InlineData(typeof(MenuPopupTests), nameof(MenuPopupTests.A_leaf_item_still_reports_expand_as_unsupported))]
    [InlineData(typeof(OptionButtonValueTests), nameof(OptionButtonValueTests.AnUnparseableStringIntoABoolean_IsATrappableTypeMismatch))]
    [InlineData(typeof(PictureBoxContainerRuntimeTests), nameof(PictureBoxContainerRuntimeTests.AContainedControl_IsDrawnInsideTheBorder))]
    [InlineData(typeof(ReadBackTests), nameof(ReadBackTests.A_toggle_on_a_radio_button_already_on_is_done_and_says_a_click_leaves_it_on))]
    [InlineData(typeof(RuntimeMenuTests), nameof(RuntimeMenuTests.ASeparator_HasAVisibleLineToDraw))]
    [InlineData(typeof(RuntimeStackingTests), nameof(RuntimeStackingTests.A_controls_plane_does_not_come_from_its_number))]
    [InlineData(typeof(SelectUnreadTests), nameof(SelectUnreadTests.An_unread_selection_with_no_list_is_not_known_and_blames_no_list))]
    [InlineData(typeof(SnapshotComposerTests), nameof(SnapshotComposerTests.A_closed_popup_does_not_appear))]
    [InlineData(typeof(SymbolFontTests), nameof(SymbolFontTests.A_Wingdings_Label_draws_its_glyphs_where_the_font_is_installed_and_reads_back_its_letters))]
    public async Task Disposing_a_test_closes_every_window_it_opened(Type type, string method)
    {
        var opened = new List<Window>();
        var open = new HashSet<Window>();
        using var opening = Window.WindowOpenedEvent.AddClassHandler<Window>((window, _) =>
        {
            opened.Add(window);
            open.Add(window);
        });
        using var closing = Window.WindowClosedEvent.AddClassHandler<Window>((window, _) => open.Remove(window));
        try
        {
            var tests = Activator.CreateInstance(type)!;
            try
            {
                if (type.GetMethod(method)!.Invoke(tests, null) is Task task)
                    await task;
            }
            finally { (tests as IDisposable)?.Dispose(); }
            Dispatcher.UIThread.RunJobs();

            opened.Should().NotBeEmpty();
            open.Should().BeEmpty("{0}.{1} must close its windows when disposed", type.Name, method);
        }
        finally
        {
            foreach (var window in opened.AsEnumerable().Reverse())
                window.Close();
            Dispatcher.UIThread.RunJobs();
        }
    }
}
