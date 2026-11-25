### Added

- **A component class can give the form designer its title-bar icon and a drawn control's starting values** (#5633). `IDesignerCaption` answers for a class whose instances are a designer's root window, and `IDesignerDrawnDefaults` sets what a control starts with when it's drawn. The Basic provider's Form and RichTextBox answer through them, so the designer no longer reaches into the provider for either. A class that throws, or gives no caption, is logged, and the designer carries on as though it hadn't asked.
