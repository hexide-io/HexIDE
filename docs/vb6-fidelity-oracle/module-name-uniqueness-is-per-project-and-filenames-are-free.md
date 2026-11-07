## Module-name uniqueness is per-project, and filenames are free (2026-09-07)

Measured because it decides whether HexIDE's `vb6://module/{Name}` document identity can collide —
that scheme keys on the module *name*, so it is sound exactly to the extent VB6 guarantees that name
is unique. Three `/make` probes, real `vb6.exe`.

| Probe | Shape | Exit | Result |
|---|---|---|---|
| A | `Folder1\Module1.bas` (`VB_Name="ModA"`) + `Folder2\Module1.bas` (`VB_Name="ModB"`), one `.vbp` | **0** | `Build of 'A.exe' succeeded.` |
| B | `Folder1\Module1.bas` + `Folder2\Module1.bas`, **both** `VB_Name="Module1"`, one `.vbp` | **1** | `Name conflicts with existing module, project, or object library` |
| C | Two separate `.vbp`s, **each** with its own `Module1`, gathered by a `.vbg` | **0**, **0** | both built |

**The rule: a module name must be unique within a project; a *filename* need not be.** Two files both
called `Module1.bas`, in different folders, are perfectly legal in one project as long as their
`VB_Name` attributes differ — VB6 binds on the name in `Attribute VB_Name`, not on the file it came
from. The `.vbp` carries both independently (`Module=<Name>; <relative path>`), which is what makes
the pair separable at all.

**The obvious guess was that the *path* disambiguates, and it does not — the reverse is true.** The
path is free and the name is constrained. So the collision to worry about is never "two files with
the same name in different folders"; it is "two modules with the same name", which VB6 refuses
outright inside one project.

**And a project group lifts that refusal.** Probe C is the case that matters: a `.vbg` is a container
of independently-compiled projects, each with its own flat namespace, so `Module1` in two members is
ordinary rather than exotic. Any identity that names a module without naming its project is therefore
ambiguous the moment a group is open — see hexide-io/HexIDE#261 and #273.

Not tested: whether the VB6 IDE *itself* refuses to add a second same-named module interactively, as
opposed to `/make` refusing to compile one. The compile-time refusal is what binds either way.

---

