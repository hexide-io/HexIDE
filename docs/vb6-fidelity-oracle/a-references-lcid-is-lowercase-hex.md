## A reference's LCID is lowercase hex, unpadded (2026-10-27, #2273)

**The question.** A `.vbp` reference is `Reference=*\G{GUID}#major.minor#lcid#path#description`. rdcore's sample of 150
public projects had `#409#` and `#9#` beside the usual `#0#`, which suggests hex. How does vb6.exe write it: hex or
decimal, which case, and is it padded?

**The probe.** VB6 SP6's IDE on the oracle VM's console, whose UI language is en-US, driven by its own menus. Each pass:
- opened a project;
- checked libraries in Project → References;
- saved with File → Save Project.

**The libraries:**
- **A real one:** the Microsoft Windows Installer Object Library, msi.dll, registered under LCID 409.
- **Three made for the probe:** empty type libraries built with `CreateTypeLib2` and registered under LCIDs 9, C09 and
  C0A, then unregistered and deleted afterwards.

| Library, its LCID | How it got into the project | VB6 wrote |
|---|---|---|
| msi.dll, 0x409 (1033, en-US) | checked in References | **`#1.0#409#`** |
| probe library, 0x9 (English, neutral) | checked in References | **`#1.0#9#`** |
| probe library, 0xC0A (3082, es-ES) | written by hand as `#c0a#` | **`#1.0#c0a#`** |
| probe library, 0xC09 (3081, en-AU) | written by hand as `#C09#` | **`#1.0#c09#`** |
| stdole2.tlb, 0 | already in the project | `#2.0#0#` |

- **Hex.** 0x409 is written `409`, not `1033`.
- **Lowercase.** The reference written by hand as `C09` was saved as `c09`. So VB6 parsed the field as hex and wrote
  it again, rather than keeping the text it read.
- **Unpadded.** 9 is `9`, not `0009` or `009`.

**What else the save did.**
- **Every reference's path was rewritten relative to the project's folder,** system files included. The stdole2 line,
  written as `C:\Windows\SysWOW64\stdole2.tlb`, was saved as `..\..\Windows\SysWOW64\stdole2.tlb` from
  `C:\hexoracle-lcid4\w`.
- **A newly checked reference was added after the ones already there:** msi.dll came last in the hand-written project.

**Why two of the four were written by hand.** The References dialog didn't list the libraries registered under C0A or
C09: each was registered, and `LB_FINDSTRING` found no row for either. It did list those under 0, 9 and 409. On this
en-US machine it seems to offer only a library whose LCID is 0, the language-neutral English 9, or the UI's own 409. That
was not separated further. A reference under another LCID, written in the `.vbp` by hand, was kept and written back.

### In HexIDE

`ProjectDeserializer` now reads the LCID as hexadecimal, accepting either case, and `ProjectSerializer` writes
lowercase hexadecimal without padding (#2273). Previously `409` was read as 409 instead of 1033, and `c0a`
was kept only as an unknown verbatim line instead of entering the reference model.
