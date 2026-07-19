using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Luau.Unity
{
    public static partial class LuauStateExtensions
    {
        /// <summary>
        /// Executes an asset into caller-owned memory. Source assets compile
        /// synchronously on the calling thread; use <see cref="ExecuteIntoAsync"/>
        /// for the ordinary bounded Unity execution lane.
        /// </summary>
        public static int ExecuteInto(
            this LuauState state,
            LuauAsset asset,
            Span<LuauValue> destination)
        {
            ValidateAssetExecutionArguments(state, asset);
            var assetName = asset.name;
            return asset.contentKind switch
            {
                LuauAssetContentKind.Source =>
                    state.DoStringInto(asset.AsSpan(), destination, Encoding.UTF8.GetBytes(assetName)),
                LuauAssetContentKind.VerifiedBytecode =>
                    ExecuteVerified(state, asset, destination),
                _ => throw InvalidContentKind(asset),
            };
        }

        /// <summary>
        /// Executes an asset and returns an owned result scope. Source assets
        /// compile synchronously on the calling thread; use
        /// <see cref="ExecuteAsync(LuauState,LuauAsset,CancellationToken)"/> for
        /// the ordinary bounded Unity execution lane.
        /// </summary>
        /// <returns>
        /// A scope that owns disposable reference results and must be disposed
        /// before the state. Retain references that must outlive it; shared
        /// child-thread wrappers are caller-managed and disposed separately.
        /// </returns>
        public static LuauResultScope Execute(this LuauState state, LuauAsset asset)
        {
            ValidateAssetExecutionArguments(state, asset);
            var assetName = asset.name;
            return asset.contentKind switch
            {
                LuauAssetContentKind.Source =>
                    state.DoString(asset.AsSpan(), Encoding.UTF8.GetBytes(assetName)),
                LuauAssetContentKind.VerifiedBytecode =>
                    ExecuteVerified(state, asset),
                _ => throw InvalidContentKind(asset),
            };
        }

        /// <summary>
        /// Executes an asset into caller-owned memory. Source compilation uses
        /// Unity's package-owned bounded background lane, while VM execution
        /// starts on the state's configured owner scheduler.
        /// </summary>
        public static ValueTask<int> ExecuteIntoAsync(
            this LuauState state,
            LuauAsset asset,
            Memory<LuauValue> destination,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAssetIntoAsync(
                state,
                asset,
                LuauUnity.CompileAssetSourceAsync,
                destination,
                compileOptions: null,
                cancellationToken,
                executionOptions: null);
        }

        /// <summary>
        /// Executes an asset through Unity's ordinary bounded lane. Source
        /// compilation runs on the package-owned background service and VM
        /// execution starts on the state's configured owner scheduler.
        /// Verified artifacts bypass source compilation.
        /// </summary>
        /// <returns>
        /// A scope that owns disposable reference results and must be disposed
        /// before the state. Retain references that must outlive it; shared
        /// child-thread wrappers are caller-managed and disposed separately.
        /// </returns>
        public static ValueTask<LuauResultScope> ExecuteAsync(
            this LuauState state,
            LuauAsset asset,
            CancellationToken cancellationToken = default)
        {
            return ExecuteAssetAsync(
                state,
                asset,
                LuauUnity.CompileAssetSourceAsync,
                compileOptions: null,
                cancellationToken,
                executionOptions: null);
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
            return state.ExecuteVerifiedBytecodeInto(
                asset.GetVerifiedBytecode(),
                destination,
                asset.name);
        }

        static LuauResultScope ExecuteVerified(LuauState state, LuauAsset asset)
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
