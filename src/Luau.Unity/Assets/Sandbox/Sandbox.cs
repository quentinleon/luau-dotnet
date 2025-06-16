using Luau;
using Luau.Unity;
using UnityEngine;
using System;

public class Sandbox : MonoBehaviour
{
    [SerializeField] LuauAsset luauAsset;

    void Start()
    {
        using var state = LuauState.Create();
        state.OpenMathLibrary();

        var results = state.Execute(luauAsset);
        Debug.Log(results[0]);
    }
}
