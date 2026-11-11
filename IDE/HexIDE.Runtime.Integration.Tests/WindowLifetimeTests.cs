using System;
using System.Threading.Tasks;
using HexIDE.Integration.Tests;
using HexIDE.Runtime.Integration.Tests.Views;

namespace HexIDE.Runtime.Integration.Tests;

public class WindowLifetimeTests
{
    [AvaloniaTheory]
    [InlineData(typeof(FormQualifiedMemberTests), nameof(FormQualifiedMemberTests.The_Immediate_window_reads_a_control_array_element_through_its_form_as_the_VB6_IDE_s_does))]
    [InlineData(typeof(FrameContainerRuntimeTests), nameof(FrameContainerRuntimeTests.AClickOnAFramesOwnSurface_BelongsToTheFrame))]
    [InlineData(typeof(LabelBorderTests), nameof(LabelBorderTests.A_3D_Fixed_Single_label_draws_a_sunken_edge_two_pixels_deep))]
    [InlineData(typeof(LabelColourTests), nameof(LabelColourTests.A_BackColor_set_at_run_time_paints_and_reads_back))]
    [InlineData(typeof(ListBoxClickTests), nameof(ListBoxClickTests.A_ListIndex_out_of_range_raises_380_and_no_Click))]
    [InlineData(typeof(ListBoxSelectedTests), nameof(ListBoxSelectedTests.MultiSelect_reads_as_an_Integer_and_cannot_be_assigned))]
    public Task Disposing_a_test_closes_every_window_it_opened(Type type, string method)
        => WindowLifetime.Disposing_a_test_closes_every_window_it_opened(type, method);
}
