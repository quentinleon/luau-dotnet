#pragma warning disable CS9113

namespace AOT
{
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    internal class MonoPInvokeCallbackAttribute(Type type) : Attribute
    {
    }
}

#pragma warning restore CS9113