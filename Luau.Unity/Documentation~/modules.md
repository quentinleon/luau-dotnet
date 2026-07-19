# Module trust domains

`require()` is an opt-in managed capability. Luau.Unity does not link upstream
native Require and does not resolve files, network resources, `Resources`, or
Addressables at runtime.

Load and authenticate a mod package outside the VM, canonicalize it into an
immutable `LuauModuleMap`, and provide that map while creating the root. The map
copies admitted input. Equivalent paths are canonicalized and parent traversal
is rejected.

One root VM is one trust domain. Module instances and retained results are
shared according to that root's resolver/map identity, never by path alone.
Mutually untrusted mods should use separate roots even when they contain the
same canonical module names.

Module policies bound count, admitted source/bytecode, dependency depth,
diagnostics, and retained cached results. Bundle compilation uses the shared
bounded compiler lane. Bundle construction is all-or-nothing: cancellation, a
diagnostic, identity mismatch, or any quota failure cannot expose a partially
installable bundle or mutate an installed resolver.

Use the Unity adapter to compile a source map without constructing or owning a
second compiler service, then install that same-process resolver explicitly:

```csharp
var map = new LuauModuleMap(authenticatedModuleSources);
var bundle = await LuauUnity.CompileModuleBundleAsync(
    map,
    cancellationToken: destroyCancellationToken);

using var root = LuauUnity.CreateState(new LuauUnityOptions
{
    ConfigureHostApis = state => state.OpenRequireLibrary(bundle),
});
```

The bundle is not a persistent artifact and does not grant bytecode trust.

Cycles fail deterministically. Maps and bundles are immutable and have no
in-place replacement API. Create a new map or bundle to obtain a new resolver
identity; use a separate root when isolation or complete version replacement is
required. Closing the root releases all module instances and cache-held
references.
