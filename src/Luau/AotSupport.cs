using System;

namespace AOT
{
    [AttributeUsage(AttributeTargets.Method)]
    internal sealed class MonoPInvokeCallbackAttribute : Attribute
    {
        internal MonoPInvokeCallbackAttribute(Type type)
        {
        }
    }
}
