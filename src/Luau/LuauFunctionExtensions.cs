namespace Luau;

public static class LuauFunctionExtensions
{
    public static async ValueTask<LuauValue[]> InvokeAsync(this LuauFunction function, LuauValue[] arguments, CancellationToken cancellationToken = default)
    {
        if (function is not LuauScriptFunction scriptFunction)
        {
            throw new InvalidOperationException(
                "Managed callback functions are host capabilities that can only be invoked by Luau code.");
        }

        return await scriptFunction.InvokeWithArgumentsAsync(arguments, cancellationToken).ConfigureAwait(false);
    }
}
