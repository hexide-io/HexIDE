# Carry files a project does not compile, show them, and let one be added

## Why

A VB6 project has always been able to carry files it does not compile — a README, a spec, notes beside the
source. VB6 writes these as `RelatedDoc=`, and writes them as ordinary `Module=` lines whenever its "Add As
Related Document" tickbox is left unticked, which is its default. So the common shape of this on disk is not
the modern key at all; it is a `.md` sitting on a `Module=` line.

HexIDE loaded those into `ProjectDefinition.Modules`, which is the collection the interpreter enumerates, the
save loop renames by extension, and the header writer prepends `Attribute VB_Name` to. The result was
**hexide-io/HexIDE#245**: opening a project and saving it prepended a VB6 module header to the developer's
prose. Not a cosmetic divergence — a silent write to a file they never edited, and by
`docs/serialization-outcomes.md`'s ordering the worst class of outcome there is.

Two other things fell out of the same gap. `Project > Add File...` had no `Command` bound at all and its
toolbar twin was bound to a disabled command (**#247**), so there was no way to get a non-VB6 file into a
project except by hand-editing the project file. And a carried file had nowhere to open: the code editor
assumes a form or a module, a VB6 language server, and a faithfulness gate, none of which describe a text
file.

The wider reason to do this now is that the language layer has just been widened to route a document to every
server claiming its language, keyed on the document's *extension* rather than its role in the project. That
design is only useful if a project can hold a document whose role is "carried". This change supplies the
member kind that one presupposes.

## What Changes

- **A carried file is a separate kind of project member, not a module.** It lives in its own collection.
  This is the whole safety argument and it is structural rather than defensive: everything that could damage
  a non-code file iterates `Modules`, so being absent from that collection makes the damage unreachable,
  rather than requiring a guard at each site that somebody has to remember to add.

- **A non-code file found on a code line is reclassified on read, and its line is preserved verbatim.**
  Reclassifying is an inference about what the developer meant. Rewriting their project file on the strength
  of an inference is a larger liberty than declining to, so the line changes only when membership actually
  changes.

- **A carried file opens in a plain text editor** with highlighting chosen from its own extension, and no
  designer, procedure dropdowns, breakpoints, faithfulness gate or companion binary — because none of those
  describe a text file.

- **Add File adopts a file that already exists**, classified by extension. VB6 source joins as a form or
  module; everything else joins as a carried file. The conservative direction is deliberate: mis-filing a
  VB6 file as carried costs a designer the developer adds again, while mis-filing prose as source is #245.

- **A path inside a project file is treated as a Windows path on every host.** Discovered by CI rather than
  by design (see below), and specified here because it is exactly the kind of invariant that regresses
  silently.

## What This Overturned

Two assumptions, both recorded because the next reader will make them again.

**"A fifth `ModuleKind` is the cheap way to model this."** It is cheaper to write and it re-introduces the
defect: a `ModuleKind` value still lives in `Modules`, so every consumer of that collection needs a new
guard. The separate collection costs more code and removes a class of bug instead of adding a case to it.

**"`System.IO.Path` is how you handle a path."** Not for these strings. A project file is a Windows-native
format whose paths are backslash-separated whatever machine reads it, while `System.IO.Path` answers about
the *host* filesystem. On Linux a backslash is an ordinary filename character, so `GetFileName` on a carried
file's path returns the whole string and the document gets named after its own directory; on write,
`GetRelativePath` yields forward slashes that then go into a file which must contain backslashes.

Worth recording how this surfaced, because it is the more useful half. The read-side fault was introduced
here; **the write-side fault was already present at all three emission sites, forms and modules included**,
and had simply never been exercised because no test put a subdirectory through the write path. Worse, three
round-trip tests were *certifying* it: their expectations were built with `Path.Combine`, so on Linux CI they
asserted that HexIDE writes forward slashes into a VB6 project file. A test that passes for the wrong reason
is worse than no test, because it reads as cover. One of the three is named `..._PreservedVerbatim`, and
`Path.Combine` is the opposite of verbatim.

Fixing it where CI was looking was not the end of it. A sweep of the layer found **seven further sites**, and
the shape of the set is the point: group files carried the entire defect in both directions and had never
been touched, two member keys derive a name from a raw path when the line carries none, the extension test
misreads a directory that contains a dot, the object browser labels every library with a whole absolute
path, the standalone runner cannot find a member in a subdirectory, and a corpus helper normalises the path
on one line while taking the name unnormalised on the line above. Not one was covered by a test that ran a
subdirectory through it, which is exactly why all seven were green. The rule is therefore specified here
rather than left as a fixed bug: what made this expensive was that it was invisible, not that it was subtle.

## What This Does Not Do

- **No preview pane, and no rendered view of any carried format.** Text in, text out. A preview re-opens the
  designer question for a member kind that exists precisely because it has no designer half.
- **No virtual folders for out-of-cone carried files.** Files outside the project directory already surface
  at the root with their location shown. Grouping them under developer-defined folders needs a custom
  project-file section and is its own change.
- **No adoption of file kinds HexIDE does not model.** ActiveX Document and designer files are VB6 source,
  but nothing here parses them, so Add File treats them as carried rather than promising a designer that
  does not exist.
- **The interpreter never sees a carried file.** That is the point of the separate collection.
