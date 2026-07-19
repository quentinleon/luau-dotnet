using Luau;
using Luau.Unity;
using UnityEngine;

namespace Luau.Unity.Samples.CapabilityBinding
{
    public sealed class CapabilityBindingSample : MonoBehaviour
    {
        [SerializeField]
        LuauAsset script;

        [SerializeField]
        GameObject target;

        async void Start()
        {
            if (script == null || target == null)
            {
                Debug.LogError("Assign both the Luau script and an explicit target.", this);
                return;
            }

            using var root = LuauUnity.CreateState();
            using var sandbox = root.CreateSandboxedThread();
            using var targetHandle = root.CreateHandle(target);
            sandbox["target"] = targetHandle;

            using var results = await sandbox.ExecuteAsync(
                script,
                destroyCancellationToken);
            Debug.Log("Luau renamed the explicit target to " + results[0].Read<string>(), this);
        }
    }
}
