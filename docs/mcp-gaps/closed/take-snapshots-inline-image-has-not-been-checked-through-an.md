# `take_snapshot`'s `inline` image has not been checked through an MCP client, over a tunnel or locally — **CLOSED** (#1330, 2026-10-25)

**Symptom.** #957 added `inline` and closed. Its archived entry records one measurement, taken through the server's
HTTP endpoint rather than a client's tool call ("a 3840×2054 window came back as a 1568×838 PNG of 75 KB"), and
says "Not yet run through Claude Code over a tunnel". A raw request shows nothing about what a client does with the
image block, and a client on another machine receiving the image is what #957 was filed for. Covered headless by
`InlineSnapshotTests` only.

**Workaround.** Without `inline`, copy the file from the IDE's machine by hand.

**Suggested fix.** No code change is expected. Call `take_snapshot {"inline":true}` through the hexide tools, on the
IDE's machine and from another one through a tunnel, and record what the client shows. [#1330](https://github.com/hexide-io/HexIDE/issues/1330).

**Closed (#1330).** Checked through Claude Code's hexide tools, from Windows, on a Debug build of main at beada72.

- **On an openSUSE Leap 16 VM through an SSH tunnel:** `take_snapshot {"inline": true}` returned its JSON, with
  `inlineWidth` 1440 and `inlineHeight` 756, and one `image/png` block the client showed. Read from the client's own
  stream, the block is a 1440×756 PNG.
- **On the IDE's own machine:** the same call returned a block of 1418×671, its `inlineWidth` and `inlineHeight`.
- **Without `inline`:** `take_snapshot {}` had no image either way.

The measurements are under #957's entry below.
