# Execution and trust lanes

Source, same-process compiler output, and persistent bytecode are separate
capabilities. Converting one to a byte array never grants another capability.

## Ordinary Unity assets

Use `state.ExecuteAsync(LuauAsset, cancellationToken)`. Source assets enter the
package-owned bounded compiler queue. Compilation happens off the VM owner
thread; installation and execution resume on the state's configured scheduler.
Verified asset artifacts bypass compilation but still pass the state's artifact
policy and validator.

`state.Execute(LuauAsset)` is a synchronous convenience for trusted tooling and
small editor workflows. It compiles source on its caller and is not the modding
path.

## Untrusted streamed source

Admit UTF-8 bytes under a host-owned download/package limit, then use
`LuauUnity.CompileAsync`. The shared lane bounds request size, queued request
count, aggregate queued bytes, compiler output, workers, and shutdown progress.
Execute only a successful `LuauCompilerOutput` in the same process.

Create a separate root VM for each mutually untrusted mod. A root is one trust
domain: its children share native allocation policy, host capability registry,
module cache, cancellation machinery, and root lifetime.

## Same-process compiler output

`LuauCompilerOutput` is opaque and compiler-issued. It is valid only inside the
process that created it. Its bytecode bytes are not a persistent load token.

Raw `LuauCompiler.Compile` is a synchronous expert API for trusted tooling. It
does not use the shared queue and cannot preempt a native compile.

## Persistent first-party bytecode

Persistent bytecode uses the bounded artifact codec and records exact compiler,
native ABI, source, payload, integrity, and provenance claims. Parsing validates
format and integrity only. Runtime loading additionally requires
`LuauBytecodePolicy.RequireValidator` and a game-owned validator that
authenticates the artifact against trusted build data.

Never accept arbitrary bytecode from mods. Do not trust an embedded label, asset
GUID, or hash merely because the artifact contains it.

## Cancellation and failure order

Queued compilation cancellation removes its reservation. A native compile that
has started cannot be interrupted safely; its output is discarded after return.
Execution cancellation is cooperative at VM interrupt points. Operation failure
precedence is hard stop, managed callback failure, then allocator/native failure.
Recoverable failures restore the shared stack boundary; a failed terminal reset
poisons and closes the root.
