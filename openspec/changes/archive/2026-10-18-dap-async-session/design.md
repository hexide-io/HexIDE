# Session boundary

The session owns its connection and optionally process. A single event reader applies state under a lock,
publishing immutable snapshots through a bounded channel. Consumers dispatch to UI themselves. Startup sends
launch without awaiting its response until after initialized and configuration, accommodating deferred launch
responses. ConfigurationDone is capability gated and advertised default exception filters are selected.

Stop identities combine a session GUID and generation. Resume invalidates inspections before sending;
responses never perform a second running transition. Definitive refusal creates a fresh stop; uncertain
timeout/cancellation ends the owned session. Inspectors validate before and after awaiting; UI consumers must
also validate on application. Raw response JSON preserves source and remote reference information.

Ended describes the program, not process exit. Disconnect sends the protocol request and always releases
ownership. Disposal is the fallback. Process completion remains separately inspectable. A single global
stopped state targets the first standard-module workflow; multi-thread state, evaluation, dynamic capabilities,
live breakpoint edits and IDE routing are outside this slice.
