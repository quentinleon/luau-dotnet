using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Luau.Unity
{
    public static partial class LuauStateExtensions
    {
        public static int Execute(this LuauState state, LuauAsset asset, Span<LuauValue> destination)
        {
            return asset.contentKind switch
            {
                LuauAssetContentKind.Source =>
                    state.DoString(asset.AsSpan(), destination, Encoding.UTF8.GetBytes(asset.name)),
                LuauAssetContentKind.VerifiedBytecode =>
                    ExecuteVerified(state, asset, destination),
                _ => throw InvalidContentKind(asset),
            };
        }

        public static LuauValue[] Execute(this LuauState state, LuauAsset asset)
        {
            return asset.contentKind switch
            {
                LuauAssetContentKind.Source =>
                    state.DoString(asset.AsSpan(), Encoding.UTF8.GetBytes(asset.name)),
                LuauAssetContentKind.VerifiedBytecode =>
                    ExecuteVerified(state, asset),
                _ => throw InvalidContentKind(asset),
            };
        }

        public static async ValueTask<int> ExecuteAsync(this LuauState state, LuauAsset asset, Memory<LuauValue> destination, CancellationToken cancellationToken = default)
        {
            if (asset.contentKind == LuauAssetContentKind.VerifiedBytecode)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new LuauExecutionCanceledException(asset.name, cancellationToken);
                ValidateVerifiedPayloadBeforeConstruction(state, asset);
                return await state.ExecuteVerifiedBytecodeAsync(
                    asset.GetVerifiedBytecode(),
                    destination,
                    asset.name.AsMemory(),
                    cancellationToken);
            }
            if (!asset.IsSource)
                throw InvalidContentKind(asset);

            var chunkName = Encoding.UTF8.GetBytes(asset.name);
            return await state.DoStringAsync(
                asset.AsMemory(),
                destination,
                chunkName,
                cancellationToken: cancellationToken);
        }

        public static async ValueTask<LuauValue[]> ExecuteAsync(this LuauState state, LuauAsset asset, CancellationToken cancellationToken = default)
        {
            if (asset.contentKind == LuauAssetContentKind.VerifiedBytecode)
            {
                if (cancellationToken.IsCancellationRequested)
                    throw new LuauExecutionCanceledException(asset.name, cancellationToken);
                ValidateVerifiedPayloadBeforeConstruction(state, asset);
                return await state.ExecuteVerifiedBytecodeAsync(
                    asset.GetVerifiedBytecode(),
                    asset.name.AsMemory(),
                    cancellationToken);
            }
            if (!asset.IsSource)
                throw InvalidContentKind(asset);

            var chunkName = Encoding.UTF8.GetBytes(asset.name);
            return await state.DoStringAsync(
                asset.AsMemory(),
                chunkName,
                cancellationToken: cancellationToken);
        }

        static InvalidOperationException InvalidContentKind(LuauAsset asset)
        {
            return new InvalidOperationException(
                $"Luau asset '{asset.name}' has unknown serialized content kind " +
                $"{(int)asset.contentKind}.");
        }

        static int ExecuteVerified(
            LuauState state,
            LuauAsset asset,
            Span<LuauValue> destination)
        {
            ValidateVerifiedPayloadBeforeConstruction(state, asset);
            return state.ExecuteVerifiedBytecode(
                asset.GetVerifiedBytecode(),
                destination,
                asset.name);
        }

        static LuauValue[] ExecuteVerified(LuauState state, LuauAsset asset)
        {
            ValidateVerifiedPayloadBeforeConstruction(state, asset);
            return state.ExecuteVerifiedBytecode(asset.GetVerifiedBytecode(), asset.name);
        }

        static void ValidateVerifiedPayloadBeforeConstruction(
            LuauState state,
            LuauAsset asset)
        {
            if (state.Options.BytecodePolicy == LuauBytecodePolicy.Reject)
            {
                var prefix = string.IsNullOrEmpty(asset.name) ? string.Empty : asset.name + ": ";
                throw new LuauException(
                    prefix + "Persistent bytecode artifacts are disabled for this state.",
                    asset.name);
            }

            if (state.Options.BytecodePolicy != LuauBytecodePolicy.RequireValidator)
                throw new InvalidOperationException("Unknown bytecode policy.");

            var limit = state.Options.MaxBytecodeBytes;
            if (limit is { } maxBytecodeBytes && asset.PayloadLength > maxBytecodeBytes)
            {
                throw new LuauLoadLimitException(
                    asset.name,
                    LuauLoadInputKind.Bytecode,
                    asset.PayloadLength,
                    maxBytecodeBytes);
            }
        }

    }
}
