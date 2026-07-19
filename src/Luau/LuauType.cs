namespace Luau;

/// <summary>Identifies the Luau VM type represented by a <see cref="LuauValue"/>.</summary>
public enum LuauType : byte
{
    /// <summary>The singleton nil value.</summary>
    Nil,
    /// <summary>A Boolean value.</summary>
    Boolean,
    /// <summary>An unmanaged light-userdata pointer.</summary>
    LightUserData,
    /// <summary>A floating-point number.</summary>
    Number,
    /// <summary>An integer number.</summary>
    Integer,
    /// <summary>A three-component vector.</summary>
    Vector,
    /// <summary>An immutable UTF-8 string value.</summary>
    String,
    /// <summary>A table reference whose lifetime is tied to its managed wrapper owner.</summary>
    Table,
    /// <summary>A callable function reference whose lifetime is tied to its managed wrapper owner.</summary>
    Function,
    /// <summary>A userdata reference whose lifetime is tied to its managed wrapper owner.</summary>
    UserData,
    /// <summary>A coroutine thread owned by its root state.</summary>
    Thread,
    /// <summary>A mutable byte buffer reference whose lifetime is tied to its managed wrapper owner.</summary>
    Buffer,
}
