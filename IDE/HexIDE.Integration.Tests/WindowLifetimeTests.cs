using System;
using System.Threading.Tasks;
using HexIDE.Integration.Tests.Automation;
using HexIDE.Integration.Tests.Views;

namespace HexIDE.Integration.Tests;

public class WindowLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(typeof(AddinToolWindowReHostTests), nameof(AddinToolWindowReHostTests.AddinToolWindowContent_HostedByTwoViews_DoesNotCrash))]
    [InlineData(typeof(LineControlTests), nameof(LineControlTests.A_line_takes_no_clicks))]
    [InlineData(typeof(MenuMembersTests), nameof(MenuMembersTests.Choosing_an_element_of_a_menu_array_passes_its_Index))]
    [InlineData(typeof(MenuPopupTests), nameof(MenuPopupTests.A_leaf_item_still_reports_expand_as_unsupported))]
    [InlineData(typeof(ReadBackTests), nameof(ReadBackTests.A_toggle_on_a_radio_button_already_on_is_done_and_says_a_click_leaves_it_on))]
    [InlineData(typeof(RuntimeMenuTests), nameof(RuntimeMenuTests.ASeparator_HasAVisibleLineToDraw))]
    [InlineData(typeof(SelectUnreadTests), nameof(SelectUnreadTests.An_unread_selection_with_no_list_is_not_known_and_blames_no_list))]
    [InlineData(typeof(SnapshotComposerTests), nameof(SnapshotComposerTests.A_closed_popup_does_not_appear))]
    public Task Disposing_a_test_closes_every_window_it_opened(Type type, string method)
        => WindowLifetime.Disposing_a_test_closes_every_window_it_opened(type, method);
}
