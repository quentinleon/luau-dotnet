using Luau;
using Luau.Unity;
using UnityEngine;

public class Sandbox : MonoBehaviour
{
    [SerializeField] LuauAsset luauAsset;

    void Start()
    {
        using var state = LuauState.Create();
        state.OpenLibraries();
        state.OpenRequireLibrary(new ResourcesLuauRequirer
        {
            Aliases =
            {
                ["Resources"] = "."
            }
        });

        state["print"] = state.CreateFunction(state =>
        {
            var args = new LuauValue[state.GetTop()];
            for (int i = 0; i < args.Length; i++)
            {
                args[i] = state.ToValue(-i - 1);
            }

            Debug.Log(string.Join('\t', args));
            return 0;
        });

        state.Execute(luauAsset);
    }
}
