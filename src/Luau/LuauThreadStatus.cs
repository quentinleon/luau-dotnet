namespace Luau;

/// <summary>
/// Describes the managed lifecycle state of a child Luau coroutine.
/// Root states do not have a coroutine lifecycle status.
/// </summary>
public enum LuauThreadStatus : byte
{
    /// <summary>The child is freshly created or suspended at a yield.</summary>
    Suspended = 0,

    /// <summary>The child is currently executing through a managed operation.</summary>
    Running = 1,

    /// <summary>The child completed, failed, or was reset after a hard stop.</summary>
    Dead = 2,
}
