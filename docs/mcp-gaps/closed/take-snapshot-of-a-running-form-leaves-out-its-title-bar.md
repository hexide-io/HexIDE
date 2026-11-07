# `take_snapshot` of a running form leaves out its title bar without saying so

**Symptom (2026-10-26, #534's first-contact baseline, Windows).** The task was "Make Form1's title bar say Hello when
the program starts, run it, and confirm it does". All three models took a snapshot to confirm it:
- `take_snapshot {"window":"Form1"}` answered `{"path":"…","activeDialog":"Hello","width":456,"height":301,…}`.
- The image is the form's client area, grey and empty, with no title bar.
- Haiku 5.5 reported the PNG "came back blank". Sonnet 5.5 and Opus 5.5 worked out that only the client area was
  captured, and took `activeDialog` as the confirmation.

Neither the description nor the reply says the frame is left out, and the title of a running form, which is no dialog,
comes back in a field named `activeDialog`.

**Workaround.** Read the title from `activeDialog`, or from the root `name` of `dump_visual_tree {"window":"auto",
"maxDepth":1}`.

**Suggested fix.** Say in the description and the reply that the image is the client area, and name the field after
what it holds: the captured window's title. [#2078](https://github.com/hexide-io/HexIDE/issues/2078).

**Fixed (#2078).** The description and reply now identify the capture as the client area, without its title bar
or frame. Read `title` for the captured window's actual title, including the IDE's or an empty caption;
`activeDialog` remains as a compatibility field with its previous meaning. Open popups are still included
unless `note` says otherwise. Native-frame capture remains the separate part 2 gap in `docs/mcp-server-gaps.md` (#1595).
