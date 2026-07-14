using Luau.Unity;
using UnityEngine;

public class Sandbox : MonoBehaviour
{
    [SerializeField] LuauAsset luauAsset;

    void Start()
    {
        using var state = LuauUnity.CreateState(new LuauUnityOptions
        {
            Requirer = new ResourcesLuauRequirer
            {
                Aliases =
                {
                    ["Resources"] = "."
                }
            }
        });

        state.Execute(luauAsset);
    }
}
