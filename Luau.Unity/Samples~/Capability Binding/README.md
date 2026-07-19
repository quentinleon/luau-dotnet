# Capability Binding sample

1. Import this sample from Package Manager.
2. Add `CapabilityBindingSample` to a GameObject.
3. Assign `CapabilityBinding.luau` and a target GameObject.
4. Enter Play Mode.

The script can rename and translate only the serialized target. The host creates
an explicit root-owned capability; it never searches the scene or exposes
arbitrary components.
