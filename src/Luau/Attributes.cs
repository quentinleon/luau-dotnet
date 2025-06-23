
#pragma warning disable CS9113

namespace Luau;

[AttributeUsage(AttributeTargets.Class)]
public sealed class LuauLibraryAttribute(string name) : Attribute;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
public sealed class LuauMemberAttribute(string? name = null) : Attribute;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromLuauStateAttribute : Attribute;

#pragma warning restore CS9113