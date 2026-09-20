# Native mod upgrade fixtures

`appearance-1.1.0.scmod` is the exact former standalone CS player appearance release.
SHA-256: `06b95bf13c15e20b6c687d48597bfaa3f94ed33c7e4f5e414263c81b6566c1e2`.
It contains the project-owned adapter and descriptors, with no framework or model binaries.
`TacticalLoadCheck` uses it to check coexistence without double registration and to resolve
the original component type identity against integrated tactical 1.2.1.

This is protected compatibility test input, not a current installation package. Do not rebuild
or replace it with current binaries. The end-user output directory only carries current releases.
