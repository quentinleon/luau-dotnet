
#pragma warning disable CS9113

namespace Luau;

/// <summary>Chooses how a generated Luau host API grants authority.</summary>
public enum LuauLibraryExposure
{
    /// <summary>Generates a root-registered global host library.</summary>
    Global = 0,

    /// <summary>Generates an opaque, per-object capability descriptor.</summary>
    Capability = 1,
}

/// <summary>Marks a partial class for reflection-free Luau host API generation.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LuauLibraryAttribute(string name) : Attribute
{
    /// <summary>
    /// Gets or sets whether the class is a global library or an object
    /// capability. One annotated class cannot implicitly grant both surfaces.
    /// </summary>
    public LuauLibraryExposure Exposure { get; set; }
}

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Property | AttributeTargets.Method)]
public sealed class LuauMemberAttribute(string? name = null) : Attribute;

[AttributeUsage(AttributeTargets.Parameter)]
public sealed class FromLuauStateAttribute : Attribute;

#pragma warning restore CS9113
