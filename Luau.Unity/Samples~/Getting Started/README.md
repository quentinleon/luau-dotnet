# Getting Started sample

1. Import this sample from Package Manager.
2. Add `GettingStartedSample` to a GameObject.
3. Assign `GettingStarted.luau` to the component's **Script** field.
4. Enter Play Mode and observe `Luau returned 42`.

The component creates a finite root, registers one generated library before the
root is sandboxed, executes through the shared bounded compiler lane, and
disposes both the owned result scope and root.
