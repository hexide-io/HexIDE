## Why
External sessions currently require relaunching to change breakpoints and expose verification only as raw output.
This slice connects editor breakpoint controls to the adapter and makes verification visible. Tracks #1176.

## What Changes
- Seed a session-only external set from launch JSON, leaving saved interpreter marks and JSON unchanged.
- Replace complete source sets asynchronously, coalescing rapid edits and preserving removal failures.
- Render verification and relocation in the gutter with localized details; apply matching adapter events.

## Scope
Gutter/F9 and Clear All for external source breakpoints. Persistence, conditions, hit counts and specialized
MCP routing remain separate work.
