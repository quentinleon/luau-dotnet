namespace Luau;

public static class LuauFunctionExtensions
{
    public static async ValueTask<LuauValue[]> InvokeAsync(this LuauFunction function, LuauValue[] arguments, CancellationToken cancellationToken = default)
    {
        function.State.Push(function);

        foreach (var arg in arguments)
        {
            function.State.Push(arg);
        }

        var nResults = await function.InvokeAsync(arguments.Length, cancellationToken);

        if (nResults == 0) return [];

        var result = new LuauValue[nResults];
        for (int i = result.Length - 1; i >= 0; i--)
        {
            result[i] = function.State.Pop();
        }

        return result;
    }
}