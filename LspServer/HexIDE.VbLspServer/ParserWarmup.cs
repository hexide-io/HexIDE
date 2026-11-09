// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

namespace HexIDE.VbLspServer;

internal static class ParserWarmup
{
    // Exercise declarations, expressions, calls and recovery on a background task (#2502).
    // The incomplete assignment forces the same SLL-to-LL path as an unfinished line in the editor.
    internal const string Source = """
        Option Explicit
        Private total As Long
        Public Sub Accumulate(ByVal count As Long)
            Dim i As Long
            For i = 1 To count
                If i > 0 Then
                    total = total + Twice(i)
                End If
            Next i
            total =
        End Sub
        Private Function Twice(ByVal value As Long) As Long
            Twice = value * 2
        End Function

        """;

    internal static ParseReport Run()
        => VbDiagnosticsProvider.GetDiagnosticsAndTreeReported(Source).Report;
}
