// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

namespace HexIDE.VbLspServer.Tests;

/// <summary>
/// Whole VB6 files, in the shape VB6 writes them (CRLF, three-space designer indents, a trailing space after
/// each <c>Begin</c> line), for the tests that hold the server to leaving a header alone
/// (hexide-io/HexIDE#273 task 3.10).
/// </summary>
/// <remarks>
/// Inline rather than read from the corpus because no file in the corpus carries a member's attribute lines,
/// so a test built on the corpus alone would pass vacuously for half of what it claims. The code below each
/// header is deliberately unformatted, so that a formatter which protected too much would be caught too.
/// </remarks>
internal static class WholeFileFixtures
{
    /// <summary>A form: <c>VERSION</c>, an <c>Object</c> line, a nested designer block, the attribute run.</summary>
    /// <remarks>
    /// Lines 0-16 are the header: the designer block ends on line 11 and the attribute run is lines 12-16.
    /// The control is named <c>cmdOK</c>, a local in the code is called <c>Caption</c>, and <c>Caption</c> is
    /// also a property name three times over in the designer block.
    /// </remarks>
    public const string Form =
        "VERSION 5.00\r\n" +                                                               // 0
        "Object = \"{831FDD16-0C5C-11D2-A9FC-0000F8754DA1}#2.0#0\"; \"MSCOMCTL.OCX\"\r\n" + // 1
        "Begin VB.Form frmOrders \r\n" +                                                  // 2
        "   Caption         =   \"Orders\"\r\n" +                                          // 3
        "   ClientHeight    =   3000\r\n" +                                               // 4
        "   Begin VB.CommandButton cmdOK \r\n" +                                          // 5
        "      Caption         =   \"OK\"\r\n" +                                           // 6
        "      BeginProperty Font \r\n" +                                                 // 7
        "         Name            =   \"MS Sans Serif\"\r\n" +                             // 8
        "      EndProperty\r\n" +                                                         // 9
        "   End\r\n" +                                                                    // 10
        "End\r\n" +                                                                       // 11
        "Attribute VB_Name = \"frmOrders\"\r\n" +                                         // 12
        "Attribute VB_GlobalNameSpace = False\r\n" +                                      // 13
        "Attribute VB_Creatable = False\r\n" +                                            // 14
        "Attribute VB_PredeclaredId = True\r\n" +                                         // 15
        "Attribute VB_Exposed = False\r\n" +                                              // 16
        "Option Explicit\r\n" +                                                           // 17
        "\r\n" +                                                                          // 18
        "private sub cmdOK_Click()\r\n" +                                                 // 19
        "dim Caption as string\r\n" +                                                     // 20
        "Caption = cmdOK.Caption\r\n" +                                                   // 21
        "end sub\r\n";                                                                    // 22

    public const int FormHeaderLines = 17;

    /// <summary>A class: the <c>BEGIN … END</c> block, the attribute run, and members' attribute lines.</summary>
    /// <remarks>
    /// Lines 0-9 are the header. Line 13 is a variable's attribute, and lines 16-17 are a property's: a
    /// description, and a procedure attribute whose name has two dots.
    /// </remarks>
    public const string Class =
        "VERSION 1.0 CLASS\r\n" +                                                         // 0
        "BEGIN\r\n" +                                                                     // 1
        "  MultiUse = -1  'True\r\n" +                                                    // 2
        "  Persistable = 0  'NotPersistable\r\n" +                                        // 3
        "END\r\n" +                                                                       // 4
        "Attribute VB_Name = \"Order\"\r\n" +                                             // 5
        "Attribute VB_GlobalNameSpace = False\r\n" +                                      // 6
        "Attribute VB_Creatable = True\r\n" +                                             // 7
        "Attribute VB_PredeclaredId = False\r\n" +                                        // 8
        "Attribute VB_Exposed = False\r\n" +                                              // 9
        "Option Explicit\r\n" +                                                           // 10
        "\r\n" +                                                                          // 11
        "Public WithEvents Clock As Timer\r\n" +                                          // 12
        "Attribute Clock.VB_VarHelpID = -1\r\n" +                                         // 13
        "\r\n" +                                                                          // 14
        "public property get Total() as currency\r\n" +                                   // 15
        "Attribute Total.VB_Description = \"The order total\"\r\n" +                      // 16
        "Attribute Total.VB_ProcData.VB_Invoke_Property = \"General\"\r\n" +              // 17
        "total = 0\r\n" +                                                                 // 18
        "end property\r\n";                                                               // 19

    public const int ClassHeaderLines = 10;

    public static readonly int[] ClassMemberAttributeLines = [13, 16, 17];

    /// <summary>
    /// A class whose attribute runs describe a member and fail to (hexide-io/HexIDE#273 task 4.1). Written by
    /// hand, not by VB6: the orphans are shapes VB6 is not known to write, and are here to show that only a
    /// run the line above it declares is protected.
    /// </summary>
    /// <remarks>
    /// Lines 0-5 are the header. Protected below it: 10-11 (a <c>WithEvents</c> variable and the next name of
    /// its comma list), 13 (the second name of a list with a comment after it), 17 (a declaration continued over 15-16),
    /// 22 and 26 (a property's <c>Get</c> and <c>Let</c>). Orphans: 7 (no qualifier), 19 (under a body
    /// statement), 29 (under a blank line), 31 (under a comment) and 33 (naming a member declared elsewhere).
    /// <c>BundledServerRespectsTheIdesRegionsTests</c> carries the same text.
    /// </remarks>
    public const string ClassWithOrphans =
        "VERSION 1.0 CLASS\r\n" +                                                         // 0
        "BEGIN\r\n" +                                                                     // 1
        "  MultiUse = -1  'True\r\n" +                                                    // 2
        "END\r\n" +                                                                       // 3
        "Attribute VB_Name = \"Ledger\"\r\n" +                                            // 4
        "Attribute VB_Exposed = False\r\n" +                                              // 5
        "Option Explicit\r\n" +                                                           // 6
        "Attribute VB_Description = \"dotless, below the code\"\r\n" +                    // 7
        "\r\n" +                                                                          // 8
        "Public WithEvents Clock As Timer, Alarm As Timer\r\n" +                          // 9
        "Attribute Clock.VB_VarHelpID = -1\r\n" +                                         // 10
        "Attribute Alarm.VB_VarDescription = \"The alarm\"\r\n" +                         // 11
        "public rate as currency, count as long ' totals\r\n" +                           // 12
        "Attribute count.VB_VarDescription = \"How many\"\r\n" +                          // 13
        "\r\n" +                                                                          // 14
        "public function total(byval a as long, _\r\n" +                                  // 15
        "    byval b as long) as currency\r\n" +                                          // 16
        "Attribute total.VB_Description = \"The sum\"\r\n" +                              // 17
        "total = a + b\r\n" +                                                             // 18
        "Attribute total.VB_UserMemId = 0\r\n" +                                          // 19
        "end function\r\n" +                                                              // 20
        "public property get Value() as long\r\n" +                                       // 21
        "Attribute Value.VB_UserMemId = 0\r\n" +                                          // 22
        "Value = 1\r\n" +                                                                 // 23
        "end property\r\n" +                                                              // 24
        "public property let Value(byval v as long)\r\n" +                                // 25
        "Attribute Value.VB_Description = \"The value\"\r\n" +                            // 26
        "end property\r\n" +                                                              // 27
        "\r\n" +                                                                          // 28
        "Attribute Value.VB_HelpID = 1\r\n" +                                             // 29
        "' the next one describes nothing either\r\n" +                                   // 30
        "Attribute Value.VB_MemberFlags = \"40\"\r\n" +                                   // 31
        "public sub Halt()\r\n" +                                                         // 32
        "Attribute Total.VB_Description = \"misplaced\"\r\n" +                            // 33
        "end sub\r\n";                                                                    // 34

    public const int ClassWithOrphansHeaderLines = 6;

    public static readonly int[] ClassWithOrphansDescribingLines = [10, 11, 13, 17, 22, 26];

    public static readonly int[] ClassWithOrphansOrphanLines = [7, 19, 29, 31, 33];

    /// <summary>The lines of <paramref name="text"/>, split as the server splits them.</summary>
    public static string[] Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
}
