using System;
using System.Threading;
using System.Threading.Tasks;

namespace Luau.Unity
{
    public static class LuauStateExtensions
    {
        public static int Execute(this LuauState state, LuauAsset asset, Span<LuauValue> destination)
        {
            if (asset.IsPrecompiled)
            {
                return state.Execute(asset.AsSpan(), destination);
            }

            return state.DoString(asset.AsSpan(), destination);
        }

        public static LuauValue[] Execute(this LuauState state, LuauAsset asset)
        {
            if (asset.IsPrecompiled)
            {
                return state.Execute(asset.AsSpan());
            }

            return state.DoString(asset.AsSpan());
        }

        public static ValueTask<int> ExecuteAsync(this LuauState state, LuauAsset asset, Memory<LuauValue> destination, CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return state.ExecuteAsync(asset.AsMemory(), destination, cancellationToken);
            }

            return state.DoStringAsync(asset.AsMemory(), destination, cancellationToken: cancellationToken);
        }

        public static ValueTask<LuauValue[]> ExecuteAsync(this LuauState state, LuauAsset asset, CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return state.ExecuteAsync(asset.AsMemory(), cancellationToken);
            }

            return state.DoStringAsync(asset.AsMemory(), cancellationToken: cancellationToken);
        }
    }
}