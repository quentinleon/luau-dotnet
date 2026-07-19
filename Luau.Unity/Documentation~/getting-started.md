# Getting started

Luau.Unity is a prebuilt managed runtime plus package-owned native plugins for
Windows x64 and Android ARM64/x86_64. Unity 6000.3.19f1 is the reviewed minimum.

## Install the tagged package

Use Package Manager's **Add package from git URL** command:

```text
https://github.com/nuskey8/luau-dotnet.git?path=Luau.Unity#v0.2.0
```

Then import **Getting Started** from the package's Samples tab.

## Create a source asset

Create `hello.luau` under `Assets`:

```luau
return sample.double(21)
```

The importer validates the file as strict UTF-8, checks its byte length before
allocation, and compiles it for authoring diagnostics. It stores source by
default. The Editor import limit is configured under **Project > Luau.Unity**;
runtime mod admission has separate limits.

## Configure and execute

The ordinary lifecycle is:

1. Create one root on the Unity thread.
2. Register only the host APIs the script needs.
3. Sandbox/freeze the root (the Unity facade does this by default).
4. Execute the `LuauAsset` through `ExecuteAsync`.
5. Dispose each result scope before disposing the root.

```csharp
using Luau;
using Luau.Unity;
using UnityEngine;

[LuauLibrary("sample")]
public sealed partial class SampleLibrary
{
    [LuauMember]
    public static int Double(int value) => checked(value * 2);
}

public sealed class HelloLuau : MonoBehaviour
{
    [SerializeField] LuauAsset script;

    async void Start()
    {
        using var root = LuauUnity.CreateState(new LuauUnityOptions
        {
            ConfigureHostApis = state =>
                state.OpenLibrary(new SampleLibrary()),
        });
        using var results = await root.ExecuteAsync(
            script,
            destroyCancellationToken);

        Debug.Log(results[0].Read<int>()); // 42
    }
}
```

Allocating execution APIs return `LuauResultScope`. Primitive values are copied.
Tables, functions, buffers, userdata, and object handles are VM-backed
references owned by the scope; call `Retain()` before disposal when one must
outlive the scope. Thread results are shared cached `LuauState` wrappers; dispose
one separately only after every holder is finished. Dispose the scope
deterministically. The `*Into` APIs are the advanced allocation-free alternative
and make the caller responsible for every managed wrapper written to the
destination.

## Next steps

- Read [execution and trust lanes](execution-and-trust.md) before accepting mods.
- Review [resource limits](resource-limits.md) before changing defaults.
- Use [capability bindings](capability-bindings.md) instead of ambient Unity
  object discovery.
- Treat [persistent artifacts](artifacts.md) as untrusted until authenticated.
