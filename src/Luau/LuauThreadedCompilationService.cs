using System.Diagnostics;

namespace Luau;

/// <summary>
/// A bounded compilation backend served by dedicated, long-lived managed
/// workers. Workers never access a Luau state, Unity API, caller buffer, or
/// synchronization context.
/// </summary>
public sealed class LuauThreadedCompilationService : ILuauCompilationService
{
    static int nextServiceId;

    readonly object gate = new();
    readonly LinkedList<CompilationRequest> queue = new();
    readonly Thread[] workers;
    readonly Func<byte[], LuauCompileOptions, int, CancellationToken, LuauCompilerOutput>
        compileBackend;
    readonly TaskCompletionSource<object?> workersExited = new(
        TaskCreationOptions.RunContinuationsAsynchronously);

    int reservedRequestCount;
    long reservedSourceBytes;
    int activeRequestCount;
    int pendingPublicationCount;
    int activeCancellationCallbackCount;
    int remainingWorkerCount;
    bool accepting = true;
    bool shutdownStarted;
    TaskCompletionSource<object?>? allRequestsPublished;
    TaskCompletionSource<object?>? allCancellationCallbacksExited;
    Task? disposeTask;

    /// <summary>
    /// Creates a bounded service and starts its dedicated compiler workers.
    /// The native ABI is verified before any worker starts.
    /// </summary>
    public LuauThreadedCompilationService(LuauThreadedCompilationOptions? options = null)
        : this(
            options,
            static (source, compileOptions, maximumOutputBytes, cancellationToken) =>
                LuauCompiler.CompileOwnedSource(
                    source,
                    compileOptions,
                    maximumOutputBytes,
                    cancellationToken),
            LuauCompiler.EnsureAvailable)
    {
    }

    internal LuauThreadedCompilationService(
        LuauThreadedCompilationOptions? options,
        Func<byte[], LuauCompileOptions, int, LuauCompilerOutput> compileBackend,
        Action? initializeBackend = null)
        : this(
            options,
            compileBackend == null
                ? null
                : (source, compileOptions, maximumOutputBytes, _) =>
                    compileBackend(source, compileOptions, maximumOutputBytes),
            initializeBackend)
    {
    }

    LuauThreadedCompilationService(
        LuauThreadedCompilationOptions? options,
        Func<byte[], LuauCompileOptions, int, CancellationToken, LuauCompilerOutput>?
            compileBackend,
        Action? initializeBackend)
    {
        Options = (options ?? LuauThreadedCompilationOptions.Default).Snapshot();
        this.compileBackend = compileBackend
            ?? throw new ArgumentNullException(nameof(compileBackend));

        // The supported host exposes no fast-flag mutation surface. Eager ABI
        // initialization here establishes all process-global compiler state
        // before concurrent calls become possible.
        initializeBackend?.Invoke();

        var serviceId = Interlocked.Increment(ref nextServiceId);
        workers = new Thread[Options.WorkerCount];
        remainingWorkerCount = workers.Length;
        var startedWorkerCount = 0;
        try
        {
            for (var index = 0; index < workers.Length; index++)
            {
                var worker = new Thread(WorkerMain)
                {
                    IsBackground = true,
                    Name = $"Luau compiler {serviceId}:{index + 1}",
                };
                workers[index] = worker;
                StartWithoutExecutionContext(worker);
                startedWorkerCount++;
            }
        }
        catch
        {
            lock (gate)
            {
                accepting = false;
                shutdownStarted = true;
                remainingWorkerCount = startedWorkerCount;
                Monitor.PulseAll(gate);
                if (startedWorkerCount == 0)
                {
                    workersExited.TrySetResult(null);
                }
            }

            throw;
        }
    }

    static void StartWithoutExecutionContext(Thread worker)
    {
        var restoreFlow = false;
        if (!ExecutionContext.IsFlowSuppressed())
        {
            ExecutionContext.SuppressFlow();
            restoreFlow = true;
        }

        try
        {
            worker.Start();
        }
        finally
        {
            if (restoreFlow)
            {
                ExecutionContext.RestoreFlow();
            }
        }
    }

    /// <summary>Gets the immutable resource policy snapshot used by this service.</summary>
    public LuauThreadedCompilationOptions Options { get; }

    /// <inheritdoc />
    public ValueTask<LuauCompileResult> CompileAsync(
        ReadOnlyMemory<byte> utf8Source,
        LuauCompileOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return new ValueTask<LuauCompileResult>(LuauCompileResult.Canceled());
        }
        if (utf8Source.Length > Options.MaxSourceBytesPerRequest)
        {
            throw new LuauCompilationLimitException(
                LuauCompilationLimitKind.SourceBytesPerRequest,
                utf8Source.Length,
                Options.MaxSourceBytesPerRequest);
        }

        var compileOptions = (options ?? LuauCompileOptions.Default) with { };
        CompilationRequest request;
        var publishCancellation = false;

        lock (gate)
        {
            if (!accepting)
            {
                throw new ObjectDisposedException(nameof(LuauThreadedCompilationService));
            }
            if (cancellationToken.IsCancellationRequested)
            {
                return new ValueTask<LuauCompileResult>(LuauCompileResult.Canceled());
            }

            if (reservedRequestCount >= Options.MaxQueuedRequestCount)
            {
                throw new LuauCompilationLimitException(
                    LuauCompilationLimitKind.QueuedRequestCount,
                    (long)reservedRequestCount + 1,
                    Options.MaxQueuedRequestCount);
            }

            var nextSourceBytes = checked(reservedSourceBytes + utf8Source.Length);
            if (nextSourceBytes > Options.MaxQueuedSourceBytes)
            {
                throw new LuauCompilationLimitException(
                    LuauCompilationLimitKind.QueuedSourceBytes,
                    nextSourceBytes,
                    Options.MaxQueuedSourceBytes);
            }

            // Capacity is checked before this sole read of caller-owned
            // memory. Serializing snapshots under the admission lock prevents
            // concurrent rejected callers from allocating outside the queue's
            // aggregate source-byte bound.
            var sourceSnapshot = utf8Source.ToArray();
            request = new CompilationRequest(
                this,
                sourceSnapshot,
                compileOptions,
                cancellationToken);
            var queueNode = new LinkedListNode<CompilationRequest>(request);

            reservedRequestCount++;
            reservedSourceBytes = nextSourceBytes;
            pendingPublicationCount++;
            request.State = CompilationRequestState.Queued;
            request.QueueNode = queueNode;
            queue.AddLast(queueNode);

            // Registration happens while the queue lock is held and before a
            // worker is pulsed. Synchronous cancellation can reenter this lock
            // and physically remove the request without leaving a tombstone.
            try
            {
                var registration = cancellationToken.UnsafeRegister(
                    static state => ((CompilationRequest)state!).Owner.CancellationCallback(
                        (CompilationRequest)state),
                    request);
                request.CancellationRegistration = registration;
                request.HasCancellationRegistration = true;
                request.RegistrationInitialized = true;
            }
            catch
            {
                if (request.State == CompilationRequestState.Queued)
                {
                    queue.Remove(request.QueueNode!);
                    request.QueueNode = null;
                    MarkCompletedLocked(request);
                }

                request.Source = Array.Empty<byte>();
                ReleaseReservationLocked(request);
                pendingPublicationCount--;
                throw;
            }

            if (request.State == CompilationRequestState.Completed)
            {
                publishCancellation = true;
            }
            else
            {
                Monitor.Pulse(gate);
            }
        }

        if (publishCancellation)
        {
            BeginPublication(request, LuauCompileResult.Canceled());
        }

        return new ValueTask<LuauCompileResult>(request.Completion.Task);
    }

    /// <summary>
    /// Stops admission, cancels queued work, and drains active native calls
    /// without aborting their threads.
    /// </summary>
    public ValueTask DisposeAsync()
    {
        List<CompilationRequest>? canceledRequests = null;
        Task currentDisposeTask;

        lock (gate)
        {
            if (disposeTask != null && !disposeTask.IsFaulted)
            {
                return new ValueTask(disposeTask);
            }

            if (!shutdownStarted)
            {
                accepting = false;
                shutdownStarted = true;
                if (queue.Count != 0)
                {
                    canceledRequests = new List<CompilationRequest>(queue.Count);
                    while (queue.First is { } node)
                    {
                        queue.RemoveFirst();
                        var request = node.Value;
                        request.QueueNode = null;
                        MarkCompletedLocked(request);
                        request.Source = Array.Empty<byte>();
                        canceledRequests.Add(request);
                    }
                }

                Monitor.PulseAll(gate);
            }

            var publicationTask = pendingPublicationCount == 0
                ? Task.CompletedTask
                : (allRequestsPublished ??= new TaskCompletionSource<object?>(
                    TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            var cancellationCallbackTask = activeCancellationCallbackCount == 0
                ? Task.CompletedTask
                : (allCancellationCallbacksExited ??=
                    new TaskCompletionSource<object?>(
                        TaskCreationOptions.RunContinuationsAsynchronously)).Task;
            disposeTask = WaitForWorkersAsync(publicationTask, cancellationCallbackTask);
            currentDisposeTask = disposeTask;
        }

        if (canceledRequests != null)
        {
            foreach (var request in canceledRequests)
            {
                BeginPublication(request, LuauCompileResult.Canceled());
            }
        }

        return new ValueTask(currentDisposeTask);
    }

    async Task WaitForWorkersAsync(
        Task publicationTask,
        Task cancellationCallbackTask)
    {
        var stopwatch = Stopwatch.StartNew();
        using var timeoutCancellation = new CancellationTokenSource();
        var timeoutTask = Task.Delay(Options.ShutdownTimeout, timeoutCancellation.Token);
        var drainTask = Task.WhenAll(
            workersExited.Task,
            publicationTask,
            cancellationCallbackTask);
        if (await Task.WhenAny(drainTask, timeoutTask).ConfigureAwait(false) != drainTask)
        {
            throw CreateShutdownException();
        }

        timeoutCancellation.Cancel();
        await drainTask.ConfigureAwait(false);

        // WorkerExited signals after all service state has been finalized but
        // immediately before the managed thread returns. Join every worker so
        // Unity cannot unload this assembly while an old worker frame remains.
        foreach (var worker in workers)
        {
            var remaining = Options.ShutdownTimeout - stopwatch.Elapsed;
            if (remaining <= TimeSpan.Zero || !worker.Join(remaining))
            {
                throw CreateShutdownException();
            }
        }
    }

    LuauCompilationShutdownException CreateShutdownException()
    {
        int active;
        lock (gate)
        {
            active = activeRequestCount;
        }

        return new LuauCompilationShutdownException(Options.ShutdownTimeout, active);
    }

    void WorkerMain()
    {
        Exception? fatalException = null;
        CompilationRequest? ownedRequest = null;
        try
        {
            while (true)
            {
                CompilationRequest request;
                lock (gate)
                {
                    while (queue.Count == 0 && !shutdownStarted)
                    {
                        Monitor.Wait(gate);
                    }
                    if (queue.Count == 0)
                    {
                        return;
                    }

                    request = queue.First!.Value;
                    queue.RemoveFirst();
                    request.QueueNode = null;
                    request.State = CompilationRequestState.Running;
                    activeRequestCount++;
                    ownedRequest = request;
                }

                if (request.CancellationToken.IsCancellationRequested)
                {
                    CompleteRunningRequest(request, LuauCompileResult.Canceled());
                    ownedRequest = null;
                    continue;
                }

                var result = CompileRequest(request);
                CompleteRunningRequest(request, result);
                ownedRequest = null;
            }
        }
        catch (Exception exception)
        {
            fatalException = exception;
        }
        finally
        {
            WorkerExited(fatalException, ownedRequest);
        }
    }

    LuauCompileResult CompileRequest(CompilationRequest request)
    {
        try
        {
            var output = compileBackend(
                request.Source,
                request.Options,
                Options.MaxBytecodeBytesPerResult,
                request.CancellationToken);
            if (output.BytecodeLength > Options.MaxBytecodeBytesPerResult)
            {
                throw new LuauCompilationLimitException(
                    LuauCompilationLimitKind.BytecodeBytesPerResult,
                    output.BytecodeLength,
                    Options.MaxBytecodeBytesPerResult);
            }

            return LuauCompileResult.Success(output);
        }
        catch (LuauCompilationException diagnostic)
        {
            return LuauCompileResult.Diagnostic(diagnostic);
        }
        catch (OperationCanceledException)
            when (request.CancellationToken.IsCancellationRequested)
        {
            return LuauCompileResult.Canceled();
        }
        catch (Exception exception)
        {
            return LuauCompileResult.InfrastructureFailure(exception);
        }
    }

    void CompleteRunningRequest(CompilationRequest request, LuauCompileResult result)
    {
        lock (gate)
        {
            if (request.State != CompilationRequestState.Running)
            {
                throw new InvalidOperationException("A compiler worker lost ownership of its request.");
            }

            activeRequestCount--;
            if (request.CancellationRequested ||
                request.CancellationToken.IsCancellationRequested ||
                result.Kind == LuauCompileResultKind.Canceled)
            {
                result = LuauCompileResult.Canceled();
            }
            MarkCompletedLocked(request);
            request.Source = Array.Empty<byte>();
        }

        BeginPublication(request, result);
    }

    void CancellationCallback(CompilationRequest request)
    {
        lock (gate)
        {
            activeCancellationCallbackCount++;
        }

        try
        {
            var complete = false;
            lock (gate)
            {
                if (request.State == CompilationRequestState.Queued)
                {
                    if (request.QueueNode == null)
                    {
                        throw new InvalidOperationException(
                            "A queued compilation request has no queue node.");
                    }

                    queue.Remove(request.QueueNode);
                    request.QueueNode = null;
                    MarkCompletedLocked(request);
                    request.Source = Array.Empty<byte>();
                    // UnsafeRegister may invoke this callback synchronously
                    // before it returns its registration. In that case
                    // CompileAsync owns publication after storing it.
                    complete = request.RegistrationInitialized;
                }
                else if (request.State == CompilationRequestState.Running)
                {
                    // Native compilation is deliberately not interrupted. Its
                    // output or failure is discarded when the call returns.
                    request.CancellationRequested = true;
                }
            }

            if (complete)
            {
                PublishFromCancellationCallback(
                    request,
                    LuauCompileResult.Canceled());
            }
        }
        finally
        {
            TaskCompletionSource<object?>? signal = null;
            lock (gate)
            {
                activeCancellationCallbackCount--;
                if (activeCancellationCallbackCount < 0)
                {
                    throw new InvalidOperationException(
                        "Compilation cancellation-callback accounting became negative.");
                }
                if (activeCancellationCallbackCount == 0)
                {
                    signal = allCancellationCallbacksExited;
                }
            }

            signal?.TrySetResult(null);
        }
    }

    static void MarkCompletedLocked(CompilationRequest request)
    {
        if (request.State is not (CompilationRequestState.Queued or CompilationRequestState.Running))
        {
            throw new InvalidOperationException("A compilation request completed more than once.");
        }

        request.State = CompilationRequestState.Completed;
    }

    void ReleaseReservationLocked(CompilationRequest request)
    {
        if (!request.ReservationHeld)
        {
            throw new InvalidOperationException("A compilation reservation was released more than once.");
        }

        request.ReservationHeld = false;
        reservedRequestCount--;
        reservedSourceBytes -= request.ReservedSourceBytes;
        if (reservedRequestCount < 0 || reservedSourceBytes < 0)
        {
            throw new InvalidOperationException("Compilation reservation accounting became negative.");
        }
    }

    void BeginPublication(CompilationRequest request, LuauCompileResult result)
    {
        ValueTask registrationDisposal;
        try
        {
            registrationDisposal = request.HasCancellationRegistration
                ? request.CancellationRegistration.DisposeAsync()
                : default;
        }
        catch (Exception exception)
        {
            FinishPublication(
                request,
                LuauCompileResult.InfrastructureFailure(exception));
            return;
        }

        if (registrationDisposal.IsCompletedSuccessfully)
        {
            FinishPublication(request, result);
            return;
        }

        _ = CompletePublicationAsync(request, result, registrationDisposal);
    }

    async Task CompletePublicationAsync(
        CompilationRequest request,
        LuauCompileResult result,
        ValueTask registrationDisposal)
    {
        try
        {
            await registrationDisposal.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            result = LuauCompileResult.InfrastructureFailure(exception);
        }

        FinishPublication(request, result);
    }

    void FinishPublication(CompilationRequest request, LuauCompileResult result)
    {
        request.HasCancellationRegistration = false;
        lock (gate)
        {
            ReleaseReservationLocked(request);
        }
        request.Completion.TrySetResult(result);

        TaskCompletionSource<object?>? signal = null;
        lock (gate)
        {
            pendingPublicationCount--;
            if (pendingPublicationCount < 0)
            {
                throw new InvalidOperationException(
                    "Compilation publication accounting became negative.");
            }
            if (pendingPublicationCount == 0)
            {
                signal = allRequestsPublished;
            }
        }

        signal?.TrySetResult(null);
    }

    void PublishFromCancellationCallback(
        CompilationRequest request,
        LuauCompileResult result)
    {
        try
        {
            if (request.HasCancellationRegistration)
            {
                // CancellationTokenRegistration.Dispose detects its own
                // callback and unregisters without waiting on this frame.
                request.CancellationRegistration.Dispose();
            }
        }
        catch (Exception exception)
        {
            result = LuauCompileResult.InfrastructureFailure(exception);
        }

        FinishPublication(request, result);
    }

    void WorkerExited(Exception? fatalException, CompilationRequest? ownedRequest)
    {
        List<CompilationRequest>? failedRequests = null;
        LuauCompileResult? ownedResult = null;
        var signalExit = false;
        lock (gate)
        {
            if (ownedRequest != null)
            {
                if (ownedRequest.State == CompilationRequestState.Running)
                {
                    activeRequestCount--;
                    MarkCompletedLocked(ownedRequest);
                    ownedRequest.Source = Array.Empty<byte>();
                }

                if (!ownedRequest.Completion.Task.IsCompleted)
                {
                    ownedResult = ownedRequest.CancellationRequested ||
                        ownedRequest.CancellationToken.IsCancellationRequested
                        ? LuauCompileResult.Canceled()
                        : LuauCompileResult.InfrastructureFailure(
                            fatalException ?? new InvalidOperationException(
                                "A compiler worker exited while it owned a request."));
                }
            }

            remainingWorkerCount--;
            if (remainingWorkerCount < 0)
            {
                return;
            }

            if (fatalException != null && remainingWorkerCount == 0 && !shutdownStarted)
            {
                accepting = false;
                shutdownStarted = true;
                failedRequests = new List<CompilationRequest>(queue.Count);
                while (queue.First is { } node)
                {
                    queue.RemoveFirst();
                    var request = node.Value;
                    request.QueueNode = null;
                    MarkCompletedLocked(request);
                    request.Source = Array.Empty<byte>();
                    failedRequests.Add(request);
                }
            }

            signalExit = remainingWorkerCount == 0;
        }

        if (ownedResult != null)
        {
            BeginPublication(ownedRequest!, ownedResult);
        }
        if (failedRequests != null)
        {
            foreach (var request in failedRequests)
            {
                BeginPublication(
                    request,
                    LuauCompileResult.InfrastructureFailure(fatalException!));
            }
        }
        if (signalExit)
        {
            workersExited.TrySetResult(null);
        }
    }

    internal (int Requests, long SourceBytes, int ActiveRequests) ReservationSnapshot
    {
        get
        {
            lock (gate)
            {
                return (reservedRequestCount, reservedSourceBytes, activeRequestCount);
            }
        }
    }

    sealed class CompilationRequest
    {
        public CompilationRequest(
            LuauThreadedCompilationService owner,
            byte[] source,
            LuauCompileOptions options,
            CancellationToken cancellationToken)
        {
            Owner = owner;
            Source = source;
            ReservedSourceBytes = source.Length;
            Options = options;
            CancellationToken = cancellationToken;
        }

        public LuauThreadedCompilationService Owner { get; }
        public byte[] Source { get; set; }
        public int ReservedSourceBytes { get; }
        public bool ReservationHeld { get; set; } = true;
        public LuauCompileOptions Options { get; }
        public CancellationToken CancellationToken { get; }
        public TaskCompletionSource<LuauCompileResult> Completion { get; } = new(
            TaskCreationOptions.RunContinuationsAsynchronously);
        public CompilationRequestState State { get; set; }
        public LinkedListNode<CompilationRequest>? QueueNode { get; set; }
        public bool CancellationRequested { get; set; }
        public bool RegistrationInitialized { get; set; }
        public CancellationTokenRegistration CancellationRegistration { get; set; }
        public bool HasCancellationRegistration { get; set; }
    }

    enum CompilationRequestState
    {
        Created,
        Queued,
        Running,
        Completed,
    }
}
