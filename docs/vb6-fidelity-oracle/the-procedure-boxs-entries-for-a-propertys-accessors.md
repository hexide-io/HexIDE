## The Procedure box's entries for a property's accessors (2026-11-18, #4783, design-time, IDE, SP6)

**The question.** A class's Property Get, Let and Set share one name. RDCore's server sends all three as symbols named
`Value`, so HexIDE's Procedure box listed `Value` three times, and choosing any of them went to the Get (#4783). How
does VB6's own Procedure box tell the accessors apart, and in what order does it list a module's procedures?

**The probe.** SP6's IDE, VB6.EXE 6.00.9782, in the guest's console session through the agent. `Project1.vbp` is an
ActiveX DLL project holding one class, `Class1`, read from its file. `Class1` declares, in this order:
- `Sub Zed`;
- `Property Get Value`, `Property Let Value`, `Property Set Value`;
- `Function Alpha`;
- `Property Get Beta`, with no Let or Set;
- `Property Set Gamma`, `Property Let Gamma`, `Property Get Gamma`, in that order;
- `Sub Class_Initialize`.

The steps:
1. The class's code window was opened from the Project window, with Ctrl+R, End and F7.
2. The Procedure box was dropped down with the real pointer. It holds only `(Declarations)` until then.
3. Its entries were read with `CB_GETLBTEXT`, and a screenshot of the open list showed the same entries.

**The Procedure box under `(General)`, all 10 entries:**

```text
(Declarations)
Alpha
Beta [PropertyGet]
Gamma [PropertyGet]
Gamma [PropertyLet]
Gamma [PropertySet]
Value [PropertyGet]
Value [PropertyLet]
Value [PropertySet]
Zed
```

**Readings:**
- **An accessor** is its name, a space, then `[PropertyGet]`, `[PropertyLet]` or `[PropertySet]`, with no space
  inside the brackets. A property with only a Get is marked too (`Beta`).
- **A Sub or a Function** is its bare name.
- **The order** is `(Declarations)` first, then by name, whatever the declaration order (`Zed` was declared first). A
  property's accessors come Get, Let, Set, whatever order they're declared in (`Gamma`).
- **`Class_Initialize` isn't under `(General)`.** It belongs to the Object box's `Class`, which this probe didn't read.
- **It agrees with** `Rate [PropertyLet]`, seen once in VB6's Procedure box in "Member attributes: where VB6 attaches
  them, and what its IDE writes".

**Not measured:**
- the Object box's entries, and the event procedures listed under `Class` or a form's objects;
- how names that differ only in case, or that start with an underscore, sort;
- the release build. Only SP6 was read.

**In HexIDE (#4783).** Under `(General)`, a Property symbol whose `detail` is `Property Get`, `Property Let` or
`Property Set` is listed as above. The bundled server's ` (Get)`, ` (Let)` and ` (Set)` names are listed the same way.
The list is sorted as measured, and each entry goes to its own accessor.
