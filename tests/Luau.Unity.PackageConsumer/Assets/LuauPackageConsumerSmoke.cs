using System.Collections.Generic;
using Luau.Unity;
using UnityEngine;

public sealed class LuauPackageConsumerSmoke : MonoBehaviour
{
    [SerializeField] LuauAsset script;

    void Awake()
    {
        var modules = new LuauModuleMap(new Dictionary<string, byte[]>());
        using var state = LuauUnity.CreateState(new LuauUnityOptions
        {
            CaptureUnitySynchronizationContext = false,
            ModuleMap = modules,
            Log = _ => { },
        });

        if (script != null)
        {
            using var thread = state.CreateSandboxedThread();
            thread.Execute(script);
        }
    }
}
