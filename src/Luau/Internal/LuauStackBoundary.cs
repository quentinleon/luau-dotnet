namespace Luau;

/// <summary>
/// Owns restoration of one synchronous managed/native stack boundary.
/// </summary>
internal ref struct LuauStackBoundary
{
    readonly LuauState state;
    readonly int baseTop;
    bool completed;

    internal LuauStackBoundary(LuauState state)
        : this(state, state.GetTop())
    {
    }

    internal LuauStackBoundary(LuauState state, int baseTop)
    {
        this.state = state;
        this.baseTop = baseTop;
        completed = false;
    }

    internal int BaseTop => baseTop;

    internal void Complete()
    {
        if (state.GetTop() != baseTop)
        {
            throw new InvalidOperationException(
                "A Luau operation did not consume its managed stack boundary.");
        }

        completed = true;
    }

    internal void Abandon()
    {
        completed = true;
    }

    internal void Restore()
    {
        if (completed)
        {
            return;
        }

        state.SetTop(baseTop);
        completed = true;
    }

    public void Dispose()
    {
        if (completed || state.IsDisposed)
        {
            return;
        }

        Restore();
    }
}
