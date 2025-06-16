using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Luau.Native;
using static Luau.Native.NativeMethods;

namespace Luau;

public unsafe partial class LuauState : IDisposable, ILuauReference
{
    static readonly ConcurrentDictionary<IntPtr, LuauState> cache = new();

    lua_State* l;
    LuauState? from;
    int reference;
    DisposableBag disposables;

    public bool IsDisposed => l == null || (from != null && from.IsDisposed);
    public bool IsMainThread => lua_mainthread(l) == l;

    int ILuauReference.Reference => reference;
    LuauState ILuauReference.State => from ?? this;

    internal LuauState? From => from;

    internal ScriptRunner? Runner { get; set; }

    internal void RegisterDisposable(IDisposable disposable)
    {
        disposables.Add(disposable);
    }

    public static LuauState Create()
    {
        var l = luaL_newstate();
        return CreateStateInternal(l);
    }

    internal static LuauState GetCachedState(lua_State* l)
    {
        if (!cache.TryGetValue((IntPtr)l, out var state))
        {
            state = new LuauState(l, null, -1);
            cache.TryAdd((IntPtr)l, state);
        }

        return state;
    }

    internal static LuauState CreateStateInternal(lua_State* l)
    {
        return CreateStateInternal(l, null, -1);
    }

    internal static LuauState CreateStateInternal(lua_State* l, lua_State* from, int reference)
    {
        if (cache.ContainsKey((IntPtr)l))
        {
            throw new InvalidOperationException();
        }

        var state = new LuauState(l, from == null ? null : GetCachedState(from), reference);
        cache.TryAdd((IntPtr)l, state);
        return state;
    }

    LuauState(lua_State* l, LuauState? from, int reference)
    {
        this.l = l;
        this.from = from;
        this.reference = reference;
    }

    public lua_State* AsPointer()
    {
        ThrowIfDisposed();
        return l;
    }

    public LuauThreadStatus GetStatus()
    {
        ThrowIfDisposed();
        return (LuauThreadStatus)lua_status(l);
    }

    public LuauState GetMainThread()
    {
        return GetCachedState(lua_mainthread(l));
    }

    public override unsafe string ToString()
    {
        ThrowIfDisposed();
        return LuauReferenceHelper.RefToString(this, reference);
    }

    public void Dispose()
    {
        DisposeCore();
        GC.SuppressFinalize(this);
    }

    void DisposeCore()
    {
        if (IsDisposed) return;
        disposables.Dispose();

        cache.Remove((IntPtr)l, out _);

        if (from != null) lua_close(l);
        else lua_unref(l, reference);

        l = null;
        from = null;
    }

    ~LuauState()
    {
        DisposeCore();
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal void ThrowIfDisposed()
    {
        if (IsDisposed) ThrowHelper.ThrowObjectDisposedException(nameof(LuauState));
    }
}