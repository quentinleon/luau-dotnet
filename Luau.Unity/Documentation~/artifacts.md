# Persistent artifacts

A persistent artifact is a versioned binary envelope around first-party Luau
bytecode. It carries format version, compiler/native ABI identity, source
identity and hash, payload length and hash, and host-defined provenance data.
Unity precompile uses `unity-asset-guid:<guid>` as the stable source identity;
the configured publisher/provenance ID remains a separate untrusted claim.

The codec checks declared lengths and configured caps before cloning or
allocating. Its span and stream readers reject truncation, trailing bytes,
integer overflow, oversized fields, invalid identity, and corruption with typed
diagnostics. The writer emits one deterministic representation.

Integrity is not trust. Successful parsing proves only that the envelope is
well-formed and internally consistent. It does not prove who created it or that
its capabilities are appropriate for the current game build.

For persistent loading:

1. Compile trusted source with the reviewed toolchain.
2. Create/write an artifact with stable source identity and provenance claims.
3. Authenticate the artifact against a signed manifest or compiled allowlist
   owned by the game build.
4. Configure the root with `LuauBytecodePolicy.RequireValidator` and that
   validator.
5. Load through the verified-artifact API.

An asset GUID or provenance string copied from the artifact is not an
authentication decision. Same-process `LuauCompilerOutput` and persistent
artifacts are deliberately not interchangeable.

Precompiled assets created before the required source-identity field was added
must be reimported. The runtime rejects such legacy serialized payloads instead
of guessing an identity from a diagnostic path or provenance label.
