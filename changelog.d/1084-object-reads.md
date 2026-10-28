### Fixed

- **CommonDialog runtime property reads follow the measured object model** (#1084). Reading its write-only
  Action, including assigning the control as a value, raises 394. Left and Top raise 393 on reads and 382 on
  writes; absent extender properties raise 438 on reads. FontName ignores a missing face and preserves an
  accepted name's spelling.
