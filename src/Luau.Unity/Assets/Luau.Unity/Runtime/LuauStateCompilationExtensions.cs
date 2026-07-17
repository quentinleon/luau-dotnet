using System;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Threading.Tasks;

namespace Luau.Unity
{
    public static partial class LuauStateExtensions
    {
        /// <summary>
        /// Executes compiler-issued output after explicitly dispatching the
        /// operation start to the state's configured owner scheduler.
        /// </summary>
        public static ValueTask<LuauValue[]> ExecuteCompilerOutputOnOwnerThreadAsync(
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

        /// <summary>
        /// Executes compiler-issued output into caller-owned memory after
        /// explicitly dispatching the operation start to the state's owner.
        /// </summary>
        public static ValueTask<int> ExecuteCompilerOutputOnOwnerThreadAsync(
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
                () => state.ExecuteCompilerOutputAsync(
                    output,
                    destination,
                    chunkName,
                    cancellationToken,
                    executionOptions));
        }

        /// <summary>
        /// Compiles a source asset through a host-owned background service,
        /// then executes successful compiler output on the state's owner.
        /// </summary>
        public static async ValueTask<LuauValue[]> ExecuteAsync(
            this LuauState state,
            LuauAsset asset,
            ILuauCompilationService compilationService,
            LuauCompileOptions compileOptions = null,
            CancellationToken cancellationToken = default,
            LuauExecutionOptions executionOptions = null)
        {
            ValidateCompilationExecutionArguments(state, asset, compilationService);
            if (!asset.IsSource)
            {
                ValidateVerifiedPayloadBeforeConstruction(state, asset);
                var verifiedName = asset.name;
                var artifact = asset.GetVerifiedBytecode();
                return await DispatchToOwnerAsync(
                    state,
                    () => state.ExecuteVerifiedBytecodeAsync(
                        artifact,
                        verifiedName.AsMemory(),
                        cancellationToken,
                        executionOptions));
            }

            // Snapshot all Unity-owned data before yielding. Compilation
            // continuations are deliberately free to run on a worker.
            var assetName = asset.name;
            var source = asset.AsMemory();
            ValidateSourceSize(state, source.Length, assetName);
            var result = await compilationService
                .CompileAsync(source, compileOptions, cancellationToken)
                .ConfigureAwait(false);
            var output = GetCompilerOutputOrThrow(result, assetName, cancellationToken);
            return await state.ExecuteCompilerOutputOnOwnerThreadAsync(
                    output,
                    assetName.AsMemory(),
                    cancellationToken,
                    executionOptions)
                .ConfigureAwait(false);
        }

        /// <summary>
        /// Compiles a source asset through a host-owned background service,
        /// then executes successful output into caller-owned memory on the
        /// state's owner.
        /// </summary>
        public static async ValueTask<int> ExecuteAsync(
            this LuauState state,
            LuauAsset asset,
            ILuauCompilationService compilationService,
            Memory<LuauValue> destination,
            LuauCompileOptions compileOptions = null,
            CancellationToken cancellationToken = default,
            LuauExecutionOptions executionOptions = null)
        {
            ValidateCompilationExecutionArguments(state, asset, compilationService);
            if (!asset.IsSource)
            {
                ValidateVerifiedPayloadBeforeConstruction(state, asset);
                var verifiedName = asset.name;
                var artifact = asset.GetVerifiedBytecode();
                return await DispatchToOwnerAsync(
                    state,
                    () => state.ExecuteVerifiedBytecodeAsync(
                        artifact,
                        destination,
                        verifiedName.AsMemory(),
                        cancellationToken,
                        executionOptions));
            }

            // Snapshot all Unity-owned data before yielding. Compilation
            // continuations are deliberately free to run on a worker.
            var assetName = asset.name;
            var source = asset.AsMemory();
            ValidateSourceSize(state, source.Length, assetName);
            var result = await compilationService
                .CompileAsync(source, compileOptions, cancellationToken)
                .ConfigureAwait(false);
            var output = GetCompilerOutputOrThrow(result, assetName, cancellationToken);
            return await state.ExecuteCompilerOutputOnOwnerThreadAsync(
                    output,
                    destination,
                    assetName.AsMemory(),
                    cancellationToken,
                    executionOptions)
                .ConfigureAwait(false);
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

        static void ValidateCompilationExecutionArguments(
            LuauState state,
            LuauAsset asset,
            ILuauCompilationService compilationService)
        {
            if (state == null)
            {
                throw new ArgumentNullException(nameof(state));
            }
            if (asset == null)
            {
                throw new ArgumentNullException(nameof(asset));
            }
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
                case LuauCompileResultKind.Diagnostic when result.Diagnostic != null:
                    ExceptionDispatchInfo.Capture(result.Diagnostic).Throw();
                    break;
                case LuauCompileResultKind.Canceled:
                    throw new OperationCanceledException(
                        "Luau compilation was canceled for '" + chunkName + "'.",
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
    }
}
