## Why

HexIDE is preparing to move its interpreter and run-time forms engine out to an optional provider (#3411). The preparation
starts with seams: the IDE reaches the parts that would move through interfaces, so that another implementation can stand
behind them. Twenty-one issues' worth of those seams have merged, one at a time, and `openspec/` recorded none of them. Their contracts
were written down only in issue threads and doc comments, and nothing told the next change what it had to keep (#4048,
#4049).

## What Changes

- Add the capability `project-file-format`: how the IDE reads and renders a project's files, asks a project's type what
  it offers, reads and writes a document's read-only text, colours parentheses and greys conditional code, and reaches
  the form designer's control classes, their pictures and the in-box control registry. Each requirement cites the issue
  that made it so (#3564, #3598, #3731, #3624, #3668, #3680, #3732, #3694, #3716, #3572, #3559, #3482, #3505, #3601).
- Add the capability `run-engine`: how F5 runs a project through a run engine, what a project no engine runs offers
  instead, where the debugger's front-end contract lives, and which run-time facts the IDE reads from Core rather than
  from the interpreter (#3412, #3186, #3499, #3498, #3670, #3566, #3567).
- Where an existing spec already holds a requirement, cite it rather than restate it: the read-only text the code window
  protects (`code-editor`), an add-in's project type (`contributed-projects`), the automation tools when no engine runs
  a project (`hexide-mcp-server`, #3514), and the IDE's own prompts (`localization`, #3513).

## Impact

- New specs: `project-file-format`, `run-engine`.
- No behaviour changes. Each requirement was read from the code at `main` on 2026-11-16 (`a3f9d9c48`).
- The decisions #3411 still lists as open aren't recorded here. Nor is the README, the website or another leading doc
  changed, which #3411 keeps as they are for now.
