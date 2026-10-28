# A whole-window `dump_visual_tree` overflows the caller's output limit — **CLOSED** (#699, 2026-10-20)

> **Fixed.** `dump_visual_tree` takes `match`: text to find instead of the tree. It returns the nodes whose Name,
> AutomationId, ClassName, ControlType or `typeNameAsName` contains it, ignoring case, or whose DataContext type does
> where that context starts, each with its path and without its subtree, up to 50, and a `note` that counts them and
> the branches `maxDepth` cut off unsearched. Every node in the tree is searched, interactive or not; a document in a
> tab that is not selected is not in the tree, so is not found. Measured with a cold agent on a Debug build, an IDE
> with one code window and one designer open:
>
> - `dump_visual_tree {"match":"TextEditor"}` → `1 node matches 'TextEditor'.`, the editor's path, with its class and
>   `CodeEditorViewModel`, in a reply of a few hundred characters. The Immediate window's editor matches too when it
>   is on screen.
> - `{"match":"PropertiesTool"}` → `1 node matches 'PropertiesTool'.`, the Properties window's view.
> - `{"match":"Properties"}` → 9 nodes: two toolbar buttons and the Properties window's parts. Before a node answered
>   by its DataContext type only where that context starts, it was 22, most of them scroll bar parts that inherited
>   `PropertiesToolViewModel`. Where a context starts is decided by identity, so a nested tree item's own context of
>   its parent's type, as in Locals, still answers.
>
> The whole-window dump itself is unchanged, and can still overflow; the tool's description now offers `match` beside
> `root`. Not done: a bound on the walk's cost, which grows with the square of a parent's same-type children (5,000
> sibling buttons took 8.7 s, measured by the reviewer), and a count of subtrees a failing control dropped from the
> walk; both were so before. Guarded by `FindTests`.


**Symptom (2026-09-25).** `dump_visual_tree {"maxDepth":12}` with an IDE showing three document tabs and
the usual tool windows returned 58,264 characters. That is over the client's limit, so it was written to a
file instead of being returned. The only way to find the code editor's path was to parse that file outside
the tool. The description does say to scope a large dump with `root`, but a first-time caller does not yet
know any path to scope it with.

**Workaround.** Since #953, walk down instead: dump the whole window with a small `maxDepth` (3 or 4), then dump
again with a node's path as `root` wherever it carries `"truncated": true`. Each step stays small, and a cut-off
subtree is marked rather than silently dropped. Dumping once to a file and extracting paths with a script still
works when a question needs the whole tree at once.

**Suggested fix.** A filter on class name, view-model type or name, returning matching nodes with their
paths and no subtrees. This is the question a first call usually asks: where is the code editor? Filed as
[#699](https://github.com/hexide-io/HexIDE/issues/699).
