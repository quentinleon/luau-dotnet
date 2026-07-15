using System;
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

            var chunkName = Encoding.UTF8.GetBytes(asset.name);
            return state.DoString(asset.AsSpan(), destination, chunkName);
        }

        public static LuauValue[] Execute(this LuauState state, LuauAsset asset)
        {
            if (asset.IsPrecompiled)
            {
                return state.Execute(asset.AsSpan(), asset.name);
            }

            var chunkName = Encoding.UTF8.GetBytes(asset.name);
            return state.DoString(asset.AsSpan(), chunkName);
        }

        public static async ValueTask<int> ExecuteAsync(this LuauState state, LuauAsset asset, Memory<LuauValue> destination, CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return await state.ExecuteAsync(
                    asset.AsMemory(),
                    destination,
                    asset.name.AsMemory(),
                    cancellationToken);
            }

            var chunkName = Encoding.UTF8.GetBytes(asset.name);
            return await state.DoStringAsync(
                asset.AsMemory(),
                destination,
                chunkName,
                cancellationToken: cancellationToken);
        }

        public static async ValueTask<LuauValue[]> ExecuteAsync(this LuauState state, LuauAsset asset, CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return await state.ExecuteAsync(
                    asset.AsMemory(),
                    asset.name.AsMemory(),
                    cancellationToken);
            }

            var chunkName = Encoding.UTF8.GetBytes(asset.name);
            return await state.DoStringAsync(
                asset.AsMemory(),
                chunkName,
                cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Executes a bundled asset whose bytecode provenance has been
        /// established by the host. For precompiled assets this bypasses the
        /// state's normal bytecode policy while preserving byte-size and
        /// execution limits. A size limit is not provenance validation. Never
        /// use this for mod-authored assets.
        /// </summary>
        public static int ExecuteTrusted(
            this LuauState state,
            LuauAsset asset,
            Span<LuauValue> destination)
        {
            return asset.IsPrecompiled
                ? state.ExecuteTrustedBytecode(asset.AsSpan(), destination, asset.name)
                : state.DoString(asset.AsSpan(), destination, Encoding.UTF8.GetBytes(asset.name));
        }

        /// <inheritdoc cref="ExecuteTrusted(LuauState, LuauAsset, Span{LuauValue})"/>
        public static LuauValue[] ExecuteTrusted(this LuauState state, LuauAsset asset)
        {
            return asset.IsPrecompiled
                ? state.ExecuteTrustedBytecode(asset.AsSpan(), asset.name)
                : state.DoString(asset.AsSpan(), Encoding.UTF8.GetBytes(asset.name));
        }

        /// <inheritdoc cref="ExecuteTrusted(LuauState, LuauAsset, Span{LuauValue})"/>
        public static async ValueTask<int> ExecuteTrustedAsync(
            this LuauState state,
            LuauAsset asset,
            Memory<LuauValue> destination,
            CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return await state.ExecuteTrustedBytecodeAsync(
                    asset.AsMemory(),
                    destination,
                    asset.name.AsMemory(),
                    cancellationToken);
            }

            return await state.DoStringAsync(
                asset.AsMemory(),
                destination,
                Encoding.UTF8.GetBytes(asset.name),
                cancellationToken: cancellationToken);
        }

        /// <inheritdoc cref="ExecuteTrusted(LuauState, LuauAsset, Span{LuauValue})"/>
        public static async ValueTask<LuauValue[]> ExecuteTrustedAsync(
            this LuauState state,
            LuauAsset asset,
            CancellationToken cancellationToken = default)
        {
            if (asset.IsPrecompiled)
            {
                return await state.ExecuteTrustedBytecodeAsync(
                    asset.AsMemory(),
                    asset.name.AsMemory(),
                    cancellationToken);
            }

            return await state.DoStringAsync(
                asset.AsMemory(),
                Encoding.UTF8.GetBytes(asset.name),
                cancellationToken: cancellationToken);
        }
    }
}
