using System;
using UnityEngine;

namespace Luau.Unity
{
    public sealed class LuauAsset : ScriptableObject
    {
        [SerializeField] internal bool isPrecompiled;
        [SerializeField] internal byte[] bytes;

#if UNITY_EDITOR
        [SerializeField] internal string text;
#endif

        public bool IsPrecompiled => isPrecompiled;
        public ReadOnlySpan<byte> AsSpan() => bytes;
        public ReadOnlyMemory<byte> AsMemory() => bytes;
    }
}