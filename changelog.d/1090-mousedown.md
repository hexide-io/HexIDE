### Fixed

- **SSTab MouseDown sees the old tab** (#1090, #2358). Caption presses switch pages after the
  MouseDown handler finishes, preserving MouseDown, Click(PreviousTab), MouseUp order even when
  the interpreter yields.
