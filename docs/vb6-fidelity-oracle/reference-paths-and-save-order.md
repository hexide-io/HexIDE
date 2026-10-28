# Reference paths and project save order (#2274, 2026-10-27)

The measurements supplied in #2274 were made by hexide-int with the SP6 IDE. The
edge-case probe opened a hand-written project, added a reference through Project →
References to dirty it, then used File → Save Project. Three type libraries were
built with CreateTypeLib2 and registered for the probe. The temporary registrations,
share and drive were removed afterwards. Provenance: #2273 comment 6434980455,
#2274's measurement comment (PR #2279, commit 75a6ad63), and the supervisor's final
scope quoting comment 6435238359 on PR #2279's docs/oracle-tier3 branch.

| Input | Saved path |
|---|---|
| System library on the project's drive, `C:\Windows\…\msi.dll` | Relative, `..\..\Windows\…\msi.dll` |
| Registered library on a subst drive, `X:\HexIde2274D.tlb` | Unchanged absolute path |
| Registered UNC library, `\\localhost\hexoracle2274\HexIde2274U.tlb` | Unchanged UNC path |
| Empty path for a library registered under `C:\hexoracle-2274\tlb` | `..\..\hexoracle-2274\tlb\HexIde2274E.tlb` |
| Empty path with an unregistered GUID, shown as MISSING | Still empty |

VB6 takes a registered library's path from the registry, then makes it relative if
it is on the project's drive. The other-drive and UNC probes used the same path in
the file and registry, so they do not distinguish the two sources. A MISSING
reference keeps its supplied path. The final supervisor scope asks HexIDE to
relativise supplied paths on the project's drive, retain other-drive and UNC paths,
and retain empty paths because registry resolution is unavailable. A moved
registration and a registered empty path therefore remain divergences.

The initial probe went through Project → References. Its save grouped Type, all
references in their existing order, then read Object lines, then members. The
2026-10-28 SP6 console probes (hexide-int, #2274 comments; #2279 at 66ec3741)
isolated the trigger: pressing OK in References, even unchanged, regroups the next
save. A plain Save Project and Save Project As without edits preserve read order.
Synthetic projects kept an Object after the members, an Object between members,
and a Reference after a Designer or after members. Data Project itself could not
load on the guest because its component registration and designer were unavailable.
The synthetic probes answer its ordering case without assuming a template's order.

The Components dialog does not regroup: its added Object follows the last Form
(#1103). Whether other edits such as Project Properties regroup remains unmeasured.
HexIDE preserves read slots unless References was accepted in that project's session.
Registry-derived library changes and VB6's added Retained=0 remain outside this fix.
