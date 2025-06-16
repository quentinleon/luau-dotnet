namespace Luau;

public enum LuauThreadStatus : byte
{
    Running = 0,
    Suspended = 1,
    Normal = 2,
    Dead = 3,
    Error = 4,
}
