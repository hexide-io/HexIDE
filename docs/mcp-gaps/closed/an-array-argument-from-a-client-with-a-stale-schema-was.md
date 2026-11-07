# An array argument from a client with a stale schema was refused as "not a long string", with no hint why — **CLOSED** (#2446, 2026-10-28)

**Symptom (2026-10-27, hexide-frm, driving the CommonDialog's two-file Open after #2395 and #2426).** The session had
loaded `answer_next_file_dialog` before #2426 added `paths`, and a client keeps a tool's schema once loaded. With no
`paths` in its copy, it sent the array the caller wrote as a JSON string, and the server refused it: `'paths' must be
array, not a long string. Its input schema names each parameter and its type.` That pointed the caller at the schema,
which was the stale thing, and told it nothing of what had arrived. A model on first contact would retry the same call,
or decide the tool was broken.

**Fixed (#2446).** A string argument that holds, as JSON, the array or object its parameter declares is refused
naming the cause, and the refusal's close says that a retry from the same session sends the same text and a new
session loads the current schema. The value is not taken: that would guess at what the client meant. A long string in
any refusal is shown by its length and start, not as "a long string". Guarded by `McpRestartTests`, which call a real
server over its transport.

**Live, reproducing the cause (2026-10-28, Windows, headless `claude -p` through the hexide tools).** The session
loaded `answer_next_file_dialog` from a batch-85 build, whose schema lists `path` alone, and used it. The IDE was then
swapped for a build of this fix, in the same session, and the call made with `paths` written as an array:

```
'answer_next_file_dialog' refused its arguments, and nothing was done: 'paths' must be array, and arrived as a string
holding one, a string of 43 characters beginning "["C:\\nowhere\\a.txt","C"…: what a client sends while it holds an
older schema of this tool, from before this parameter took an array. Retrying in this session sends the same text: its
client keeps the schema it loaded, which this server has since changed. A new session loads the current schema and
sends the value as it was written.
```

The middle of that reply has since been tidied to `arrived as a string of 43 characters holding one, beginning …`; the
test pins the new wording.
