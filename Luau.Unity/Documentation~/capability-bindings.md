# Capability bindings

Host authority is explicit. `[LuauLibrary]` and `[LuauMember]` are the only
generated binding model; generation is deterministic, AOT-safe, and
reflection-free.

## Global libraries

Register a generated global library in `LuauUnityOptions.ConfigureHostApis`.
Registration happens before the facade sandboxes and freezes the root.

```csharp
[LuauLibrary("clock")]
public sealed partial class ClockLibrary
{
    [LuauMember]
    public static double Realtime() => Time.realtimeSinceStartupAsDouble;
}
```

Unannotated members are not reachable from Luau. Generated diagnostics reject
unsupported signatures and inaccessible annotated members at compile time.

## Object capabilities

An object capability grants one reviewed descriptor for one managed target. It
does not expose a pointer, registry token, `GCHandle`, arbitrary userdata
constructor, component search, `Resources` lookup, or scene discovery.

Luau.Unity includes explicit descriptors for `GameObject` and `Transform`:

```csharp
using var handle = root.CreateHandle(targetGameObject);
thread["target"] = handle;
```

The handle is scoped to the creating root. Access fails after its target is
collected, after a `UnityEngine.Object` is destroyed, or after the root closes.
The finite `MaxManagedHandleCount` root option bounds live registrations.

Import the **Capability Binding** sample for a complete example. It binds a
serialized `GameObject`; it never searches the active scene.

## Callback ownership

`LuauCallContext` and table, function, buffer, userdata, or object-handle
arguments read from it are borrowed, generation-checked views. They fail after
callback invalidation. Call `Retain()` only when the host must create an owned
reference that survives callback return, and dispose that owner later. A
`LuauState` thread argument is instead the VM's shared cached wrapper and remains
valid after callback return; dispose it only after every holder is finished.

`Return<T>` pushes a value into Luau but never transfers or disposes the managed
owner. Generated properties and methods follow the same rule. A library that
returns a persistent wrapper keeps it live; a callback that creates a temporary
owned wrapper must dispose that owner at the point its host lifetime ends.

Callbacks expose zero-based `Read<T>`, `Return<T>`, cancellation, and bounded
diagnostics. Raw native handles and mutable stack access are intentionally not
public.
