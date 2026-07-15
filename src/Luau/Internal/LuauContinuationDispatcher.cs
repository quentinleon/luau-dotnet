namespace Luau;

/// <summary>
/// Converts the host-facing fire-and-forget scheduler contract into awaitable
/// operations while keeping exceptions inside the originating execution.
/// </summary>
internal static class LuauContinuationDispatcher
{
    internal static ValueTask InvokeAsync(
        ILuauContinuationScheduler? scheduler,
        Action action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (scheduler == null || scheduler.CheckAccess())
        {
            action();
            return default;
        }

        var completion = new TaskCompletionSource<object?>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionContext = ExecutionContext.Capture();
        try
        {
            scheduler.Post(() =>
            {
                void Invoke()
                {
                    try
                    {
                        action();
                        completion.TrySetResult(null);
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                }

                if (executionContext == null)
                {
                    Invoke();
                }
                else
                {
                    ExecutionContext.Run(executionContext, static state => ((Action)state!).Invoke(), (Action)Invoke);
                }
            });
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        return new ValueTask(completion.Task);
    }

    internal static ValueTask<T> InvokeAsync<T>(
        ILuauContinuationScheduler? scheduler,
        Func<T> action)
    {
        if (action == null)
        {
            throw new ArgumentNullException(nameof(action));
        }

        if (scheduler == null || scheduler.CheckAccess())
        {
            return new ValueTask<T>(action());
        }

        var completion = new TaskCompletionSource<T>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var executionContext = ExecutionContext.Capture();
        try
        {
            scheduler.Post(() =>
            {
                void Invoke()
                {
                    try
                    {
                        completion.TrySetResult(action());
                    }
                    catch (Exception exception)
                    {
                        completion.TrySetException(exception);
                    }
                }

                if (executionContext == null)
                {
                    Invoke();
                }
                else
                {
                    ExecutionContext.Run(executionContext, static state => ((Action)state!).Invoke(), (Action)Invoke);
                }
            });
        }
        catch (Exception exception)
        {
            completion.TrySetException(exception);
        }

        return new ValueTask<T>(completion.Task);
    }
}
