using System.Diagnostics;
using System.Runtime.InteropServices;
using ConsoleAppFramework;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using ZLinq;

var app = ConsoleApp.Create();
app.Add("", Commands.Repl);
app.Add("analyze", Commands.Analyze);
app.Add("ast", Commands.Ast);
app.Add("compile", Commands.Compile);
app.Add("dluau", Commands.GenerateLuauDeclarationFile);
app.Run(args);

static class Commands
{
    public static async Task<int> Repl([Argument] params string[] args)
    {
        return await RunProcessAsync("luau", args);
    }

    public static async Task<int> Analyze([Argument] params string[] args)
    {
        return await RunProcessAsync("luau-analyze", args);
    }

    public static async Task<int> Ast([Argument] params string[] args)
    {
        return await RunProcessAsync("luau-ast", args);
    }

    public static async Task<int> Compile([Argument] params string[] args)
    {
        return await RunProcessAsync("luau-compile", args);
    }

    static async Task<int> RunProcessAsync(string toolName, string[] args)
    {
        var currentAssemblyLocation = AppContext.BaseDirectory;
        var ext = RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? ".exe" : "";

        string toolPath;
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            toolPath = Path.Combine(currentAssemblyLocation, "tools", "win", toolName + ext);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {
            toolPath = Path.Combine(currentAssemblyLocation, "tools", "linux", toolName + ext);
        }
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            toolPath = Path.Combine(currentAssemblyLocation, "tools", "osx", toolName + ext);
        }
        else
        {
            Console.WriteLine("Unsupported operating system");
            return 1;
        }

        var startInfo = new ProcessStartInfo
        {
            FileName = toolPath,
            Arguments = string.Join(' ', args),
            UseShellExecute = true,
        };

        using var process = new Process { StartInfo = startInfo };
        process.Start();
        await process.WaitForExitAsync();
        return process.ExitCode;
    }

    /// <summary>
    /// Analyzes C# files in the project and generates .d.luau file
    /// </summary>
    /// <param name="path">The path of the file or directory</param>
    /// <param name="output">-o|Output path</param>
    /// <returns></returns>
    public static int GenerateLuauDeclarationFile([Argument] string path, string? output = null)
    {
        var outputPath = output ?? Path.Combine(
            Path.GetDirectoryName(path)!,
            Path.GetFileNameWithoutExtension(path) + ".d.luau");

        if (File.Exists(path))
        {
            var writer = File.CreateText(outputPath);
            writer.Write(CreateLuauDeclarationFileCore(path));
            writer.Flush();
            return 0;
        }
        else if (Directory.Exists(path))
        {
            var writer = File.CreateText(outputPath);

            foreach (var info in new DirectoryInfo(path)
                .DescendantsAndSelf()
                .OfType<FileInfo>()
                .Where(x => x.Extension is ".cs"))
            {
                writer.Write(CreateLuauDeclarationFileCore(info.FullName));
            }

            writer.Flush();
            return 0;
        }
        else
        {
            Console.WriteLine($"File or directory '{path}' not found.");
            return 1;
        }
    }

    static string CreateLuauDeclarationFileCore(string filePath)
    {
        var text = File.ReadAllText(filePath);
        var tree = CSharpSyntaxTree.ParseText(text, path: filePath);

        var builder = new CodeBuilder(0);

        foreach (var node in tree.GetRoot().DescendantNodes())
        {
            if (node is not TypeDeclarationSyntax declaration) continue;

            var libraryAttribute = GetAttributes(declaration).FirstOrDefault(x => x.Name.ToString() is "LuauLibrary" or "Luau.LuauLibrary" or "global::Luau.LuauLibrary");
            if (libraryAttribute == null) continue;

            var libraryName = libraryAttribute.ArgumentList!.Arguments[0].ToString().Replace("\"", "");

            using (builder.BeginBlock($"declare {libraryName}:"))
            {
                foreach (var member in declaration.Members)
                {
                    var memberAttribute = GetAttributes(member).FirstOrDefault(x => x.Name.ToString() is "LuauMember" or "Luau.LuauMember" or "global::Luau.LuauMember");
                    if (memberAttribute == null) continue;

                    var luauMemberName = memberAttribute.ArgumentList?.Arguments[0].ToString().Replace("\"", "");
                    switch (member)
                    {
                        case FieldDeclarationSyntax field:
                            var fieldName = luauMemberName ?? field.Declaration.Variables.First().Identifier.Text;
                            builder.AppendLine($"{fieldName}: {LuauTypeHelper.GetLuauType(field.Declaration.Type)},");
                            break;
                        case PropertyDeclarationSyntax property:
                            var propertyName = luauMemberName ?? property.Identifier.Text;
                            builder.AppendLine($"{propertyName}: {LuauTypeHelper.GetLuauType(property.Type)},");
                            break;
                        case MethodDeclarationSyntax method:
                            var methodName = luauMemberName ?? method.Identifier.Text;
                            var parameters = method.ParameterList.Parameters
                                .Select(x => (Name: x.Identifier.Text, Type: LuauTypeHelper.GetLuauType(x.Type!)))
                                .Select(x => $"{x.Name}: {x.Type}");
                            builder.AppendLine($"{methodName}: ({string.Join(", ", parameters)}) -> {LuauTypeHelper.GetLuauType(method.ReturnType)},");
                            break;
                    }
                }
            }
        }

        return builder.ToString();
    }

    static AttributeSyntax[] GetAttributes(MemberDeclarationSyntax syntax)
    {
        return syntax.AttributeLists
            .AsValueEnumerable()
            .SelectMany(x => x.Attributes)
            .ToArray();
    }
}