using System.Runtime.CompilerServices;
using Luau;

using var state = LuauState.Create();
state.OpenLibraries();
state.OpenRequireLibrary(new FileSystemLuauRequirer
{
    WorkingDirectory = Directory.GetParent(GetCallerFilePath())!.FullName,
});
state.OpenLibrary<Commands>();

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

[LuauLibrary("cmd")]
partial class Commands
{
    [LuauMember]
    public double foo;

    [LuauMember]
    public void Hello()
    {
        Console.WriteLine("Hello!");
    }

    [LuauMember("echo")]
    public static void Echo(string value)
    {
        Console.WriteLine(value);
    }
}