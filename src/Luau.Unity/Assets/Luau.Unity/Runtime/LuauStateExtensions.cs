using System;
using System.Buffers;
using System.Text;
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
                return state.Execute(asset.AsSpan(), destination, asset.name);
            }

            Span<byte> buffer = stackalloc byte[asset.name.Length * 3 + 1];
            var count = Encoding.UTF8.GetBytes(asset.name, buffer);
            buffer[count] = 0;
            return state.DoString(asset.AsSpan(), destination, buffer[..count]);
        }

        public static LuauValue[] Execute(this LuauState state, LuauAsset asset)
        {
            if (asset.IsPrecompiled)
            {
                return state.Execute(asset.AsSpan(), asset.name);
            }

            Span<byte> buffer = stackalloc byte[asset.name.Length * 3 + 1];
            var count = Encoding.UTF8.GetBytes(asset.name, buffer);
            buffer[count] = 0;
            return state.DoString(asset.AsSpan(), buffer[..count]);
        }

        public static ValueTask<int> ExecuteAsync(this LuauState state, LuauAsset asset, Memory<LuauValue> destination, CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return state.ExecuteAsync(asset.AsMemory(), destination, cancellationToken);
            }

            var buffer = ArrayPool<byte>.Shared.Rent(asset.name.Length * 3 + 1);
            try
            {
                var count = Encoding.UTF8.GetBytes(asset.name, buffer);
                buffer[count] = 0;
                return state.DoStringAsync(asset.AsMemory(), destination, buffer.AsMemory()[..count], cancellationToken: cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        public static ValueTask<LuauValue[]> ExecuteAsync(this LuauState state, LuauAsset asset, CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return state.ExecuteAsync(asset.AsMemory(), asset.name.AsMemory(), cancellationToken);
            }


            var buffer = ArrayPool<byte>.Shared.Rent(asset.name.Length * 3 + 1);
            try
            {
                var count = Encoding.UTF8.GetBytes(asset.name, buffer);
                buffer[count] = 0;
                return state.DoStringAsync(asset.AsMemory(), buffer.AsMemory()[..count], cancellationToken: cancellationToken);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }
    }
}