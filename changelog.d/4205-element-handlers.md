### Fixed

- **Control-array event procedures carry their Index parameter** (#4205). Both the designer's double-click and the code window's procedure list write `Index As Integer` before the event's own arguments. When a form starts, mismatched handler counts or an element's first parameter stop execution with VB6's compile error, located at the handler declaration; renamed parameters, Integer suffixes and DefInt remain accepted.
