# Frame selection ownership

The UI session host retains the raw stack frames and a selected frame from its current stop. Selection
validates both the stop and the adapter frame ID; an ID is not an array index, and zero is valid.

Each selection advances an inspection generation before clearing locals. Scopes/variables responses must
match both the live stop and that generation before publication. This also handles selecting A, then B,
then A again: an older A request cannot satisfy the newest selection. Wire requests remain bounded by
existing connection deadlines; obsolete responses are discarded rather than blocking further selection.

Call Stack keeps its row instances until the stack changes. A guarded two-way selection binding forwards
user selection without feeding programmatic selection updates back into the host. The top-frame arrow
continues to identify execution; row selection identifies inspection. Source reveal uses the selected
frame, while execution highlighting and Show Next Statement use the actual top frame.
