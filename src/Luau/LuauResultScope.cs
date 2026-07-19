using System.Collections;

namespace Luau;

/// <summary>
/// Owns the table, function, buffer, userdata, and object-handle results
/// produced by one allocating Luau operation. Dispose the scope
/// deterministically; call <c>Retain</c> on one of those wrappers when it must
/// outlive the scope. Primitive values require no cleanup. A returned
/// <see cref="LuauState"/> thread is a cached caller-owned wrapper rather than
/// a scope-owned result and must be disposed separately.
/// </summary>
public sealed class LuauResultScope : IReadOnlyList<LuauValue>, IDisposable
{
    LuauValue[]? values;

    internal LuauResultScope(LuauValue[] values)
    {
        this.values = values ?? throw new ArgumentNullException(nameof(values));
    }

    /// <summary>Gets the number of returned values.</summary>
    public int Count => GetValues().Length;

    /// <summary>Gets the number of returned values.</summary>
    public int Length => Count;

    /// <summary>Gets a returned value by zero-based index.</summary>
    public LuauValue this[int index] => GetValues()[index];

    /// <summary>
    /// Reads one result using the standard managed conversion rules. A child
    /// <see cref="LuauState"/> returned here remains caller-owned and must be
    /// disposed separately from this scope.
    /// </summary>
    public T Read<T>(int index) => this[index].Read<T>();

    /// <summary>Gets a read-only span over the callback-independent values.</summary>
    public ReadOnlySpan<LuauValue> AsSpan() => GetValues();

    /// <summary>
    /// Releases all disposable results still owned by this scope. Cached
    /// <see cref="LuauState"/> thread wrappers are not scope-owned. Disposal
    /// is idempotent.
    /// </summary>
    public void Dispose()
    {
        var owned = Interlocked.Exchange(ref values, null);
        if (owned == null)
        {
            return;
        }

        for (var index = owned.Length - 1; index >= 0; index--)
        {
            owned[index].DisposeOwnedReference();
            owned[index] = default;
        }
    }

    /// <summary>Returns an enumerator over the result values.</summary>
    public IEnumerator<LuauValue> GetEnumerator() =>
        ((IEnumerable<LuauValue>)GetValues()).GetEnumerator();

    IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();

    internal LuauValue Detach(int index)
    {
        var owned = GetValues();
        var value = owned[index];
        owned[index] = default;
        return value;
    }

    LuauValue[] GetValues()
    {
        return Volatile.Read(ref values)
            ?? throw new ObjectDisposedException(nameof(LuauResultScope));
    }
}
