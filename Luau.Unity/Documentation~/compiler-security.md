# Compiler security and residual risk

The official Luau compiler executes as native code inside the Unity process.
The package bounds request, queue, source, output, worker, and diagnostic sizes,
and tests hostile inputs, determinism, cancellation edges, and sanitizer/fuzzer
corpora.

Those controls limit admission and resource retention. They do not turn an
in-process native compiler call into a hard security boundary:

- a native crash terminates the process;
- a native hang cannot be preempted safely;
- the wall-clock execution budget applies to VM execution, not a native compile
  already in progress;
- compiler intermediate allocations are not fully described by output limits;
- cancellation discards output after the native call returns.

Version 0.2.0 explicitly accepts this residual in-process compiler risk. The
package does not add a second native product, desktop compiler CLI, filesystem
resolver, or killable worker process. A future process-isolation feature needs
a demonstrated supported-platform requirement, a reviewed IPC/artifact trust
boundary, and separate performance/security evidence.

Hosts receiving arbitrary remote content should still authenticate package
structure, cap downloads and decompression, validate strict UTF-8, use the
shared bounded compilation lane, isolate mutually untrusted mods in separate
roots, expose minimal capabilities, and preserve an application-level watchdog
outside the Unity process where availability is security-critical.
