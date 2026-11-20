### Fixed

- **`With New` creates an object for the block** (#4599), using the same creation path as a `New` expression. The object is released at `End With`, unless another reference keeps it alive.
