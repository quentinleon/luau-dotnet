using System.Runtime.CompilerServices;
using Luau;

using var state = LuauState.Create();
state.OpenLibraries();
state.OpenRequireLibrary(new FileSystemLuauRequirer());

state["wait"] = state.CreateFunction(async (double seconds, CancellationToken ct) =>
{
    await Task.Delay(TimeSpan.FromSeconds(seconds), ct);
});

var path = Path.Combine(Directory.GetParent(GetCallerFilePath())!.FullName, "main.luau");
var results = state.DoString(File.ReadAllBytes(path));

var co = results[0].Read<LuauState>();

for (int i = 0; i < 10; i++)
{
    var coResults = await co.ResumeAsync();
    Console.WriteLine(coResults[0]);
}

static string GetCallerFilePath([CallerFilePath] string callerFilePath = "")
{
    return callerFilePath;
}