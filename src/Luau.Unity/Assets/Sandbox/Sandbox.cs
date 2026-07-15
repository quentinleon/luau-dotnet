using Luau.Unity;
using UnityEngine;

public class Sandbox : MonoBehaviour
{
    [SerializeField] LuauAsset luauAsset;

    void Start()
    {
        using var state = LuauUnity.CreateState(new LuauUnityOptions
        {
            EnableRequire = true,
            Requirer = new ResourcesLuauRequirer
            {
                Aliases =
                {
                    ["Resources"] = "."
                }
            }
        });

        using var script = state.CreateSandboxedThread();
        // This sample asset is bundled with the application. Mod-provided
        // Addressables must use Execute so the state's bytecode policy applies.
        script.ExecuteTrusted(luauAsset);
    }
}
