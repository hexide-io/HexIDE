using System;
using System.Linq;
using HexIDE.Runtime.Components;
using HexIDE.Runtime.Components.InBox;

namespace HexIDE.Runtime.Tests;

// VB6 SP6, #5293 and docs/vb6-fidelity-oracle/the-object-browsers-library-list.md.
public class FormMemberTableTests
{
    [Theory]
    [InlineData(MemberKind.Property, "ActiveControl, ControlBox, Controls, Count, HasDC, hDC, hWnd, Image, MaxButton, MDIChild, MinButton, Moveable, Name, ShowInTaskbar, StartUpPosition, WhatsThisButton, WhatsThisHelp, Appearance, AutoRedraw, BackColor, BorderStyle, Caption, ClipControls, CurrentX, CurrentY, DrawMode, DrawStyle, DrawWidth, Enabled, FillColor, FillStyle, Font, FontBold, FontItalic, FontName, FontSize, FontStrikethru, FontTransparent, FontUnderline, ForeColor, Height, HelpContextID, Icon, KeyPreview, Left, LinkMode, LinkTopic, MouseIcon, MousePointer, OLEDropMode, Palette, PaletteMode, Picture, RightToLeft, ScaleHeight, ScaleLeft, ScaleMode, ScaleTop, ScaleWidth, Tag, Top, Visible, Width, WindowState")]
    [InlineData(MemberKind.Method, "Circle, Cls, Hide, Line, Move, OLEDrag, PaintPicture, PopupMenu, PrintForm, PSet, Refresh, Scale, SetFocus, Show, ValidateControls, WhatsThisMode, ZOrder, Point, ScaleX, ScaleY, TextHeight, TextWidth")]
    [InlineData(MemberKind.Event, "Activate, Click, DblClick, Deactivate, DragDrop, DragOver, GotFocus, Initialize, KeyDown, KeyPress, KeyUp, LinkClose, LinkError, LinkExecute, LinkOpen, Load, LostFocus, MouseDown, MouseMove, MouseUp, OLECompleteDrag, OLEDragDrop, OLEDragOver, OLEGiveFeedback, OLESetData, OLEStartDrag, Paint, QueryUnload, Resize, Terminate, Unload")]
    public void Members_HaveMeasuredNamesAndKinds(MemberKind kind, string names)
    {
        FormComponentClass.Instance.Members.Where(m => m.Kind == kind).Select(m => m.Name)
            .Should().BeEquivalentTo(names.Split(", "));
    }

    [Fact]
    public void Members_AreUniqueSortedAndFromVb6()
    {
        var members = FormComponentClass.Instance.Members;
        members.Should().HaveCount(117);
        members.Select(m => m.Name).Should().OnlyHaveUniqueItems()
            .And.BeInAscendingOrder(StringComparer.OrdinalIgnoreCase);
        members.Should().OnlyContain(m => m.Origin == MemberOrigin.VB6);
        members.Where(m => m.IsFrmProperty).Select(m => m.Name).Should().BeEquivalentTo(
            "Caption BackColor Enabled ForeColor Icon MousePointer ShowInTaskbar Font StartUpPosition KeyPreview MaxButton MinButton Left Top Width Height Visible Tag".Split(' '));
        // #991: Refresh now runs; the drawing oracle is exercised by PaintAndRefreshTests.
        members.Where(m => m.Implemented).Select(m => m.Name).Should().BeEquivalentTo(
            "ActiveControl Controls Name Icon CurrentX CurrentY DrawWidth DrawStyle DrawMode FillStyle FillColor AutoRedraw hDC FontTransparent ScaleMode ScaleLeft ScaleTop ScaleWidth ScaleHeight WindowState Left Top Width Height MousePointer RightToLeft KeyPreview Visible Enabled Tag BackColor ForeColor Font Caption FontName FontSize FontBold FontItalic FontUnderline FontStrikethru Circle Cls Hide Line Move PSet Refresh Scale SetFocus Show Point ScaleX ScaleY Activate Click DblClick Deactivate GotFocus Initialize KeyDown KeyPress KeyUp Load LostFocus MouseDown MouseMove MouseUp Paint QueryUnload Resize Terminate Unload".Split(' '));
    }

    [Fact]
    public void OtherDesignerRoots_KeepTheirEmptyTables()
    {
        UserControlRootClass.Instance.Members.Should().BeEmpty();
        PropertyPageRootClass.Instance.Members.Should().BeEmpty();
    }
}
