using System;

namespace HexIDE.IDE;

// Kept distinct so the IDE can translate its own refusal without translating a program's Err.Description (#5313).
internal sealed class MDIFormRunRefusal() : NotImplementedException("Running an MDI form is not implemented (#5313).");
