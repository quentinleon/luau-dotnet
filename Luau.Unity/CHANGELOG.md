# Changelog

All notable package changes are recorded here. The package follows semantic
versioning while in preview; preview releases may make explicitly documented
API or ABI breaks.

## [0.2.0] - 2026-07-19

### Added

- Finite state, execution, decoded-value, module, compiler-queue, importer, and
  capability budgets, with visibly named unbounded opt-ins for trusted work.
- A versioned, bounded persistent-artifact encoding plus bounded, immutable
  managed module maps and bundles.
- Explicit generated object capabilities for `GameObject` and `Transform`.
- Package-local documentation, legal notices, XML IntelliSense, and two
  importable samples.
- Deterministic package archive/content validation and stripped Android
  shipping-plugin validation with separately retained symbols.

### Changed

- Allocating execution, invocation, and resume APIs return a disposable
  `LuauResultScope`; `*Into` APIs retain caller-owned destination semantics.
- Callback reference arguments are borrowed and callback-scoped. Call
  `Retain()` to create an owned reference that may outlive the callback.
- Module maps and trust-domain policy are managed-runtime contracts; Unity
  supplies asset adapters only.
- The native host ABI is revision 2 and uses stale-safe, monotonic non-reused
  opaque references with O(1) validation and explicit callback registration
  identity.
- `.luau` import is length-first, bounded, a single admitted-byte pass, and
  strict UTF-8. First-party artifacts persist stable source identity separately
  from provenance claims.

### Removed

- Implicit ownership through result arrays and legacy duplicate execution
  paths.
- Verification-only smoke types from the product API inventory.

## [0.1.6] - 2026-07-19

- Completed the standalone `Luau.Unity` UPM migration.
- Established the package as the sole Unity product and interop authority.
- Added deterministic managed/native artifact checks and generated-consumer
  validation.

[0.2.0]: https://github.com/nuskey8/luau-dotnet/releases/tag/v0.2.0
[0.1.6]: https://github.com/nuskey8/luau-dotnet/releases/tag/v0.1.6
