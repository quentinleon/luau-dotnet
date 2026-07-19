namespace Luau;

/// <summary>Registers an explicitly generated host library with a Luau state.</summary>
public interface ILuauLibrary
{
    /// <summary>
    /// Registers this library with <paramref name="state"/>. The state owns
    /// the resulting VM registrations; disposing the library does not revoke
    /// authority already granted to that state.
    /// </summary>
    /// <param name="state">The live root state that receives the library.</param>
    void RegisterTo(LuauState state);
}
