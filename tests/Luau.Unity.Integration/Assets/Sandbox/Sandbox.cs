using Luau.Unity;
using UnityEngine;

public class Sandbox : MonoBehaviour
{
    [SerializeField] LuauAsset luauAsset;

    void Start()
    {
        using var state = LuauUnity.CreateState();

        using var script = state.CreateSandboxedThread();
        script.Execute(luauAsset);
    }
}
