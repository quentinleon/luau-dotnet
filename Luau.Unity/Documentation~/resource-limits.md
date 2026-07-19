# Resource limits

Ordinary constructors and Unity defaults are finite. Assigning a replacement
options object replaces the complete policy, so preserve every limit you still
need. Unlimited profiles are visibly named and intended only for trusted work.

## Root limits

`LuauStateOptions.Default` bounds:

- native VM allocation bytes;
- admitted source and bytecode bytes;
- per-string and aggregate decoded managed result bytes;
- live managed object capability registrations;
- managed module dependency depth and retained module-cache results;
- default execution duration, interrupt count, and result count.

Native VM allocation accounting does not include arbitrary allocations made by
managed callbacks or by the host around the VM.

`LuauStateOptions.UnboundedResources` removes its optional resource ceilings
deliberately, but keeps diagnostic decoding finitely bounded and leaves
persistent bytecode policy at `Reject`. Per-operation options may tighten root
limits; they cannot remove them or replace the root scheduler.

## Module-map and bundle limits

`LuauModuleLimits.Default` separately bounds modules per immutable map or
bundle, aggregate admitted source, canonical module-ID bytes, compiled bytecode
per module, and aggregate bundle bytecode. `LuauModuleLimits.UnsafeUnbounded` is
the explicit trusted-content opt-out; root cache count and dependency depth
still come from `LuauStateOptions`.

## Compiler limits

The shared Unity lane has finite per-request source, per-result bytecode,
incomplete request, aggregate queued source, worker, and shutdown limits.
`LuauCompilationLimitException` represents admission exhaustion; compiler
diagnostics, cancellation, and infrastructure failures remain distinct.

## Importer limits

The Editor setting `LuauAssetImportSettings.MaxImportedSourceBytes` defaults to
1 MiB. The importer checks file length before allocating, reads once, validates
strict UTF-8, and compiles/stores the same admitted bytes.

This setting protects authoring import. It does not replace download, archive,
module, compiler-queue, or state source limits for runtime mods.

## Logging and diagnostics

The default Unity `print` binding limits arguments, UTF-8 output bytes, and
messages per second. Diagnostic decoding has a separate finite budget and
truncates only on valid UTF-8 boundaries. Rate-limit any custom logging sink.

Measure representative first-party and hostile content before raising a budget.
Use separate roots when content should not share memory, cache, or capabilities.
