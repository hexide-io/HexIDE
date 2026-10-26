// SPDX-License-Identifier: MIT
// Copyright (C) 2026 The HexIDE Authors

using Antlr4.Runtime;
using Antlr4.Runtime.Tree;

namespace HexIDE.VbLspServer;

/// <summary>Names closed blocks from the cached CST, without binding names or inferring types.</summary>
internal static class VbInlayHintProvider
{
    public static List<LspInlayHint> GetHints(VisualBasic6Parser.StartRuleContext? tree, string source)
    {
        var hints = new List<LspInlayHint>();
        if (tree is null) return hints;
        var lines = source.ReplaceLineEndings("\n").Split('\n');
        var pending = new Stack<IParseTree>();
        pending.Push(tree);
        while (pending.TryPop(out var node))
        {
            if (node is ParserRuleContext context)
            {
                var (end, label) = context switch
                {
                    VisualBasic6Parser.SubStmtContext c => (c.END_SUB(), "Sub " + c.ambiguousIdentifier().GetText()),
                    VisualBasic6Parser.FunctionStmtContext c => (c.END_FUNCTION(), "Function " + c.ambiguousIdentifier().GetText()),
                    VisualBasic6Parser.PropertyGetStmtContext c => (c.END_PROPERTY(), "Property Get " + c.ambiguousIdentifier().GetText()),
                    VisualBasic6Parser.PropertyLetStmtContext c => (c.END_PROPERTY(), "Property Let " + c.ambiguousIdentifier().GetText()),
                    VisualBasic6Parser.PropertySetStmtContext c => (c.END_PROPERTY(), "Property Set " + c.ambiguousIdentifier().GetText()),
                    VisualBasic6Parser.BlockIfThenElseContext c => (c.END_IF(), "If " + Text(c.ifBlockStmt().ifConditionStmt())),
                    VisualBasic6Parser.DoLoopStmtContext c => (c.LOOP(), Header(c)),
                    VisualBasic6Parser.ForNextStmtContext c => (c.NEXT(), "For " + Text(c.iCS_S_VariableOrProcedureCall())),
                    VisualBasic6Parser.ForEachStmtContext c => (c.NEXT(), "For Each " + c.ambiguousIdentifier(0).GetText()),
                    VisualBasic6Parser.WhileWendStmtContext c => (c.WEND(), "While " + Text(c.valueStmt())),
                    VisualBasic6Parser.SelectCaseStmtContext c => (c.END_SELECT(), "Select Case " + Text(c.valueStmt())),
                    VisualBasic6Parser.WithStmtContext c => (c.END_WITH(), "With " + Text(c.implicitCallStmt_InStmt())),
                    _ => ((ITerminalNode?)null, ""),
                };
                // Recovery can synthesize a closer. Do not annotate a block that has not actually closed.
                if (end is not null && end.Symbol.TokenIndex >= 0)
                {
                    var line = end.Symbol.Line - 1;
                    if (line >= 0 && line < lines.Length)
                        hints.Add(new LspInlayHint(new LspPosition(line, lines[line].Length), "' " + label));
                }
            }
            for (var i = node.ChildCount - 1; i >= 0; i--)
                pending.Push(node.GetChild(i));
        }
        return hints.OrderBy(h => h.Position.Line).ThenBy(h => h.Position.Character).ToList();

        string Text(ParserRuleContext c) => c.Start.StartIndex >= 0 && c.Stop.StopIndex < source.Length
            ? source[c.Start.StartIndex..(c.Stop.StopIndex + 1)].ReplaceLineEndings(" ").Trim()
            : c.GetText();

        string Header(VisualBasic6Parser.DoLoopStmtContext c)
        {
            var separator = c.blockSep(0).Start.StartIndex;
            return separator > c.Start.StartIndex
                ? source[c.Start.StartIndex..separator].ReplaceLineEndings(" ").Trim()
                : "Do";
        }
    }
}

internal sealed record LspInlayHint(LspPosition Position, string Label);
