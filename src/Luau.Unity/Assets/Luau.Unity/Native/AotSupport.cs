using System;

namespace AOT
{
    /// <summary>
    /// Marker recognized by Unity IL2CPP for reverse P/Invoke entry points.
    /// It lives in the low-level interop assembly so both the host bindings
    /// and the managed runtime use one unambiguous definition.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        public MonoPInvokeCallbackAttribute(Type type)
        {
        }
    }
}
