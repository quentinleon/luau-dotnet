using System.Collections;
using System.Runtime.CompilerServices;

namespace Luau.SourceGenerator;

public readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
    where T : IEquatable<T>
{
    readonly T[]? array;

    public EquatableArray() // for collection literal []
    {
        array = [];
    }

    public EquatableArray(T[] array)
    {
        this.array = (T[])array.Clone();
    }

    public static implicit operator EquatableArray<T>(T[] array)
    {
        return new EquatableArray<T>(array);
    }

    public ref readonly T this[int index]
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => ref array![index];
    }

    public int Length => array?.Length ?? 0;

    public ReadOnlySpan<T> AsSpan()
    {
        return array.AsSpan();
    }

    public ReadOnlySpan<T>.Enumerator GetEnumerator()
    {
        return AsSpan().GetEnumerator();
    }

    IEnumerator<T> IEnumerable<T>.GetEnumerator()
    {
        return (array ?? []).AsEnumerable().GetEnumerator();
    }

    IEnumerator IEnumerable.GetEnumerator()
    {
        return (array ?? []).GetEnumerator();
    }

    public bool Equals(EquatableArray<T> other)
    {
        return AsSpan().SequenceEqual(other.AsSpan());
    }

    public override int GetHashCode()
    {
        unchecked
        {
            var hash = 17;
            foreach (var item in this)
            {
                hash = (hash * 31) + (item is null ? 0 : item.GetHashCode());
            }

            return hash;
        }
    }
}
