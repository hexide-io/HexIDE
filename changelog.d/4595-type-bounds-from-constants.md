### Fixed

- **Array fields inside a Type accept constant bounds** (#4595). Preceding Consts, Enum members and
  expressions of them now size fields in standard modules and classes, including bounds from another
  module's Public constants or Enum members. A later Const or a variable reports "Constant expression required".
