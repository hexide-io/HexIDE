# Demos

A gallery of small, self-running VB6 demoscene intros — each built end-to-end by driving the live
HexIDE IDE entirely through its MCP automation tools (the `demo-hexide-mcp` skill), then compiled
with the real VB6 compiler and verified by screenshot + resize.

Every **verified** demo is archived here in its own subfolder with its full VB6 source, the
pre-compiled `.exe`, a screenshot, and a README. They are meant to be kept — each is a working
example of what MCP-driven HexIDE automation can produce.

## Convention

Each demo lives in `demo/<jaunty-name>/` and contains:

- `<Name>.vbp` + `.frm`/`.cls`/`.bas` — the complete, self-contained VB6 source
- `<Name>.exe` — the pre-built binary (committed; just run it)
- `screenshot.png` — the effect running
- `README.md` — what it is and how it works

Each demo gets a fresh, random, jaunty project name (never "Project1") so its build never collides
with another's. To rebuild any of them you need VB6 installed (`VB98\VB6.EXE`):
`VB6.EXE /make <Name>.vbp`.

## Gallery

| Demo | Effect |
|------|--------|
| [neon-aurora](neon-aurora/) | A six-part Amiga-style intro — raster plasma, copper bars, 3D starfield, glenz vector, vector balls and a sine-scroller, with real MIDI chiptune music |
| [wild-ecstasy](wild-ecstasy/) | Big wavy text — "HEXIDE" rippling on per-letter sine waves with colour cycling |
| [neon-vertigo](neon-vertigo/) | A rotating 3D sphere of 130 glowing vector balls — Fibonacci-distributed, spun on two axes, depth-shaded magenta→blue and painted back-to-front |
