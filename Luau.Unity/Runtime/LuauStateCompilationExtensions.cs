using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Luau.Unity
{
    public static partial class LuauStateExtensions
    {
        /// <summary>
        /// Compiles a source asset through an explicitly caller-owned service,
        /// then starts VM execution on the state's configured owner scheduler.
        /// Verified artifacts bypass the service.
        /// </summary>
        /// <returns>
        /// A scope that owns disposable reference results and must be disposed
        /// before the state. Shared child-thread wrappers are caller-managed and
        /// disposed separately. The compilation service remains caller-owned.
        /// </returns>
        public static ValueTask<LuauResultScope> ExecuteWithCompilationServiceAsync(
            this LuauState state,
            LuauAsset asset,
            ILuauCompilationService compilationService,
            LuauCompileOptions compileOptions = null,
            CancellationToken cancellationToken = default,
            LuauExecutionOptions executionOptions = null)
        {
            ValidateCompilationExecutionArguments(state, asset, compilationService);
            return ExecuteAssetAsync(
                state,
                asset,
                compilationService.CompileAsync,
                compileOptions,
                cancellationToken,
                executionOptions);
        }

        /// <summary>
        /// Compiles a source asset through an explicitly caller-owned service,
        /// then executes it into caller-owned memory on the state's configured
        /// owner scheduler. Verified artifacts bypass the service.
        /// </summary>
        public static ValueTask<int> ExecuteIntoWithCompilationServiceAsync(
            this LuauState state,
            LuauAsset asset,
            ILuauCompilationService compilationService,
            Memory<LuauValue> destination,
            LuauCompileOptions compileOptions = null,
            CancellationToken cancellationToken = default,
            LuauExecutionOptions executionOptions = null)
        {
            ValidateCompilationExecutionArguments(state, asset, compilationService);
            return ExecuteAssetIntoAsync(
                state,
                asset,
                compilationService.CompileAsync,
                destination,
                compileOptions,
                cancellationToken,
                executionOptions);
        }

        internal static ValueTask<LuauResultScope> ExecuteCompilerOutputOnOwnerAsync(
            this LuauState state,
            LuauCompilerOutput output,
            ReadOnlyMemory<char> chunkName = default,
            CancellationToken cancellationToken = default,
            LuauExecutionOptions executionOptions = null)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            return DispatchToOwnerAsync(
                state,
                () => state.ExecuteCompilerOutputAsync(
                    output,
                    chunkName,
                    cancellationToken,
                    executionOptions));
        }

        internal static ValueTask<int> ExecuteCompilerOutputIntoOnOwnerAsync(
            this LuauState state,
            LuauCompilerOutput output,
            Memory<LuauValue> destination,
            ReadOnlyMemory<char> chunkName = default,
            CancellationToken cancellationToken = default,
            LuauExecutionOptions executionOptions = null)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (output == null)
            {
                throw new ArgumentNullException(nameof(output));
            }

            return DispatchToOwnerAsync(
                state,
                () => state.ExecuteCompilerOutputIntoAsync(
                    output,
                    destination,
                    chunkName,
                    cancellationToken,
                    executionOptions));
        }

        static async ValueTask<LuauResultScope> ExecuteAssetAsync(
            LuauState state,
            LuauAsset asset,
            LuauAssetCompilationProvider compilationProvider,
            LuauCompileOptions compileOptions,
            CancellationToken cancellationToken,
            LuauExecutionOptions executionOptions)
        {
            if (compilationProvider == null)
            {
                throw new ArgumentNullException(nameof(compilationProvider));
            }

            var snapshot = SnapshotAssetForAsync(state, asset, cancellationToken);
            if (snapshot.Kind == LuauAssetContentKind.VerifiedBytecode)
            {
                return await DispatchToOwnerAsync(
                        state,
                        () => state.ExecuteVerifiedBytecodeAsync(
                            snapshot.Artifact,
                            snapshot.Name.AsMemory(),
                            cancellationToken,
                            executionOptions))
                    .ConfigureAwait(false);
            }

            var result = await compilationProvider(
                    snapshot.Source,
                    compileOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            var output = GetCompilerOutputOrThrow(
                result,
                snapshot.Name,
                cancellationToken);
            return await state.ExecuteCompilerOutputOnOwnerAsync(
                    output,
                    snapshot.Name.AsMemory(),
                    cancellationToken,
                    executionOptions)
                .ConfigureAwait(false);
        }

        static async ValueTask<int> ExecuteAssetIntoAsync(
            LuauState state,
            LuauAsset asset,
            LuauAssetCompilationProvider compilationProvider,
            Memory<LuauValue> destination,
            LuauCompileOptions compileOptions,
            CancellationToken cancellationToken,
            LuauExecutionOptions executionOptions)
        {
            if (compilationProvider == null)
            {
                throw new ArgumentNullException(nameof(compilationProvider));
            }

            var snapshot = SnapshotAssetForAsync(state, asset, cancellationToken);
            if (snapshot.Kind == LuauAssetContentKind.VerifiedBytecode)
            {
                return await DispatchToOwnerAsync(
                        state,
                        () => state.ExecuteVerifiedBytecodeIntoAsync(
                            snapshot.Artifact,
                            destination,
                            snapshot.Name.AsMemory(),
                            cancellationToken,
                            executionOptions))
                    .ConfigureAwait(false);
            }

            var result = await compilationProvider(
                    snapshot.Source,
                    compileOptions,
                    cancellationToken)
                .ConfigureAwait(false);
            var output = GetCompilerOutputOrThrow(
                result,
                snapshot.Name,
                cancellationToken);
            return await state.ExecuteCompilerOutputIntoOnOwnerAsync(
                    output,
                    destination,
                    snapshot.Name.AsMemory(),
                    cancellationToken,
                    executionOptions)
                .ConfigureAwait(false);
        }

        static LuauAssetExecutionSnapshot SnapshotAssetForAsync(
            LuauState state,
            LuauAsset asset,
            CancellationToken cancellationToken)
        {
            ValidateAssetExecutionArguments(state, asset);
            var assetName = asset.name ?? string.Empty;
            if (cancellationToken.IsCancellationRequested)
            {
                throw new LuauExecutionCanceledException(assetName, cancellationToken);
            }

            switch (asset.contentKind)
            {
                case LuauAssetContentKind.Source:
                    var source = asset.AsMemory();
                    ValidateSourceSize(state, source.Length, assetName);
                    // Never retain Unity-owned serialized memory across an await.
                    return LuauAssetExecutionSnapshot.FromSource(
                        assetName,
                        source.ToArray());
                case LuauAssetContentKind.VerifiedBytecode:
                    ValidateVerifiedPayloadBeforeConstruction(state, asset);
                    return LuauAssetExecutionSnapshot.FromArtifact(
                        assetName,
                        asset.GetVerifiedBytecode());
                default:
                    throw InvalidContentKind(asset);
            }
        }

        static ValueTask<T> DispatchToOwnerAsync<T>(
            LuauState state,
            Func<ValueTask<T>> operation)
        {
            var scheduler = state.Options.DefaultExecutionOptions.ContinuationScheduler;
            if (scheduler == null || scheduler.CheckAccess())
            {
                return operation();
            }

            var completion = new TaskCompletionSource<T>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            try
            {
                scheduler.Post(() => StartDispatchedOperation(operation, completion));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }

            return new ValueTask<T>(completion.Task);
        }

        static void StartDispatchedOperation<T>(
            Func<ValueTask<T>> operation,
            TaskCompletionSource<T> completion)
        {
            try
            {
                var pending = operation();
                if (pending.IsCompletedSuccessfully)
                {
                    completion.TrySetResult(pending.Result);
                    return;
                }

                _ = CompleteDispatchedOperationAsync(pending, completion);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        static async Task CompleteDispatchedOperationAsync<T>(
            ValueTask<T> pending,
            TaskCompletionSource<T> completion)
        {
            try
            {
                completion.TrySetResult(await pending.ConfigureAwait(false));
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        }

        static void ValidateAssetExecutionArguments(
            LuauState state,
            LuauAsset asset)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }
            if (state.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(LuauState));
            }
        }

        static void ValidateCompilationExecutionArguments(
            LuauState state,
            LuauAsset asset,
            ILuauCompilationService compilationService)
        {
            ValidateAssetExecutionArguments(state, asset);
            if (compilationService == null)
            {
                throw new ArgumentNullException(nameof(compilationService));
            }
        }

        static LuauCompilerOutput GetCompilerOutputOrThrow(
            LuauCompileResult result,
            string chunkName,
            CancellationToken cancellationToken)
        {
            if (result == null)
            {
                throw new InvalidOperationException(
                    "The Luau compilation service returned a null result.");
            }

            switch (result.Kind)
            {
                case LuauCompileResultKind.Success when result.Output != null:
                    return result.Output;
                case LuauCompileResultKind.Diagnostic
                    when result.CompilationDiagnostic != null:
                    if (string.IsNullOrEmpty(chunkName))
                    {
                        ExceptionDispatchInfo.Capture(result.CompilationDiagnostic).Throw();
                    }
                    throw new LuauCompilationException(
                        chunkName + ": " + result.CompilationDiagnostic.Message,
                        chunkName,
                        result.CompilationDiagnostic);
                case LuauCompileResultKind.Canceled:
                    throw new LuauExecutionCanceledException(
                        chunkName,
                        cancellationToken);
                case LuauCompileResultKind.InfrastructureFailure
                    when result.InfrastructureException != null:
                    ExceptionDispatchInfo.Capture(result.InfrastructureException).Throw();
                    break;
            }

            throw new InvalidOperationException(
                "The Luau compilation service returned an inconsistent " + result.Kind + " result.");
        }

        static void ValidateSourceSize(LuauState state, int sourceBytes, string chunkName)
        {
            var limit = state.Options.MaxSourceBytes;
            if (limit.HasValue && sourceBytes > limit.Value)
            {
                throw new LuauLoadLimitException(
                    chunkName,
                    LuauLoadInputKind.Source,
                    sourceBytes,
                    limit.Value);
            }
        }

        readonly struct LuauAssetExecutionSnapshot
        {
            LuauAssetExecutionSnapshot(
                LuauAssetContentKind kind,
                string name,
                byte[] source,
                LuauBytecodeArtifact artifact)
            {
                Kind = kind;
                Name = name;
                Source = source;
                Artifact = artifact;
            }

            internal LuauAssetContentKind Kind { get; }
            internal string Name { get; }
            internal ReadOnlyMemory<byte> Source { get; }
            internal LuauBytecodeArtifact Artifact { get; }

            internal static LuauAssetExecutionSnapshot FromSource(
                string name,
                byte[] source)
            {
                return new LuauAssetExecutionSnapshot(
                    LuauAssetContentKind.Source,
                    name,
                    source,
                    null);
            }

            internal static LuauAssetExecutionSnapshot FromArtifact(
                string name,
                LuauBytecodeArtifact artifact)
            {
                return new LuauAssetExecutionSnapshot(
                    LuauAssetContentKind.VerifiedBytecode,
                    name,
                    null,
                    artifact);
            }
        }
    }
}
