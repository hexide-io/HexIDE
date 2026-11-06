# `take_snapshot` returns only a path on the IDE's machine, so a client attached from elsewhere cannot see the image

**Symptom.** Driving a Debug build of b15c7cf in an openSUSE Leap 16 VM from a Windows host, through an SSH tunnel
to the VM's MCP port (2026-10-13):

```
take_snapshot {}
→ {"path":"/tmp/hexide_snapshot_20261013-004254-281_1.png","width":1920,"height":1011,"scale":1}
```

The path is in the VM's `/tmp`, which the client cannot open. The description says the tool "returns the file
path so the caller can read the image", which holds only for a caller on the same machine. The server answers
loopback only, so a tunnel is how another machine reaches it. Not Linux-specific: any remote attach has it.

**Workaround.** Copy the file across by hand; every snapshot in that run was fetched with `scp`.

**Suggested fix.** An optional `inline` parameter, default false, that returns the image as MCP image content
(`image/png`) beside the same JSON. Keep path-only as the default: an inline image costs every local caller that
does not need it. [#957](https://github.com/hexide-io/HexIDE/issues/957).

> **Fixed.** `take_snapshot` takes `inline`, default false. Passed true, the reply carries the image itself as MCP
> image content (`image/png`) after its JSON, which keeps its shape and its path. The copy is encoded by the tool from
> the image in hand and attached by a call-tool filter, as `OpenDialogNote`'s note is, so nothing is read back from a
> file another local user could swap. It is fitted to 1568 pixels on its longest side, since a model sees an image at
> about that size, a request holding many images refuses one over 2000 pixels, and a 4K PNG is several times the bytes;
> `inlineWidth`, `inlineHeight` and `inlineScale` describe it, so `boundingRect × inlineScale` finds a control in it.
> Path-only stays the default: an image costs a few thousand tokens. Measured on Windows through the server's HTTP
> endpoint: a 3840×2054 window came back as a 1568×838 PNG of 75 KB, and a call without `inline` has no image block.
> Guarded by `InlineSnapshotTests`. Run through Claude Code since, over a tunnel and locally (#1330, below).
>
> **Live (#1330, 2026-10-25), over a tunnel.** From a Claude Code session on Windows, through an SSH tunnel to a
> Debug build of main at beada72 on an openSUSE Leap 16 VM:
>
> - `take_snapshot {"inline": true}` returned its JSON, with `inlineWidth` 1440, `inlineHeight` 756 and
>   `inlineScale` 1, and one `image/png` block, which the client showed.
> - That block, read from the client's own stream, is a PNG of 1440×756.
> - `take_snapshot {}` returned the JSON alone, with no `inline*` fields and no image.
>
> **Live (#1330, 2026-10-25), on the IDE's machine.** From a Claude Code session on Windows, against a Debug build of
> main at beada72 on the same machine:
>
> - A 1418×671 window: `take_snapshot {"inline": true}` returned `inlineWidth` 1418, `inlineHeight` 671 and
>   `inlineScale` 1.5, and one `image/png` block the client showed. The block is a PNG of 1418×671.
> - A 1920×974 window, in another session where the call was sent as `{"inline":"true"}` and the client turned it into
>   the boolean: it returned `inlineWidth` 1568, `inlineHeight` 795 and `inlineScale` 1.225, fitted to 1568 on the
>   longest side as above, followed by an image block. That run did not measure the block.
> - `take_snapshot {}` again had no image.
