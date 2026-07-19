using System.Collections.Concurrent;
using System.Runtime.InteropServices;
using Luau.Internal.Interop;

namespace Luau;

internal readonly record struct LuauObjectToken(long ContextId, int Slot, int Generation);

[StructLayout(LayoutKind.Sequential)]
internal struct LuauObjectPayload
{
    internal const ulong ExpectedMagic = 0x4C55415543415032UL; // "LUAUCAP2"

    internal ulong Magic;
    internal long ContextId;
    internal int Slot;
    internal int Generation;

    internal readonly LuauObjectToken Token => new(ContextId, Slot, Generation);
}

internal sealed class LuauObjectRegistry
{
    static readonly ConcurrentDictionary<long, WeakReference<LuauObjectRegistry>> owners = new();
    static long nextContextId;

    readonly object gate = new();
    readonly int? limit;
    // Native userdata destruction is a no-block/no-allocation boundary. Entries
    // therefore live in a concurrently readable index so the destructor can
    // publish only an atomic tombstone; ordinary managed paths retire it under
    // the registry gate.
    readonly ConcurrentDictionary<int, Entry> entries = new();
    readonly Dictionary<int, int> generations = [];
    readonly Stack<int> freeSlots = new();
    readonly Dictionary<LuauObjectDescriptor, LuauObjectBinding> bindings = [];
    Entry? releasedEntries;
    int nextSlot;
    bool closed;

    internal LuauObjectRegistry(int? limit)
    {
        this.limit = limit;
        var id = Interlocked.Increment(ref nextContextId);
        if (id <= 0)
        {
            throw new InvalidOperationException("The process exhausted Luau capability-registry identifiers.");
        }

        ContextId = id;
        owners[id] = new WeakReference<LuauObjectRegistry>(this);
    }

    internal long ContextId { get; }

    internal int Count
    {
        get
        {
            lock (gate)
            {
                DrainNativeReleases();
                return entries.Count;
            }
        }
    }

    internal LuauObjectToken ReserveOrRetain(
        object target,
        LuauObjectDescriptor descriptor,
        out bool retained)
    {
        lock (gate)
        {
            DrainNativeReleases();
            ThrowIfClosed();
            foreach (var pair in entries)
            {
                var entry = pair.Value;
                if (entry.NativeAlive &&
                    entry.Reference >= 0 &&
                    ReferenceEquals(entry.Descriptor, descriptor) &&
                    entry.Target.TryGetTarget(out var candidate) &&
                    ReferenceEquals(candidate, target))
                {
                    entry.WrapperCount = checked(entry.WrapperCount + 1);
                    retained = true;
                    return entry.Token;
                }
            }

            retained = false;
            return ReserveCore(target, descriptor);
        }
    }

    internal LuauObjectToken Reserve(object target, LuauObjectDescriptor descriptor)
    {
        lock (gate)
        {
            DrainNativeReleases();
            ThrowIfClosed();
            return ReserveCore(target, descriptor);
        }
    }

    internal void Activate(LuauObjectToken token, int reference)
    {
        lock (gate)
        {
            DrainNativeReleases();
            var entry = GetEntry(token);
            if (reference < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(reference));
            }
            if (entry.Reference >= 0 || !entry.TryActivate())
            {
                throw new InvalidOperationException("The managed capability registration is already active.");
            }

            entry.Reference = reference;
        }
    }

    internal void CancelReservation(LuauObjectToken token)
    {
        lock (gate)
        {
            DrainNativeReleases();
            if (TryGetEntry(token, out var entry) && !entry.NativeAlive)
            {
                RemoveEntry(entry);
            }
        }
    }

    internal void RollbackActivation(LuauObjectToken token, LuauState state)
    {
        var reference = -1;
        lock (gate)
        {
            DrainNativeReleases();
            if (TryGetEntry(token, out var entry))
            {
                reference = entry.Reference;
                RemoveEntry(entry);
            }
        }

        if (reference >= 0)
        {
            state.TryReleaseReference(reference);
        }
    }

    internal int GetReference(LuauObjectToken token)
    {
        lock (gate)
        {
            DrainNativeReleases();
            var entry = GetEntry(token);
            if (!entry.NativeAlive || entry.Reference < 0)
            {
                throw new ObjectDisposedException(
                    nameof(LuauObjectHandle),
                    "The Luau object capability no longer has a managed reference.");
            }

            return entry.Reference;
        }
    }

    internal void RetainFromStack(LuauObjectToken token, Func<int> createReference)
    {
        lock (gate)
        {
            DrainNativeReleases();
            var entry = GetEntry(token);
            if (!entry.NativeAlive)
            {
                throw new ObjectDisposedException(
                    nameof(LuauObjectHandle),
                    "The Luau object capability has already been collected.");
            }

            if (entry.Reference < 0)
            {
                entry.Reference = createReference();
            }

            entry.WrapperCount = checked(entry.WrapperCount + 1);
        }
    }

    internal void RetainWrapper(LuauObjectToken token)
    {
        lock (gate)
        {
            DrainNativeReleases();
            var entry = GetEntry(token);
            if (!entry.NativeAlive || entry.Reference < 0)
            {
                throw new ObjectDisposedException(nameof(LuauObjectHandle));
            }

            entry.WrapperCount = checked(entry.WrapperCount + 1);
        }
    }

    internal void ReleaseWrapper(LuauObjectToken token, LuauState state)
    {
        var reference = -1;
        lock (gate)
        {
            DrainNativeReleases();
            if (!TryGetEntry(token, out var entry) || entry.WrapperCount == 0)
            {
                return;
            }

            entry.WrapperCount--;
            if (entry.WrapperCount == 0)
            {
                reference = entry.Reference;
                entry.Reference = -1;
            }
        }

        if (reference >= 0)
        {
            state.TryReleaseReference(reference);
        }
    }

    internal object ResolveTarget(LuauObjectToken token, LuauObjectDescriptor descriptor)
    {
        object target;
        lock (gate)
        {
            DrainNativeReleases();
            var entry = GetEntry(token);
            if (!entry.NativeAlive)
            {
                throw new ObjectDisposedException(
                    nameof(LuauObjectHandle),
                    "The Luau object capability has already been collected.");
            }
            if (!ReferenceEquals(entry.Descriptor, descriptor))
            {
                throw new InvalidOperationException(
                    $"The Luau object capability does not grant the '{descriptor.TypeName}' authority.");
            }
            if (!entry.Target.TryGetTarget(out target!))
            {
                throw new ObjectDisposedException(
                    descriptor.TypeName,
                    "The managed target exposed to Luau has been collected.");
            }
        }

        descriptor.ValidateTarget(target);
        return target;
    }

    internal LuauObjectDescriptor ResolveDescriptor(LuauObjectToken token)
    {
        lock (gate)
        {
            DrainNativeReleases();
            var entry = GetEntry(token);
            if (!entry.NativeAlive)
            {
                throw new ObjectDisposedException(nameof(LuauObjectHandle));
            }

            return entry.Descriptor;
        }
    }

    internal LuauObjectBinding GetOrCreateBinding(
        LuauObjectDescriptor descriptor,
        Func<LuauObjectBinding> factory)
    {
        lock (gate)
        {
            DrainNativeReleases();
            ThrowIfClosed();
            if (bindings.TryGetValue(descriptor, out var existing))
            {
                return existing;
            }
        }

        var created = factory();
        var adopted = false;
        try
        {
            lock (gate)
            {
                DrainNativeReleases();
                ThrowIfClosed();
                if (bindings.TryGetValue(descriptor, out var existing))
                {
                    return existing;
                }

                bindings.Add(descriptor, created);
                adopted = true;
                return created;
            }
        }
        finally
        {
            if (!adopted)
            {
                created.Dispose();
            }
        }
    }

    internal static void ReleaseFromNative(LuauObjectToken token)
    {
        if (token.ContextId <= 0 ||
            !owners.TryGetValue(token.ContextId, out var owner) ||
            !owner.TryGetTarget(out var registry))
        {
            return;
        }

        registry.MarkReleasedFromNative(token);
    }

    void MarkReleasedFromNative(LuauObjectToken token)
    {
        if (token.ContextId == ContextId &&
            token.Slot > 0 &&
            entries.TryGetValue(token.Slot, out var entry) &&
            entry.Token.Generation == token.Generation &&
            entry.TryQueueNativeRelease())
        {
            Entry? head;
            do
            {
                head = Volatile.Read(ref releasedEntries);
                entry.NextReleased = head;
            }
            while (Interlocked.CompareExchange(ref releasedEntries, entry, head) != head);
            entry.MarkReleasedFromNative();
            if (Volatile.Read(ref closed))
            {
                Interlocked.Exchange(ref releasedEntries, null);
            }
        }
    }

    internal void Close()
    {
        LuauObjectBinding[] ownedBindings;
        lock (gate)
        {
            if (closed)
            {
                return;
            }

            Volatile.Write(ref closed, true);
            entries.Clear();
            freeSlots.Clear();
            Interlocked.Exchange(ref releasedEntries, null);
            ownedBindings = [.. bindings.Values];
            bindings.Clear();
        }

        owners.TryRemove(ContextId, out _);
        foreach (var binding in ownedBindings)
        {
            binding.Dispose();
        }
    }

    Entry GetEntry(LuauObjectToken token)
    {
        if (!TryGetEntry(token, out var entry))
        {
            throw new ObjectDisposedException(
                nameof(LuauObjectHandle),
                "The Luau object capability token is stale or invalid.");
        }

        return entry;
    }

    bool TryGetEntry(LuauObjectToken token, out Entry entry)
    {
        if (token.ContextId == ContextId &&
            token.Slot > 0 &&
            entries.TryGetValue(token.Slot, out entry!) &&
            entry.Token.Generation == token.Generation)
        {
            return true;
        }

        entry = null!;
        return false;
    }

    void RemoveEntry(Entry entry)
    {
        if (!entries.TryGetValue(entry.Token.Slot, out var current) ||
            !ReferenceEquals(current, entry) ||
            !entries.TryRemove(entry.Token.Slot, out _))
        {
            return;
        }

        freeSlots.Push(entry.Token.Slot);
        entry.MarkRemoved();
        entry.Reference = -1;
        entry.WrapperCount = 0;
    }

    LuauObjectToken ReserveCore(object target, LuauObjectDescriptor descriptor)
    {
        if (limit is { } maximum && entries.Count >= maximum)
        {
            throw new LuauManagedHandleLimitException(maximum);
        }

        var slot = 0;
        while (freeSlots.Count != 0)
        {
            var candidate = freeSlots.Pop();
            generations.TryGetValue(candidate, out var candidateGeneration);
            if (candidateGeneration != int.MaxValue)
            {
                slot = candidate;
                break;
            }
        }

        if (slot == 0)
        {
            slot = checked(++nextSlot);
            if (slot <= 0)
            {
                throw new InvalidOperationException("The Luau state exhausted managed capability slots.");
            }
        }

        generations.TryGetValue(slot, out var generation);
        generation = checked(generation + 1);
        generations[slot] = generation;

        var token = new LuauObjectToken(ContextId, slot, generation);
        if (!entries.TryAdd(slot, new Entry(token, target, descriptor)))
        {
            throw new InvalidOperationException("The Luau capability registry could not reserve a free slot.");
        }

        return token;
    }

    void DrainNativeReleases()
    {
        var entry = Interlocked.Exchange(ref releasedEntries, null);
        while (entry != null)
        {
            var next = entry.NextReleased;
            entry.NextReleased = null;
            RemoveEntry(entry);
            entry = next;
        }
    }

    void ThrowIfClosed()
    {
        if (closed)
        {
            throw new ObjectDisposedException(nameof(LuauState));
        }
    }

    sealed class Entry
    {
        const int Reserved = 0;
        const int Active = 1;
        const int Released = 2;

        int nativeState;
        int nativeReleaseQueued;

        internal Entry(LuauObjectToken token, object target, LuauObjectDescriptor descriptor)
        {
            Token = token;
            Target = new WeakReference<object>(target);
            Descriptor = descriptor;
            WrapperCount = 1;
        }

        internal LuauObjectToken Token { get; }
        internal WeakReference<object> Target { get; }
        internal LuauObjectDescriptor Descriptor { get; }
        internal bool NativeAlive => Volatile.Read(ref nativeState) == Active;
        internal Entry? NextReleased { get; set; }
        internal int Reference { get; set; } = -1;
        internal int WrapperCount { get; set; }

        internal bool TryActivate() =>
            Interlocked.CompareExchange(ref nativeState, Active, Reserved) == Reserved;

        internal bool TryQueueNativeRelease() =>
            Interlocked.Exchange(ref nativeReleaseQueued, 1) == 0;

        internal void MarkReleasedFromNative() =>
            Interlocked.Exchange(ref nativeState, Released);

        internal void MarkRemoved() => Volatile.Write(ref nativeState, Released);
    }
}

internal sealed class LuauObjectBinding : IDisposable
{
    readonly LuauFunction?[] methodFunctions;
    readonly LuauFunction[] dispatchFunctions;
    int disposed;

    internal LuauObjectBinding(
        LuauTable metatable,
        LuauFunction?[] methodFunctions,
        LuauFunction[] dispatchFunctions)
    {
        Metatable = metatable;
        this.methodFunctions = methodFunctions;
        this.dispatchFunctions = dispatchFunctions;
    }

    internal LuauTable Metatable { get; }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
        {
            return;
        }

        foreach (var function in methodFunctions)
        {
            function?.Dispose();
        }
        foreach (var function in dispatchFunctions)
        {
            function.Dispose();
        }
        Metatable.Dispose();
    }
}

internal static unsafe class LuauObjectLifetime
{
    static readonly LuauHostUserdataDestructor destructor = Destroy;

    internal static LuauHostUserdataDestructor Destructor => destructor;

    [AOT.MonoPInvokeCallback(typeof(LuauHostUserdataDestructor))]
    static void Destroy(void* userdata)
    {
        try
        {
            if (userdata == null)
            {
                return;
            }

            var payload = (LuauObjectPayload*)userdata;
            if (payload->Magic == LuauObjectPayload.ExpectedMagic)
            {
                LuauObjectRegistry.ReleaseFromNative(payload->Token);
            }
        }
        catch
        {
            // Native userdata destruction must never unwind into the Luau VM.
        }
    }
}
