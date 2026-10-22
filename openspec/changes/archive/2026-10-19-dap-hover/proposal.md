## Why
External debugging now supports Locals, Immediate and Watches, but hovering an identifier still cannot inspect its external value.

## What Changes
Use the existing editor data tip to evaluate identifiers in the selected external frame with DAP hover context, gated by supportsEvaluateForHovers. Suppress failed and obsolete results. Keep interpreter and LSP behavior outside external sessions.

## Scope
One UI slice for #1186 under #696. Identifier-only tips retain the existing dwell; expression selection, expandable tips, dynamic capabilities and MCP command routing are separate work.
