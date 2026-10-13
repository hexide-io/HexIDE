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
    /// A class whose attribute runs describe a member and fail to, under the rules VB6 was measured to bind them
    /// by (hexide-io/HexIDE#273 task 4.1). Written by hand, in shapes the bundled grammar reads, so it also proves
    /// the server parses every run without a diagnostic; <see cref="FormWithReaderShapes"/> carries the shapes
    /// it does not.
    /// </summary>
    /// <remarks>
    /// Lines 0-5 are the header. Protected below it: 10-11 (a <c>WithEvents</c> variable and the next name of its
    /// comma list), 13 (the second name of a list with a comment after it), 15 (the second declaration after a
    /// <c>:</c>, which VB6's own dialog wrote), 17 (the second constant of two), 21 (a variable's run left naming
    /// the name a rename replaced), 27 (a declaration continued over 25-26), 32 and 36 (a property's <c>Get</c> and
    /// <c>Let</c>), and 43 (a procedure's run naming another member, which VB6 gives to the procedure all the
    /// same). Orphans: 7 (no qualifier), 19 (a procedure attribute under a variable), 23 (a variable's run naming
    /// a variable declared elsewhere), 29 (under a body statement), 39 (under a blank line), 41 (under a
    /// comment), 46-47 (the two kinds mixed) and 50 (a variable attribute under a procedure).
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
        "Attribute VB_Description = \"dotless, below the code\"\r\n" +                    // 7  orphan
        "\r\n" +                                                                          // 8
        "Public WithEvents Clock As Timer, Alarm As Timer\r\n" +                          // 9
        "Attribute Clock.VB_VarHelpID = -1\r\n" +                                         // 10
        "Attribute Alarm.VB_VarDescription = \"The alarm\"\r\n" +                         // 11
        "public rate as currency, count as long ' totals\r\n" +                           // 12
        "Attribute count.VB_VarDescription = \"How many\"\r\n" +                          // 13
        "Public A As Long: Public B As Long\r\n" +                                        // 14
        "Attribute B.VB_VarDescription = \"After a colon.\"\r\n" +                        // 15
        "Private Const C1 = 1, C2 = 2\r\n" +                                              // 16
        "Attribute C2.VB_VarDescription = \"Second of two consts.\"\r\n" +                // 17
        "Public Tally As Long\r\n" +                                                      // 18
        "Attribute Tally.VB_Description = \"a procedure attribute\"\r\n" +                // 19 orphan
        "Private Score As Long\r\n" +                                                     // 20
        "Attribute Points.VB_VarDescription = \"left by a rename\"\r\n" +                 // 21
        "Private Spare As Long\r\n" +                                                     // 22
        "Attribute Clock.VB_VarDescription = \"another variable's\"\r\n" +                // 23 orphan
        "\r\n" +                                                                          // 24
        "public function total(byval a as long, _\r\n" +                                  // 25
        "    byval b as long) as currency\r\n" +                                          // 26
        "Attribute total.VB_Description = \"The sum\"\r\n" +                              // 27
        "total = a + b\r\n" +                                                             // 28
        "Attribute total.VB_UserMemId = 0\r\n" +                                          // 29 orphan
        "end function\r\n" +                                                              // 30
        "public property get Value() as long\r\n" +                                       // 31
        "Attribute Value.VB_UserMemId = 0\r\n" +                                          // 32
        "Value = 1\r\n" +                                                                 // 33
        "end property\r\n" +                                                              // 34
        "public property let Value(byval v as long)\r\n" +                                // 35
        "Attribute Value.VB_Description = \"The value\"\r\n" +                            // 36
        "end property\r\n" +                                                              // 37
        "\r\n" +                                                                          // 38
        "Attribute Value.VB_HelpID = 1\r\n" +                                             // 39 orphan
        "' the next one describes nothing either\r\n" +                                   // 40
        "Attribute Value.VB_MemberFlags = \"40\"\r\n" +                                   // 41 orphan
        "public sub Halt()\r\n" +                                                         // 42
        "Attribute Total.VB_Description = \"named for another member\"\r\n" +             // 43
        "end sub\r\n" +                                                                   // 44
        "public sub Ring()\r\n" +                                                         // 45
        "Attribute Ring.VB_Description = \"mixed\"\r\n" +                                 // 46 orphan
        "Attribute Ring.VB_VarHelpID = -1\r\n" +                                          // 47 orphan
        "end sub\r\n" +                                                                   // 48
        "public sub Chime()\r\n" +                                                        // 49
        "Attribute Chime.VB_VarHelpID = -1\r\n" +                                         // 50 orphan
        "end sub\r\n";                                                                    // 51

    public const int ClassWithOrphansHeaderLines = 6;

    public static readonly int[] ClassWithOrphansDescribingLines = [10, 11, 13, 15, 17, 21, 27, 32, 36, 43];

    public static readonly int[] ClassWithOrphansOrphanLines = [7, 19, 23, 29, 39, 41, 46, 47, 50];

    /// <summary>
    /// A form giving each rule of the declaration reader, and each rule of what a line is, one run that only
    /// that rule decides (hexide-io/HexIDE#273 task 4.1). Written by hand, and not a file VB6 would compile:
    /// <c>PtrSafe</c>, an underscore alone in column 1 and an attribute line inside a declaration are refused
    /// by VB6 or the bundled grammar. It exists so that <c>BundledServerRespectsTheIdesRegionsTests</c>, which
    /// carries the same text, sees the two copies of the rule disagree on any of them.
    /// </summary>
    /// <remarks>
    /// Lines 0-8 are the header. Protected below it, each by the rule named: 11 (<c>PtrSafe</c> read as
    /// <c>Declare</c>), 22 (a bracketed name), 24 (a comma inside bounds), 27 (a continuation inside a comma
    /// list), 29 (every statement read), 35 (a string as one token), 43 (a run a rename left, naming nothing),
    /// 46 (a name ending in an underscore continuing nothing), 49-52 (attribute lines continued with
    /// <c> _</c>) and 72 (<c>AttributeCount</c> below it starting with another word). Orphans, each by the rule
    /// named: 14 and 18 (<c>Enum</c> and <c>Type</c> declaring nothing), 31 (a comment ending the line), 33
    /// (<c>Rem</c> after a <c>:</c> ending it), 37, 39 and 41 (naming a control, a local and a procedure, which the
    /// module declares), 61 (an underscore alone in column 1 continuing nothing), 65 (a local's, below the first
    /// procedure) and 68-69 (<c>Attribute(1)</c> starting with the reserved word, so one of the run, and naming
    /// nothing). Line 55 is no attribute line at all:
    /// it continues the declaration above it. The <c>Event</c> and <c>Declare</c> above the variables end
    /// nothing: every variable run is still in the declarations section.
    /// </remarks>
    public const string FormWithReaderShapes =
        "VERSION 5.00\r\n" +                                                                       // 0
        "Begin VB.Form frmShapes \r\n" +                                                          // 1
        "   Caption         =   \"Shapes\"\r\n" +                                                  // 2
        "   Begin VB.CommandButton cmdGo \r\n" +                                                  // 3
        "      Caption         =   \"Go\"\r\n" +                                                   // 4
        "   End\r\n" +                                                                            // 5
        "End\r\n" +                                                                               // 6
        "Attribute VB_Name = \"frmShapes\"\r\n" +                                                 // 7
        "Attribute VB_Exposed = False\r\n" +                                                      // 8
        "Option Explicit\r\n" +                                                                   // 9
        "Private Declare PtrSafe Function GetTickCount Lib \"kernel32\" () As Long\r\n" +         // 10
        "Attribute GetTickCount.VB_Description = \"PtrSafe is VBA7's Declare\"\r\n" +             // 11
        "Public Event Rang()\r\n" +                                                               // 12
        "Public Enum Sizes\r\n" +                                                                 // 13
        "Attribute Sizes.VB_VarDescription = \"an Enum declares nothing\"\r\n" +                  // 14 orphan
        "    Small = 1\r\n" +                                                                     // 15
        "End Enum\r\n" +                                                                          // 16
        "Private Type Point\r\n" +                                                                // 17
        "Attribute Point.VB_VarDescription = \"nor does a Type\"\r\n" +                           // 18 orphan
        "    X As Long\r\n" +                                                                     // 19
        "End Type\r\n" +                                                                          // 20
        "Private [Total] As Long\r\n" +                                                           // 21
        "Attribute Total.VB_VarDescription = \"a bracketed name\"\r\n" +                          // 22
        "Public a1 As Long, b1(1 To 3, 4) As String, c1 As Long\r\n" +                            // 23
        "Attribute c1.VB_VarDescription = \"past a comma in bounds\"\r\n" +                       // 24
        "Public d1 As Long, f1 As Long, _\r\n" +                                                  // 25
        "       e1 As Long\r\n" +                                                                 // 26
        "Attribute e1.VB_VarDescription = \"past a continuation\"\r\n" +                          // 27
        "Public g1 As Long, k1 As Long: Public h1 As Long\r\n" +                                  // 28
        "Attribute h1.VB_VarDescription = \"after a colon\"\r\n" +                                // 29
        "Public m1 As Long, p1 As Long ' x, n1\r\n" +                                             // 30
        "Attribute n1.VB_VarDescription = \"in a comment\"\r\n" +                                 // 31 orphan
        "Public q1 As Long, r1 As Long: Rem x: Dim s1 As Long\r\n" +                              // 32
        "Attribute s1.VB_VarDescription = \"after Rem\"\r\n" +                                    // 33 orphan
        "Private Const Lf = 10, Sep = \":\", Tb = 9\r\n" +                                        // 34
        "Attribute Tb.VB_VarDescription = \"past a string\"\r\n" +                                // 35
        "Public Spare1 As Long\r\n" +                                                             // 36
        "Attribute cmdGo.VB_VarDescription = \"names a control\"\r\n" +                           // 37 orphan
        "Public Spare2 As Long\r\n" +                                                             // 38
        "Attribute Inner.VB_VarDescription = \"names a local\"\r\n" +                             // 39 orphan
        "Public Spare3 As Long\r\n" +                                                             // 40
        "Attribute Lone.VB_VarDescription = \"names a procedure\"\r\n" +                          // 41 orphan
        "Public Spare4 As Long\r\n" +                                                             // 42
        "Attribute Gone.VB_VarDescription = \"names nothing: a rename left it\"\r\n" +            // 43
        "Private m_\r\n" +                                                                        // 44
        "Public Function Foo() As Long\r\n" +                                                     // 45
        "Attribute Foo.VB_Description = \"under a name ending in an underscore\"\r\n" +           // 46
        "End Function\r\n" +                                                                      // 47
        "Public Sub Wrapped()\r\n" +                                                              // 48
        "Attribute Wrapped.VB_Description = _\r\n" +                                              // 49
        "\"continued\"\r\n" +                                                                     // 50
        "Attribute _\r\n" +                                                                       // 51
        "Wrapped.VB_HelpID = 3\r\n" +                                                             // 52
        "End Sub\r\n" +                                                                           // 53
        "Public Sub Split(ByVal a As Long, _\r\n" +                                               // 54
        "Attribute Split.VB_Description = \"part of the declaration\"\r\n" +                      // 55
        "ByVal b As Long)\r\n" +                                                                  // 56
        "End Sub\r\n" +                                                                           // 57
        "Public Sub Lone(ByVal a As Long, _\r\n" +                                                // 58
        "_\r\n" +                                                                                 // 59
        "ByVal b As Long)\r\n" +                                                                  // 60
        "Attribute Lone.VB_Description = \"under the declaration's last line\"\r\n" +             // 61 orphan
        "End Sub\r\n" +                                                                           // 62
        "Public Sub Body()\r\n" +                                                                 // 63
        "    Dim Inner As Long\r\n" +                                                             // 64
        "Attribute Inner.VB_VarDescription = \"a local's\"\r\n" +                                 // 65 orphan
        "End Sub\r\n" +                                                                           // 66
        "Public Sub Reserved()\r\n" +                                                             // 67
        "Attribute Reserved.VB_Description = \"beside a line naming nothing\"\r\n" +              // 68 orphan
        "Attribute(1) = 2\r\n" +                                                                  // 69 orphan
        "End Sub\r\n" +                                                                           // 70
        "Public Sub Prefixed()\r\n" +                                                             // 71
        "Attribute Prefixed.VB_Description = \"beside a line of another word\"\r\n" +             // 72
        "AttributeCount = 2\r\n" +                                                                // 73
        "End Sub\r\n";                                                                            // 74

    public const int FormWithReaderShapesHeaderLines = 9;

    public static readonly int[] FormWithReaderShapesDescribingLines = [11, 22, 24, 27, 29, 35, 43, 46, 49, 50, 51, 52, 72];

    /// <summary>The lines of <paramref name="text"/>, split as the server splits them.</summary>
    public static string[] Lines(string text) =>
        text.Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
}
