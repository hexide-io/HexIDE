### Fixed

- **Arguments on a source-class instance reach its default member** (#4606): reads call its Function or Property Get and assignments call its Property Let, including class fields, indexed results and parameterless procedures returning a class. Indexed access to a typed class variable holding Nothing raises error 91; unmeasured default-member kinds remain explicitly refused.
