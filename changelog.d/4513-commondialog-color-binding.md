### Fixed

- **CommonDialog Color follows its binding** (#4513). Early-bound reads return a Long; Object and Variant reads retain the unsigned Automation Long. Measured conversions, unsupported-type errors and assignment errors now match VB6. CallByName remains tracked by #4520.
