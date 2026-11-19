### Fixed

- **For Each walks control arrays** (#4716) in index order, using the indices present when the loop starts and looking up each control when reached. An unloaded element raises error 340; On Error Resume Next records it and continues.
