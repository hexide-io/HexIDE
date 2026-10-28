# Keyboard `type_text` counted a prompt-raising key as typed — **CLOSED** (#2035, 2026-10-26)

**Symptom (2026-10-26, Windows, #1975's live check).** Keyboard `type_text` of `xyz` into the code window
while running replied `typed 1 of 3 key(s)` and told the caller to answer the reset prompt, then send the
rest. After Cancel, `get_file_content` showed the line unchanged and `hasUnsavedChanges: false`: the `x`
had not landed. The brief does not record the full call's target arguments.

**Workaround (before the fix).** Answer the prompt, then read the text before choosing what to resend.

**Fixed (#2035).** The stop reply identifies the key sent and the dialog it raised, warns that the key
may not have landed, says nothing after it was sent, and directs the caller to answer and check the text
before deciding what to send next. It makes no claim about what each answer does to an arbitrary edit.
`TypeKeysTests` covers dialogs over the typing window, another window, and in a window of their own.
Live verification of the revised reply remains for the supervising session.
