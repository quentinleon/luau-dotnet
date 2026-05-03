using System.Runtime.InteropServices;
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
            var top = state.GetTop();
            var parts = new string[top];
            for (int i = 1; i <= top; i++)
            {
                parts[i - 1] = ToDisplayString(state, i);
            }

            Debug.Log(string.Join('\t', parts));
            return 0;
        });

        state.Execute(luauAsset);
    }

    static string ToDisplayString(LuauState state, int index)
    {
        unsafe
        {
            var ptr = Luau.Native.NativeMethods.luaL_tolstring(state.AsPointer(), index, null);
            try
            {
                return Marshal.PtrToStringAnsi((nint)ptr) ?? string.Empty;
            }
            finally
            {
                state.Pop();
            }
        }
    }
}
