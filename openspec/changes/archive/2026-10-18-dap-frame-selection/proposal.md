# Inspect external caller frames

The external Call Stack currently lists callers but only the top frame supplies Locals and source reveal.
Selecting a frame will inspect its locals and reveal its loaded source without moving the execution point.

Scope: one stopped thread, selection scoped to its current stop, asynchronous scopes/variables with stale
result rejection. Each new stop selects the top frame. Execution highlighting and Show Next Statement
continue to identify the actual top frame. Evaluation and thread selection remain separate work.

Tracked by #1172; part of #696.
