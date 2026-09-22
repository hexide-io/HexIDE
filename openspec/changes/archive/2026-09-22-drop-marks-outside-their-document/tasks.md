# Tasks

## 1. Load

- [x] 1.1 `UserSidecarService.LoadAsync` passes each document's bookmarks (from 0) and breakpoints (from 1)
  through `WithinDocument`, which keeps the lines the document has and logs the rest.
- [x] 1.2 Lines are counted in the model's code, as `set_breakpoints` and `set_bookmarks` count them when no
  editor is open.

## 2. Tests

- [x] 2.1 `MarksOnLinesTheDocumentDoesNotHaveAreDroppedAtLoad_AndNotWrittenBack`: bookmarks on
  `-1, 0, 11, 12, 99` of a twelve-line form load as `0, 11`; breakpoints on `-1, 0, 1, 12, 13, 99` of a
  twelve-line module load as `1, 12`; the next save writes only those.
- [x] 2.2 The existing sidecar fixtures give their documents twelve lines, because their marks were on lines
  empty documents do not have.
