## A project group's file, as VB6 writes it (2026-11-14, #579, design-time, IDE, SP6)

**The question.** Where does VB6 put a group's startup project in the `.vbg` it saves: on `StartupProject=` alone, or
on a `Project=` line as well, and in what order? And how does it spell the header? HexIDE's reader loads projects
only from `Project=` lines (#579), and its writer lists the startup project on both lines.

**The probe.** VB6 SP6 (6.00.9782), in its IDE, on the Win10 oracle guest, driven by keystrokes and clicks from the
guest's console agent. There were two Standard EXE projects, each with one module: `Alpha` in `A\A.vbp` and `Beta` in
`B\B.vbp`.
1. Open `A\A.vbp`, then File > Add Project..., Existing, `B\B.vbp`. That makes Alpha, the first project added, the
   startup project, shown in bold in the Project Explorer.
2. File > Save Project Group As..., `G1.vbg`, in the folder holding `A` and `B`.
3. In the Project Explorer, right-click Beta and choose Set as Start Up, then File > Save Project Group.

The bytes written, with every line ending in CR LF:

| After | `G1.vbg` |
|---|---|
| step 2 (startup Alpha, added first) | `VBGROUP 5.0` / `StartupProject=A\A.vbp` / `Project=B\B.vbp` (54 bytes) |
| step 3 (startup Beta, added second) | `VBGROUP 5.0` / `Project=A\A.vbp` / `StartupProject=B\B.vbp` |

Neither save touched `A.vbp` or `B.vbp`, which hadn't changed.

**The answer.**
- **The startup project is written once, on `StartupProject=`, and has no `Project=` line.**
- **That line keeps the project's place in the group.** It's first when the startup project is the first project
  and second when it's the second, so changing the startup project swaps the two keys and leaves the order alone.
- **The header is `VBGROUP 5.0`,** not `5.00`.
- **Paths are relative to the group's folder,** with backslashes.

**VB6 reading HexIDE's format.** This is the file HexIDE's `GroupSerializer` writes for the same group:

```
VBGROUP 5.00
StartupProject=B\B.vbp
Project=A\A.vbp
Project=B\B.vbp
```

Opened in VB6's IDE, it stopped at a dialog: "The file 'C:\…\B\B.vbp' is already open.--Continue Loading Project
Group?", with Yes, No and Help. Yes gave a group of both projects, with Beta as the startup project. So VB6 accepts the
`5.00` header, but it reads the second listing of the startup project as an attempt to open it twice.

**Earlier rows this agrees with (#746, 6.00.8176, the original release).** A hand-written `VBGROUP 5.0` group holding
`StartupProject=probe1.vbp` and `Project=probe2.vbp` opened with both projects, and one holding only `StartupProject=`
opened with that one project. So VB6 reads `StartupProject=` as a member of the group, as it writes it.

**Not measured:** a group of three or more, a project in the group's own folder or above it, and what VB6 does with a
line it doesn't know (#2757).
