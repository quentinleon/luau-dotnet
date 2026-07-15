namespace Luau;

internal sealed class DisposableBag : IDisposable
{
    readonly object gate = new();
    List<IDisposable>? items = [];

    internal int Count
    {
        get
        {
            lock (gate)
            {
                return items?.Count ?? 0;
            }
        }
    }

    public void Add(IDisposable item)
    {
        lock (gate)
        {
            if (items != null)
            {
                items.Add(item);
                return;
            }
        }

        DisposeNoThrow(item);
    }

    public void Clear()
    {
        List<IDisposable>? snapshot;

        lock (gate)
        {
            if (items == null)
            {
                return;
            }

            snapshot = items;
            items = [];
        }

        DisposeAll(snapshot);
    }

    public void Remove(IDisposable item)
    {
        lock (gate)
        {
            items?.Remove(item);
        }
    }

    public void Dispose()
    {
        List<IDisposable>? snapshot;

        lock (gate)
        {
            snapshot = items;
            items = null;
        }

        DisposeAll(snapshot);
    }

    static void DisposeAll(List<IDisposable>? snapshot)
    {
        if (snapshot == null)
        {
            return;
        }

        foreach (var item in snapshot)
        {
            DisposeNoThrow(item);
        }
    }

    static void DisposeNoThrow(IDisposable item)
    {
        try
        {
            item.Dispose();
        }
        catch
        {
            // State shutdown must continue so native resources are not leaked.
        }
    }
}
