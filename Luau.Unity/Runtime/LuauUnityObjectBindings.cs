using System;
using NumericsVector3 = System.Numerics.Vector3;
using UnityEngine;

namespace Luau.Unity
{
    /// <summary>AOT-safe conversions between Unity and Luau's vector value.</summary>
    public static class LuauUnityValue
    {
        /// <summary>Reads a Luau vector argument as a Unity vector.</summary>
        public static Vector3 ReadVector3(LuauCallContext context, int index)
        {
            var value = context.Read<NumericsVector3>(index);
            return new Vector3(value.X, value.Y, value.Z);
        }

        /// <summary>Returns a Unity vector through Luau's vector value type.</summary>
        public static void ReturnVector3(LuauCallContext context, Vector3 value)
        {
            context.Return(new NumericsVector3(value.x, value.y, value.z));
        }
    }

    /// <summary>Generated Unity capability bindings use this liveness guard.</summary>
    public static class LuauUnityObjectGuard
    {
        /// <summary>
        /// Rejects both a managed null reference and Unity's destroyed-object
        /// fake-null state before generated member dispatch.
        /// </summary>
        public static void ThrowIfDestroyed<T>(T target)
            where T : UnityEngine.Object
        {
            if (ReferenceEquals(target, null))
            {
                throw new ArgumentNullException(nameof(target));
            }
            if (target == null)
            {
                throw new MissingReferenceException(
                    "The Unity object exposed through this Luau capability has been destroyed.");
            }
        }
    }

    /// <summary>
    /// Explicit built-in capability surfaces for the two foundational Unity
    /// object types. Arbitrary components are never discovered by reflection.
    /// </summary>
    public static partial class LuauStateExtensions
    {
        static readonly LuauObjectDescriptor<GameObject> GameObjectDescriptor = new LuauObjectDescriptor<GameObject>(
            "GameObject",
            LuauUnityObjectGuard.ThrowIfDestroyed,
            new[]
            {
                LuauObjectMember<GameObject>.Property(
                    "name",
                    (target, context) => context.Return(target.name),
                    (target, context) => target.name = context.Read<string>(2)),
                LuauObjectMember<GameObject>.Property(
                    "activeSelf",
                    (target, context) => context.Return(target.activeSelf),
                    null),
                LuauObjectMember<GameObject>.Property(
                    "transform",
                    (target, context) =>
                    {
                        using (var handle = CreateHandle(context.State, target.transform))
                        {
                            context.Return(handle);
                        }
                    },
                    null),
                LuauObjectMember<GameObject>.Method(
                    "SetActive",
                    (target, context) => target.SetActive(context.Read<bool>(1))),
            });

        static readonly LuauObjectDescriptor<Transform> TransformDescriptor = new LuauObjectDescriptor<Transform>(
            "Transform",
            LuauUnityObjectGuard.ThrowIfDestroyed,
            new[]
            {
                LuauObjectMember<Transform>.Property(
                    "name",
                    (target, context) => context.Return(target.name),
                    (target, context) => target.name = context.Read<string>(2)),
                LuauObjectMember<Transform>.Property(
                    "position",
                    (target, context) => LuauUnityValue.ReturnVector3(context, target.position),
                    (target, context) => target.position = LuauUnityValue.ReadVector3(context, 2)),
                LuauObjectMember<Transform>.Property(
                    "localPosition",
                    (target, context) => LuauUnityValue.ReturnVector3(context, target.localPosition),
                    (target, context) => target.localPosition = LuauUnityValue.ReadVector3(context, 2)),
                LuauObjectMember<Transform>.Property(
                    "localScale",
                    (target, context) => LuauUnityValue.ReturnVector3(context, target.localScale),
                    (target, context) => target.localScale = LuauUnityValue.ReadVector3(context, 2)),
                LuauObjectMember<Transform>.Property(
                    "gameObject",
                    (target, context) =>
                    {
                        using (var handle = CreateHandle(context.State, target.gameObject))
                        {
                            context.Return(handle);
                        }
                    },
                    null),
                LuauObjectMember<Transform>.Method(
                    "Translate",
                    (target, context) => target.Translate(LuauUnityValue.ReadVector3(context, 1))),
            });

        /// <summary>Creates the package's explicit built-in GameObject capability.</summary>
        public static LuauObjectHandle CreateHandle(this LuauState state, GameObject target)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (ReferenceEquals(target, null))
            {
                throw new ArgumentNullException(nameof(target));
            }

            LuauUnityObjectGuard.ThrowIfDestroyed(target);
            return state.CreateHandle(target, GameObjectDescriptor);
        }

        /// <summary>Creates the package's explicit built-in Transform capability.</summary>
        public static LuauObjectHandle CreateHandle(this LuauState state, Transform target)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (ReferenceEquals(target, null))
            {
                throw new ArgumentNullException(nameof(target));
            }

            LuauUnityObjectGuard.ThrowIfDestroyed(target);
            return state.CreateHandle(target, TransformDescriptor);
        }
    }
}
